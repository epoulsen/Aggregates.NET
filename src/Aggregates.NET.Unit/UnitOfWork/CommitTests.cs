﻿using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class CommitTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private Moq.Mock<IRepository<_AggregateStub<Guid>>> _guidRepository;
        private Moq.Mock<IRepository<_AggregateStub<Int32>>> _intRepository;
        private Moq.Mock<IRepositoryFactory> _repoFactory;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();
            _bus = new Moq.Mock<IBus>();
            _guidRepository = new Moq.Mock<IRepository<_AggregateStub<Guid>>>();
            _intRepository = new Moq.Mock<IRepository<_AggregateStub<Int32>>>();
            _guidRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>())).Verifiable();
            _intRepository.Setup(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>())).Verifiable();

            _repoFactory.Setup(x => x.ForAggregate<_AggregateStub<Guid>>(Moq.It.IsAny<IBuilder>())).Returns(_guidRepository.Object);
            _repoFactory.Setup(x => x.ForAggregate<_AggregateStub<Int32>>(Moq.It.IsAny<IBuilder>())).Returns(_intRepository.Object);

            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _repoFactory.Object);
        }

        [Test]
        public void Commit_no_events()
        {
            Assert.DoesNotThrow(() => _uow.Commit());
        }

        [Test]
        public void Commit_one_repo()
        {
            var repo = _uow.For<_AggregateStub<Guid>>();
            Assert.DoesNotThrow(() => _uow.Commit());
            _guidRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>()), Moq.Times.Once);
        }

        [Test]
        public void Commit_multiple_repo()
        {
            var repo = _uow.For<_AggregateStub<Guid>>();
            var repo2 = _uow.For<_AggregateStub<Int32>>();
            Assert.DoesNotThrow(() => _uow.Commit());
            _guidRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>()), Moq.Times.Once);
            _intRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>()), Moq.Times.Once);
        }

        [Test]
        public void end_calls_commit()
        {
            var repo = _uow.For<_AggregateStub<Guid>>();
            _uow.End();
            _guidRepository.Verify(x => x.Commit(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IDictionary<String, Object>>()), Moq.Times.Once);
        }
    }
}