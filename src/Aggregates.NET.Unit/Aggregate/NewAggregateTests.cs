﻿using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Aggregates.Unit.Aggregate
{
    [TestFixture]
    public class NewAggregateTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<IRouteResolver> _resolver;
        private IUnitOfWork _uow;
        private Guid _id;

        [SetUp]
        public void Setup()
        {
            _id = Guid.NewGuid();
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _stream = new Moq.Mock<IEventStream>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _resolver = new Moq.Mock<IRouteResolver>();

            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<CreatedEvent>>())).Returns<Action<CreatedEvent>>((e) => { var ev = new CreatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<UpdatedEvent>>())).Returns<Action<UpdatedEvent>>((e) => { var ev = new UpdatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(typeof(CreatedEvent))).Returns(new CreatedEvent());
            _eventFactory.Setup(x => x.CreateInstance(typeof(UpdatedEvent))).Returns(new UpdatedEvent());

            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<_AggregateStub>(), typeof(CreatedEvent))).Returns<_AggregateStub, Type>((agg, type) => (@event) => (agg as _AggregateStub).Handle(@event as CreatedEvent));
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<_AggregateStub>(), typeof(UpdatedEvent))).Returns<_AggregateStub, Type>((agg, type) => (@event) => (agg as _AggregateStub).Handle(@event as UpdatedEvent));

            _store.Setup(x => x.GetSnapshot<_AggregateStub>(Moq.It.IsAny<String>()));
            _store.Setup(x => x.GetStream<_AggregateStub>(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32?>())).Returns(_stream.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(_resolver.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);
            _stream.Setup(x => x.StreamId).Returns(String.Format("{0}", _id));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.Events).Returns(new List<IWritableEvent>());

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, new DefaultRepositoryFactory());
        }

        [Test]
        public void new_aggregate_stream_id()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            Assert.False(String.IsNullOrEmpty(root.StreamId));
        }

        [Test]
        public void new_aggregate_with_id()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            Assert.AreEqual(root.Id, _id);
        }

        [Test]
        public void new_aggregate_version_0()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            Assert.AreEqual(root.Version, 0);
        }

        [Test]
        public void new_aggregate_has_value_set()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            Assert.AreEqual("test", root.Value);
        }

        [Test]
        public void new_aggregate_throw_event()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            root.Update("Updated");
            Assert.AreEqual("Updated", root.Value);
        }


        [Test]
        public void new_aggregate_with_bucket()
        {
            //_stream.Setup(x => x.BucketId).Returns("test");
            var root = _uow.For<_AggregateStub>().New("test", _id);
            root.Create(_id, "test");

            Assert.AreEqual(root.BucketId, "test");
        }
    }
}