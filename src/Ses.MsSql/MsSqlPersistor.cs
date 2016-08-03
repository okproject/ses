﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ses.Abstracts;

namespace Ses.MsSql
{
    internal class MsSqlPersistor : IEventStreamPersistor
    {
        private readonly string _connectionString;

        public MsSqlPersistor(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task<IList<IEvent>> Load(Guid streamId, int fromVersion, bool pessimisticLock, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }

        public Task DeleteStream(Guid streamId, int expectedVersion, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }

        public Task AddSnapshot(Guid streamId, int version, string contractName, byte[] payload, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }

        public Func<Guid, string, byte[], IEvent> OnReadEvent { get; set; }

        public Func<Guid, string, int, byte[], IRestoredMemento> OnReadSnapshot { get; set; }

        public Task SaveChanges(Guid streamId, Guid commitId, int expectedVersion, IEnumerable<EventRecord> events, byte[] metadata, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }
    }
}
