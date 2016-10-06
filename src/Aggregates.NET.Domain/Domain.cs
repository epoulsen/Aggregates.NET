﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using NServiceBus.Pipeline;
using NServiceBus.Config.ConfigurationSource;
using NServiceBus.Config;
using Aggregates.Exceptions;

namespace Aggregates
{
    public class Domain : ConsumerFeature
    {
        public Domain() : base()
        {
            Defaults(s =>
            {
                s.SetDefault("ShouldCacheEntities", false);
                s.SetDefault("MaxConflictResolves", 3);
                s.SetDefault("StreamGenerator", new StreamIdGenerator((type, bucket, stream) => $"{bucket}.[{type.FullName}].{stream}"));
                s.SetDefault("WatchConflicts", false);
                s.SetDefault("ClaimThreshold", 5);
                s.SetDefault("ExpireConflict", TimeSpan.FromMinutes(1));
                s.SetDefault("ClaimLength", TimeSpan.FromMinutes(10));
                s.SetDefault("CommonalityRequired", 0.9M);
            });
        }
        protected override void Setup(FeatureConfigurationContext context)
        {
            base.Setup(context);

            context.RegisterStartupTask(() => new DomainStart(context.Settings));

            context.Container.ConfigureComponent<UnitOfWork>(DependencyLifecycle.InstancePerUnitOfWork);
            context.Container.ConfigureComponent<DefaultRepositoryFactory>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<DefaultRouteResolver>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<Processor>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<MemoryStreamCache>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<Func<Accept>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return () => { return eventFactory.CreateInstance<Accept>(); };
            }, DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent<Func<String, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (message) => { return eventFactory.CreateInstance<Reject>(e => { e.Message = message; }); };
            }, DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<Func<BusinessException, Reject>>(y =>
            {
                var eventFactory = y.Build<IMessageCreator>();
                return (exception) => {
                    return eventFactory.CreateInstance<Reject>(e => {
                        e.Message = "Exception raised";
                    });
                };
            }, DependencyLifecycle.SingleInstance);


            var settings = context.Settings;
            context.Pipeline.Register(
                behavior: typeof(CommandAcceptor),
                description: "Filters [BusinessException] from processing failures"
                );
            context.Pipeline.Register(
                behavior: new CommandUnitOfWork(settings.Get<Int32>("SlowAlertThreshold")),
                description: "Begins and Ends command unit of work"
                );
            context.Pipeline.Register(
                behavior: typeof(MutateIncomingCommands),
                description: "Running command mutators for incoming messages"
                );

            if(settings.Get<Boolean>("WatchConflicts"))
                context.Pipeline.Register(
                    behavior: new BulkCommandBehavior(settings.Get<Int32>("ClaimThreshold"), settings.Get<TimeSpan>("ExpireConflict"),settings.Get<TimeSpan>("ClaimLength"), settings.Get<Decimal>("CommonalityRequired"), settings.EndpointName(), settings.InstanceSpecificQueue()),
                    description: "Watches commands for many version conflict exceptions, when found it will claim the command type with a byte mask to run only on 1 instance reducing eventstore conflicts considerably"
                    );

            //context.Pipeline.Register<SafetyNetRegistration>();
            //context.Pipeline.Register<TesterBehaviorRegistration>();
        }

        public class DomainStart : FeatureStartupTask
        {
            private static readonly ILog Logger = LogManager.GetLogger(typeof(DomainStart));

            private readonly ReadOnlySettings _settings;

            public DomainStart(ReadOnlySettings settings)
            {
                _settings = settings;
            }

            protected override async Task OnStart(IMessageSession session)
            {
                Logger.Write(LogLevel.Debug, "Starting domain");
                await session.Publish<Messages.DomainAlive>(x =>
                {
                    x.Endpoint = _settings.InstanceSpecificQueue();
                    x.Instance = Aggregates.Defaults.Instance;
                }).ConfigureAwait(false);

            }
            protected override async Task OnStop(IMessageSession session)
            {
                Logger.Write(LogLevel.Debug, "Stopping domain");
                await session.Publish<Messages.DomainDead>(x =>
                {
                    x.Endpoint = _settings.InstanceSpecificQueue();
                    x.Instance = Aggregates.Defaults.Instance;
                }).ConfigureAwait(false);
            }
        }
    }
}