using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Ses.Abstracts.Subscriptions;
using Ses.Subscriptions;

namespace Ses.Samples.Subscriptions
{
    [DataContract(Name = "ProjectionsSubscriptionPooler")]
    public class ProjectionsSubscriptionPooler : SubscriptionPooler
    {
        public ProjectionsSubscriptionPooler(ISubscriptionEventSource[] sources) : base(sources)
        {
            
        }

        protected override IEnumerable<Type> FindHandlerTypes()
        {
            return typeof(SampleRunner).Assembly.GetTypes().Where(x => x.Namespace != null && x.Namespace.EndsWith("Projections"));
        }

        protected override IHandle CreateHandlerInstance(Type handlerType)
        {
            return Activator.CreateInstance(handlerType) as IHandle; // usually use IContainer
        }

        public override TimeSpan? RunForDuration => TimeSpan.FromMinutes(5); // to run again use: subscriber.RunPooler(typeof(DenormalizersSubscriptionPooler));

        protected override bool LogStats => true;
    }
}