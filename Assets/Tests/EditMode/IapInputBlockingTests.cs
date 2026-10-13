using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace JewelryMiniFactory.Economy.Tests
{
    [Category("UnityRuntime")]
    public sealed class IapInputBlockingTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void UnavailableStore_ReleasesGameplayEvenIfPurchaseRemainsBusy()
        {
            var type = Type.GetType("JewelryMiniFactory.Purchasing.IapPurchaseService, Assembly-CSharp", true);
            var service = Activator.CreateInstance(type, new object[] { new Func<string, bool>(_ => false) });
            try
            {
                type.GetProperty("BlocksGameplayInput").SetValue(service, true);
                type.GetField("purchaseRequested", PrivateInstance).SetValue(service, true);
                type.GetMethod("Unavailable", PrivateInstance).Invoke(service, new object[] { "test failure" });
                Assert.That(type.GetProperty("BlocksGameplayInput").GetValue(service), Is.False);
                Assert.That(type.GetProperty("IsBusy").GetValue(service), Is.True);
                Assert.That(type.GetProperty("CanPurchase").GetValue(service), Is.False);
            }
            finally { ((IDisposable)service).Dispose(); }
        }

        [Test]
        public void DeferredPurchase_DoesNotKeepGameplayLocked()
        {
            var type = Type.GetType("JewelryMiniFactory.Purchasing.IapPurchaseService, Assembly-CSharp", true);
            var service = Activator.CreateInstance(type, new object[] { new Func<string, bool>(_ => false) });
            try
            {
                type.GetProperty("BlocksGameplayInput").SetValue(service, true);
                type.GetMethod("OnDeferred", PrivateInstance).Invoke(service, new object[] { null });
                Assert.That(type.GetProperty("BlocksGameplayInput").GetValue(service), Is.False);
                Assert.That(type.GetProperty("IsBusy").GetValue(service), Is.True);
            }
            finally { ((IDisposable)service).Dispose(); }
        }

        [Test]
        public void IapCompletion_BlocksRemainderOfClickFrameButNotSubsequentFrames()
        {
            var type = Type.GetType("JewelryMiniFactory.Gameplay.GameController, Assembly-CSharp", true);
            var root = new GameObject("IAP input frame regression");
            try
            {
                var controller = root.AddComponent(type);
                type.GetMethod("OnIapStateChanged", PrivateInstance).Invoke(controller, null);
                var blocked = type.GetProperty("IsGameInputBlocked", PrivateInstance);
                Assert.That(blocked.GetValue(controller), Is.True);
                type.GetField("inputBlockedThroughFrame", PrivateInstance).SetValue(controller, Time.frameCount - 1);
                Assert.That(blocked.GetValue(controller), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
