﻿using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Aggregates
{
    /// <summary>
    /// Keeps track of the domain events it handles and imposes a limit on the amount of events to process (allowing other instances to process others)
    /// Used for load balancing
    /// </summary>
    public class CompetingSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CompetingSubscriber));
        private readonly IEventStoreConnection _client;
        private readonly IPersistCheckpoints _store;
        private readonly IManageCompetes _competes;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly HashSet<String> _domains;
        private Meter _fullMeter = Metric.Meter("Queue Full Exceptions", Unit.Errors);

        public CompetingSubscriber(IEventStoreConnection client, IPersistCheckpoints store, IManageCompetes competes, IDispatcher dispatcher, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _client = client;
            _store = store;
            _competes = competes;
            _dispatcher = dispatcher;
            _settings = settings;
            _jsonSettings = jsonSettings;
            _domains = new HashSet<String>();
        }

        public void SubscribeToAll(String endpoint)
        {
            var saved = _store.Load(endpoint);
            // To support HA simply save IManageCompetes data to a different db, in this way we can make clusters of consumers
            var maxDomains = _settings.Get<Int32?>("HandledDomains") ?? Int32.MaxValue;

            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from position '{1}'", endpoint, saved);
            _client.SubscribeToAllFrom(saved, false, (subscription, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);

                // If the event doesn't contain a domain header it was not generated by the domain
                String domain;
                if (!descriptor.Headers.TryGetValue(Aggregates.Defaults.DomainHeader, out domain))
                    return;

                if (!_domains.Contains(domain))
                {
                    if (_domains.Count >= maxDomains)
                        return;
                    else
                    {
                        // Returns true if it claimed the domain
                        if (_competes.CheckOrSave(endpoint, domain))
                            _domains.Add(domain);
                        else
                            return;
                    }
                }

                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;

                try
                {
                    _dispatcher.Dispatch(data, descriptor);
                    // Todo: Shouldn't save position here, event isn't actually processed yet
                    if (e.OriginalPosition.HasValue)
                        _store.Save(endpoint, e.OriginalPosition.Value);
                }
                catch (QueueFullException)
                {
                    _fullMeter.Mark();
                    // If the queue fills up take a break from dispatching and hope its ready when we continue
                    subscription.Stop(TimeSpan.FromSeconds(15));

                    ThreadPool.QueueUserWorkItem((_) =>
                    {
                        Thread.Sleep(15000);
                        SubscribeToAll(endpoint);
                    });
                }

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
            });
        }
    }
}