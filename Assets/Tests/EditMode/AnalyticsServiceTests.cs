using System;
using System.Collections.Generic;
using System.Reflection;
using JewelryMiniFactory.Analytics;
using NUnit.Framework;
using UnityEngine;

namespace JewelryMiniFactory.Economy.Tests
{
    public sealed class AnalyticsServiceTests
    {
        private sealed class RecordingProvider : IAnalyticsProvider
        {
            public readonly List<string> Events = new List<string>();
            public IReadOnlyDictionary<string, object> Parameters;
            public void SendEvent(string eventName, IReadOnlyDictionary<string, object> parameters)
            {
                Events.Add(eventName);
                Parameters = parameters;
            }
        }

        private sealed class ThrowingProvider : IAnalyticsProvider
        {
            public void SendEvent(string eventName, IReadOnlyDictionary<string, object> parameters)
                => throw new InvalidOperationException("provider unavailable");
        }

        private sealed class Source : IFactoryConfigSource
        {
            public double InitialBalance => 0;
            public double UpgradeCostMultiplier => 1.5;
            public bool BoostEnabled => true;
            public double BoostDuration => 30;
            public double BoostMultiplier => 2;
            public double MaxOfflineDuration => 10800;
            public IReadOnlyList<MachineConfig> GetMachines() => new[] { new MachineConfig("Mine", true, 1, 1, 0, 10) };
        }

        [Test]
        public void Track_DeliversEventAndParametersToEveryProvider()
        {
            var first = new RecordingProvider();
            var second = new RecordingProvider();
            var analytics = new AnalyticsService(first, second);
            analytics.Track("machine_upgraded", new Dictionary<string, object> { ["machine_id"] = "GeodeMine", ["level"] = 2, ["cost"] = 10.0 });
            Assert.That(first.Events, Is.EqualTo(new[] { "machine_upgraded" }));
            Assert.That(second.Events, Is.EqualTo(first.Events));
            Assert.That(second.Parameters["machine_id"], Is.EqualTo("GeodeMine"));
            Assert.That(second.Parameters["level"], Is.EqualTo(2));
            Assert.That(second.Parameters["cost"], Is.EqualTo(10));
        }

        [Test]
        public void Track_FailingProviderDoesNotStopOtherProvidersOrThrow()
        {
            var first = new RecordingProvider();
            var last = new RecordingProvider();
            var analytics = new AnalyticsService(new ThrowingProvider(), first, new ThrowingProvider(), last);
            Assert.DoesNotThrow(() => analytics.Track("game_started"));
            Assert.That(first.Events, Is.EqualTo(new[] { "game_started" }));
            Assert.That(last.Events, Is.EqualTo(first.Events));
        }

        [Test]
        public void Track_ParametersAreReadOnlyAndDetachedFromCallerDictionary()
        {
            var provider = new RecordingProvider();
            var analytics = new AnalyticsService(provider);
            var data = new Dictionary<string, object> { ["amount"] = 100 };
            analytics.Track("purchase_succeeded", data);
            data["amount"] = 999;
            Assert.That(provider.Parameters["amount"], Is.EqualTo(100));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object>)provider.Parameters)["amount"] = 1);
        }

        [Test]
        public void Track_NoProvidersOrNullProvider_IsSafe()
        {
            Assert.DoesNotThrow(() => new AnalyticsService().Track("game_started"));
            var provider = new RecordingProvider();
            new AnalyticsService(null, provider).Track("game_started");
            Assert.That(provider.Events.Count, Is.EqualTo(1));
        }

        [Test, Category("UnityRuntime")]
        public void RestoredBoost_ReportsFinishOnceAndExpiredCheckpointDoesNotRepeatIt()
        {
            var source = new Source();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            boost.TryActivate(start);
            var saved = SaveService.Capture(factory, boost, start.AddSeconds(5));
            Assert.That(SaveService.TryRestore(saved, source, out factory, out boost, out _), Is.True);
            var provider = new RecordingProvider();
            var type = Type.GetType("JewelryMiniFactory.Gameplay.GameController, Assembly-CSharp", true);
            var root = new GameObject("Analytics Boost finish regression");
            try
            {
                var controller = root.AddComponent(type);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField("analytics", flags).SetValue(controller, new AnalyticsService(provider));
                type.GetField("boost", flags).SetValue(controller, boost);
                var check = type.GetMethod("TrackBoostFinished", flags);
                check.Invoke(controller, new object[] { start.AddSeconds(29) });
                Assert.That(provider.Events, Is.Empty);
                check.Invoke(controller, new object[] { start.AddSeconds(30) });
                check.Invoke(controller, new object[] { start.AddSeconds(31) });
                Assert.That(provider.Events, Is.EqualTo(new[] { "boost_finished" }));
                Assert.That(provider.Parameters["duration"], Is.EqualTo(30));
                Assert.That(provider.Parameters["multiplier"], Is.EqualTo(2));
                var expiredSave = SaveService.Capture(factory, boost, start.AddSeconds(31));
                Assert.That(SaveService.TryRestore(expiredSave, source, out _, out var nextLaunchBoost, out _), Is.True);
                type.GetField("boost", flags).SetValue(controller, nextLaunchBoost);
                type.GetField("reportedBoostEndUtc", flags).SetValue(controller, null);
                check.Invoke(controller, new object[] { start.AddSeconds(40) });
                Assert.That(provider.Events.Count, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test, Category("UnityRuntime")]
        public void RejectedGameplayInputBeforeInitialization_DoesNotEmitEvents()
        {
            var type = Type.GetType("JewelryMiniFactory.Gameplay.GameController, Assembly-CSharp", true);
            var root = new GameObject("Analytics rejected input regression");
            var events = new List<string>();
            Application.LogCallback record = (message, stack, logType) =>
            {
                if (message.StartsWith("[Analytics]", StringComparison.Ordinal)) events.Add(message);
            };
            Application.logMessageReceived += record;
            try
            {
                var controller = root.AddComponent(type);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("OnUnlockRequested", flags).Invoke(controller, new object[] { null });
                type.GetMethod("OnOpenRequested", flags).Invoke(controller, new object[] { null });
                type.GetMethod("OnUpgradeRequested", flags).Invoke(controller, null);
                type.GetMethod("OnBoostRequested", flags).Invoke(controller, null);
                Assert.That(events, Is.Empty);
            }
            finally
            {
                Application.logMessageReceived -= record;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
