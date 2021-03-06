﻿using System;
using System.Collections.Generic;
using Ses.Abstracts;

namespace Ses.Domain
{
    /// <summary>
    /// Base functionality for concrete aggregate
    /// </summary>
    public abstract class Aggregate : IAggregate
    {
        private readonly List<IEvent> _uncommittedEvents = new List<IEvent>(3);

        /// <summary>
        /// Replay existing events to get current state
        /// </summary>
        /// <param name="history">Events from history</param>
        protected void RestoreFrom(IEvent[] history)
        {
            if (history == null || history.Length == 0) return;
            var snapshot = history[0] as IRestoredMemento;
            if (snapshot != null)
            {
                CommittedVersion = snapshot.Version;
                RestoreFromSnapshot(snapshot.State);
            }

            for (var i = snapshot == null ? 0 : 1; i < history.Length; i++)
            {
                Invoke(history[i]);
                CommittedVersion++;
            }
        }

        public void Restore(Guid id, IEvent[] history)
        {
            Id = id;
            RestoreFrom(history);
        }

        /// <summary>
        /// Returns snapshot from current state.
        /// </summary>
        /// <returns>Snapshot from current state</returns>
        public virtual IAggregateSnapshot GetSnapshot()
        {
            return null;
        }

        protected virtual void RestoreFromSnapshot(IMemento memento) { }

        /// <summary>
        /// Returns aggregate identifier
        /// </summary>
        public Guid Id { get; protected set; }

        /// <summary>
        /// Returns committed aggregate version
        /// </summary>
        public int CommittedVersion { get; protected set; }

        /// <summary>
        /// Returns aggregate current version (committed + uncommitted)
        /// </summary>
        public int CurrentVersion => CommittedVersion + _uncommittedEvents.Count;

        /// <summary>
        /// Returns new events registered during one scope of changes.
        /// </summary>
        /// <returns>Collection of events</returns>
        public IEvent[] TakeUncommittedEvents()
        {
            var events = _uncommittedEvents.ToArray();
            _uncommittedEvents.Clear();
            return events;
        }

        /// <summary>
        /// Apply a new event and add to pending events to be committed to event store
        /// when transaction completes.
        /// </summary>
        /// <param name="event">An event which should be applied</param>
        protected void Apply(IEvent @event)
        {
            Invoke(@event);
            _uncommittedEvents.Add(@event);
        }

        /// <summary>
        /// Invoke handler to change state of aggregate in response to event.
        /// Event may be an old event from the event store, or may be an event triggered
        /// during the lifetime of this instance.
        /// </summary>
        /// <param name="event">An event which should be invoked</param>
        protected internal virtual void Invoke(IEvent @event)
        {
            RedirectToWhen.InvokeEventOptional(this, @event);
        }
    }
}
