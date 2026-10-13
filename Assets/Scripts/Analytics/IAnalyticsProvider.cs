using System.Collections.Generic;

namespace JewelryMiniFactory.Analytics
{
    public interface IAnalyticsProvider
    {
        void SendEvent(string eventName, IReadOnlyDictionary<string, object> parameters);
    }
}
