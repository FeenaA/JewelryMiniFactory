using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace JewelryMiniFactory.Analytics
{
    public sealed class ConsoleAnalyticsProvider : IAnalyticsProvider
    {
        public void SendEvent(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            var values = string.Join(", ", parameters.Select(item =>
                item.Key + "=" + System.Convert.ToString(item.Value, CultureInfo.InvariantCulture)));
            Debug.Log("[Analytics] " + eventName + " {" + values + "}");
        }
    }
}
