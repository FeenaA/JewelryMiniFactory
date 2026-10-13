using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace JewelryMiniFactory.Economy.Tests
{
    public sealed class PurchaseRewardTests
    {
        private sealed class Source : IFactoryConfigSource
        {
            public double InitialBalance => 0.25;
            public double UpgradeCostMultiplier => 1.5;
            public bool BoostEnabled => true;
            public double BoostDuration => 30;
            public double BoostMultiplier => 2;
            public double MaxOfflineDuration => 10800;
            public IReadOnlyList<MachineConfig> GetMachines() => new[] { new MachineConfig("Mine", true, 1, 1, 0, 10) };
        }

        [Test]
        public void PaidTransaction_PersistsRewardAndIdBeforeChangingLiveBalance()
        {
            var factory = new FactoryService(new Source());
            var persisted = false;
            Assert.That(factory.TryCreditPurchase("tx1", 100, (balance, ids) =>
            {
                Assert.That(factory.State.Balance, Is.EqualTo(0.25));
                Assert.That(factory.State.ProcessedTransactionIds, Is.Empty);
                Assert.That(balance, Is.EqualTo(100.25));
                Assert.That(ids, Is.EquivalentTo(new[] { "tx1" }));
                persisted = true;
                return true;
            }), Is.True);
            Assert.That(persisted, Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(100.25));
            Assert.That(factory.State.ProcessedTransactionIds, Is.EquivalentTo(new[] { "tx1" }));
        }

        [Test]
        public void DuplicatePaidTransaction_DoesNotGrantAgain()
        {
            var factory = new FactoryService(new Source());
            var commits = 0;
            Func<double, IReadOnlyList<string>, bool> save = (balance, ids) => { commits++; return true; };
            Assert.That(factory.TryCreditPurchase("tx1", 100, save), Is.True);
            Assert.That(factory.TryCreditPurchase("tx1", 100, save), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(100.25));
            Assert.That(commits, Is.EqualTo(1));
        }

        [Test]
        public void FailedCommit_DoesNotGrantAndCanBeRetried()
        {
            var factory = new FactoryService(new Source());
            Assert.That(factory.TryCreditPurchase("tx1", 100, (balance, ids) => false), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(0.25));
            Assert.That(factory.State.ProcessedTransactionIds, Is.Empty);
            Assert.That(factory.TryCreditPurchase("tx1", 100, (balance, ids) => true), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(100.25));
        }

        [Test]
        public void ThrowingCommit_DoesNotChangeRewardState()
        {
            var factory = new FactoryService(new Source());
            Assert.Throws<InvalidOperationException>(() => factory.TryCreditPurchase("tx1", 100,
                (balance, ids) => throw new InvalidOperationException("disk failure")));
            Assert.That(factory.State.Balance, Is.EqualTo(0.25));
            Assert.That(factory.State.ProcessedTransactionIds, Is.Empty);
        }

        [Test]
        public void Consumable_NewTransaction_GrantsAnotherPack()
        {
            var factory = new FactoryService(new Source());
            factory.TryCreditPurchase("tx1", 100, (balance, ids) => true);
            factory.TryCreditPurchase("tx2", 100, (balance, ids) => true);
            Assert.That(factory.State.Balance, Is.EqualTo(200.25));
            Assert.That(factory.State.ProcessedTransactionIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void SaveSnapshot_RestoresTransactionHistoryAndPreventsDuplicateGrant()
        {
            var source = new Source();
            var factory = new FactoryService(source);
            factory.TryCreditPurchase("tx1", 100, (balance, ids) => true);
            var utc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var data = SaveService.Capture(factory, new BoostService(source), utc);
            Assert.That(data.ProcessedTransactionIds, Is.EquivalentTo(new[] { "tx1" }));
            Assert.That(SaveService.TryRestore(data, source, out var restored, out _, out _), Is.True);
            Assert.That(restored.TryCreditPurchase("tx1", 100, (balance, ids) => throw new Exception("duplicate must not persist again")), Is.True);
            Assert.That(restored.State.Balance, Is.EqualTo(100.25));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void AddCurrency_InvalidAmount_IsRejected(double amount)
        {
            var factory = new FactoryService(new Source());
            Assert.Throws<ArgumentOutOfRangeException>(() => factory.AddCurrency(amount));
            Assert.That(factory.State.Balance, Is.EqualTo(0.25));
        }

        [Test]
        public void AddCurrency_ValidReward_PreservesFractionalBalance()
        {
            var factory = new FactoryService(new Source());
            factory.AddCurrency(100);
            Assert.That(factory.State.Balance, Is.EqualTo(100.25));
        }

        [TestCase(null)]
        [TestCase("")]
        public void PaidTransaction_InvalidId_IsRejected(string id)
        {
            var factory = new FactoryService(new Source());
            Assert.Throws<ArgumentException>(() => factory.TryCreditPurchase(id, 100, (balance, ids) => true));
            Assert.That(factory.State.Balance, Is.EqualTo(0.25));
        }
    }
}
