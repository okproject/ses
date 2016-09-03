using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Ses.Abstracts.Contracts;
using Ses.Abstracts.Subscriptions;

namespace Ses.Subscriptions
{
    public abstract class SubscriptionPooler : ISubscriptionPooler
    {
        private readonly TransactionOptions _transactionOptions = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
        private readonly HandlerRegistrar _handlerRegistrar;
        private readonly IDictionary<ISubscriptionEventSource, int> _contractSubscriptions;
        private string _poolerContractName;

        protected SubscriptionPooler(ISubscriptionEventSource[] sources)
        {
            Sources = sources;
            _handlerRegistrar = new HandlerRegistrar(FindHandlerTypes());
            _contractSubscriptions = new Dictionary<ISubscriptionEventSource, int>(Sources.Length);
        }

        public ISubscriptionEventSource[] Sources { get; }
        public virtual TimeSpan? RunForDuration => null;
        public virtual TimeSpan GetFetchTimeout() => TimeSpan.Zero;
        protected abstract IEnumerable<Type> FindHandlerTypes();
        protected abstract IHandle CreateHandlerInstance(Type handlerType);
        protected virtual IEnumerable<Type> GetConcreteSubscriptionEventTypes() => null;

        internal IEnumerable<Type> GetRegisteredHanlders() => _handlerRegistrar.RegisteredHandlerTypes;

        internal async Task OnStart(IContractsRegistry contractsRegistry)
        {
            _poolerContractName = contractsRegistry.GetContractName(GetType());
            var eventTypes = GetConcreteSubscriptionEventTypes();
            if (eventTypes == null) return;

            var contractNames = eventTypes.Select(contractsRegistry.GetContractName).ToArray();

            foreach (var source in Sources)
            {
                var id = await source.CreateSubscriptionForContracts(_poolerContractName, contractNames);
                _contractSubscriptions.Add(source, id);
            }
        }

        internal async Task<bool> Execute(PoolerContext ctx, CancellationToken cancellationToken = default(CancellationToken))
        {
            var anyDispatched = false;
            try
            {
                var poolerStates = await ctx.StateRepository.Load(_poolerContractName, cancellationToken);
                var timeline = await FetchEventTimeline(ctx, poolerStates);

                foreach (var item in timeline)
                {
                    foreach (var handlerType in _handlerRegistrar.RegisteredHandlerTypes) // all handlers can/should run in parallel
                    {
                        var state = FindOrCreateState(ctx.ContractsRegistry, poolerStates, item.SourceType, handlerType);
                        if (item.Envelope.SequenceId > state.EventSequenceId)
                        {
                            try
                            {
                                anyDispatched = await TryDispatch(ctx, handlerType, item.Envelope, state);
                            }
                            catch (Exception ex)
                            {
                                PostHandleEventException(item.Envelope, handlerType, ex);
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ctx.Logger.Error(e.ToString());
            }
            return anyDispatched;
        }

        private async Task<bool> TryDispatch(PoolerContext ctx, Type handlerType, EventEnvelope envelope, PoolerState state)
        {
            var shouldDispatch = IsHandlerFor(handlerType, envelope);
            if (shouldDispatch) PreHandleEvent(envelope, handlerType);

            using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, _transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
            {
                if (shouldDispatch)
                {
                    var handlerInstance = CreateHandlerInstance(handlerType);
                    if (handlerInstance == null) throw new NullReferenceException($"Handler instance {handlerType.FullName} is null.");
                    ctx.Logger.Trace("Dispatching event {0} to {1}...", envelope.Event.GetType().FullName, handlerType.FullName);
                    ((dynamic)handlerInstance).Handle((dynamic)envelope.Event, envelope);
                }
                state.EventSequenceId = envelope.SequenceId;
                await ctx.StateRepository.InsertOrUpdate(state);
                scope.Complete();
            }
            if (shouldDispatch) PostHandleEvent(envelope, handlerType);
            return true;
        }

        protected virtual void PreHandleEvent(EventEnvelope envelope, Type handlerType) { }
        protected virtual void PostHandleEvent(EventEnvelope envelope, Type handlerType) { }
        protected virtual void PostHandleEventException(EventEnvelope envelope, Type handlerType, Exception e) { }

        private bool IsHandlerFor(Type handlerType, EventEnvelope envelope)
        {
            return _handlerRegistrar.GetRegisteredEventTypesFor(handlerType).Contains(envelope.Event.GetType());
        }

        private PoolerState FindOrCreateState(IContractsRegistry contractsRegistry, IEnumerable<PoolerState> poolerStates, Type sourceType, Type handlerType)
        {
            var sourceContractName = contractsRegistry.GetContractName(sourceType);
            var handlerContractName = contractsRegistry.GetContractName(handlerType);

            var state = poolerStates.FirstOrDefault(x => x.HandlerContractName == handlerContractName && x.SourceContractName == sourceContractName)
                ?? new PoolerState(_poolerContractName, sourceContractName, handlerContractName);
            return state;
        }

        private async Task<IList<ExtractedEvent>> FetchEventTimeline(PoolerContext ctx, IReadOnlyCollection<PoolerState> poolerStates)
        {
            var tasks = new List<Task<IList<ExtractedEvent>>>(Sources.Length);
            foreach (var source in Sources)
            {
                var minSequenceId = GetMinSequenceIdFor(ctx.ContractsRegistry, poolerStates, source);
                ctx.Logger.Trace("Min sequence id for {0} is {1}", _poolerContractName, minSequenceId);

                var concreteSubscriptionIdentifier = _contractSubscriptions.Count > 0 && _contractSubscriptions.ContainsKey(source)
                    ? _contractSubscriptions[source]
                    : (int?)null;
                var task = source.Fetch(ctx.ContractsRegistry, minSequenceId, concreteSubscriptionIdentifier);
                tasks.Add(task);
            }

            var events = await Task.WhenAll(tasks.ToArray());
            var merged = Merge(events);
            ctx.Logger.Trace("Fetched {0} events from {1} stream sources.", merged.Count, Sources.Length);
            return merged;
        }

        private static IList<ExtractedEvent> Merge(IList<IList<ExtractedEvent>> list)
        {
            if (list.Count == 0) return new List<ExtractedEvent>(0);
            if (list.Count == 1) return list[0];

            var merged = list[0];
            for (var i = 1; i < list.Count; i++)
            {
                merged = merged
                    .MergeSorted(list[i], (l1, l2) => l1.Envelope.CreatedAtUtc > l2.Envelope.CreatedAtUtc ? 1 : -1)
                    .ToList();
            }

            return merged;
        }

        private static long GetMinSequenceIdFor(IContractsRegistry contractsRegistry, IEnumerable<PoolerState> poolerStates, ISubscriptionEventSource source)
        {
            var sourceContractName = contractsRegistry.GetContractName(source.GetType());
            long? min = null;
            foreach (var x in poolerStates)
            {
                if (x.SourceContractName != sourceContractName) continue;
                if (!min.HasValue || min.Value > x.EventSequenceId) min = x.EventSequenceId;
            }
            return min ?? 0;
        }
    }
}