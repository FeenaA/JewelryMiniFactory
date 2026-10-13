using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace JewelryMiniFactory.Analytics
{
    public sealed class AnalyticsService
    {
        private readonly IAnalyticsProvider[] providers;

        public AnalyticsService(params IAnalyticsProvider[] providers)
        {
            this.providers = providers == null ? new IAnalyticsProvider[0] : (IAnalyticsProvider[])providers.Clone();
        }

        public void Track(string eventName, IDictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return;
            var snapshot = new ReadOnlyDictionary<string, object>(parameters == null
                ? new Dictionary<string, object>() : new Dictionary<string, object>(parameters));
            foreach (var provider in providers)
            {
                if (provider == null) continue;
                try { provider.SendEvent(eventName, snapshot); }
                catch (Exception)
                {
                    // Analytics is optional: a provider failure must not affect gameplay or other providers.
                }
            }
        }
    }
}
