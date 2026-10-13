using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace JewelryMiniFactory.Economy.Tests
{
    public sealed class BoostServiceTests
    {
        private static readonly DateTimeOffset StartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private sealed class TestConfigSource : IFactoryConfigSource
        {
            public double InitialBalance => 0;
            public double UpgradeCostMultiplier => 1.5;
            public bool BoostEnabled { get; set; } = true;
            public double BoostDuration { get; set; } = 30;
            public double BoostMultiplier { get; set; } = 2;

            public IReadOnlyList<MachineConfig> GetMachines() => new[]
            {
                new MachineConfig("GeodeMine", true, 1, 1, 0, 10),
                new MachineConfig("GeodeCrusher", true, 2, 5, 50, 40),
                new MachineConfig("JewelryWorkshop", false, 1, 20, 250, 120)
            };
        }

        [Test]
        public void Activate_EnabledBoost_StartsConfiguredUtcWindow()
        {
            var boost = new BoostService(new TestConfigSource());
            Assert.That(boost.IsActive(StartUtc), Is.False);
            Assert.That(boost.TryActivate(StartUtc), Is.True);
            Assert.That(boost.StartedAtUtc, Is.EqualTo(StartUtc));
            Assert.That(boost.EndsAtUtc, Is.EqualTo(StartUtc.AddSeconds(30)));
            Assert.That(boost.IsActive(StartUtc), Is.True);
            Assert.That(boost.GetCurrentMultiplier(StartUtc), Is.EqualTo(2));
            Assert.That(boost.GetRemainingSeconds(StartUtc.AddSeconds(1)), Is.EqualTo(29));
        }

        [Test]
        public void Activate_AlreadyActive_IsRejectedWithoutExtendingWindow()
        {
            var boost = new BoostService(new TestConfigSource());
            Assert.That(boost.TryActivate(StartUtc), Is.True);
            Assert.That(boost.TryActivate(StartUtc.AddSeconds(15)), Is.False);
            Assert.That(boost.StartedAtUtc, Is.EqualTo(StartUtc));
            Assert.That(boost.EndsAtUtc, Is.EqualTo(StartUtc.AddSeconds(30)));
            Assert.That(boost.GetRemainingSeconds(StartUtc.AddSeconds(15)), Is.EqualTo(15));
        }

        [Test]
        public void Expiry_UsesUtcBoundaryAndAllowsImmediateReactivation()
        {
            var boost = new BoostService(new TestConfigSource());
            boost.TryActivate(StartUtc);
            Assert.That(boost.IsActive(StartUtc.AddSeconds(29.5)), Is.True);
            Assert.That(boost.IsActive(StartUtc.AddSeconds(30)), Is.False);
            Assert.That(boost.GetCurrentMultiplier(StartUtc.AddSeconds(30)), Is.EqualTo(1));
            Assert.That(boost.GetRemainingSeconds(StartUtc.AddSeconds(31)), Is.Zero);
            Assert.That(boost.TryActivate(StartUtc.AddSeconds(30)), Is.True);
            Assert.That(boost.EndsAtUtc, Is.EqualTo(StartUtc.AddSeconds(60)));
        }

        [Test]
        public void Income_ActiveBoost_MultipliesProductionOfAllOpenMachines()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            boost.TryActivate(StartUtc);
            var income = factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(StartUtc, StartUtc.AddSeconds(5)));
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(11));
            Assert.That(factory.GetIncomePerSecond("GeodeMine"), Is.EqualTo(1));
            Assert.That(factory.GetIncomePerSecond("GeodeCrusher"), Is.EqualTo(10));
            Assert.That(factory.GetIncomePerSecond("JewelryWorkshop"), Is.Zero);
            Assert.That(income, Is.EqualTo(110));
            Assert.That(factory.State.Balance, Is.EqualTo(110));
        }

        [Test]
        public void Income_ExpiryInsideInterval_SplitsBoostedAndNormalSeconds()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            boost.TryActivate(StartUtc);
            // 5 boosted seconds + 5 normal seconds at base production 11/sec.
            var effective = boost.GetEffectiveElapsedSeconds(StartUtc.AddSeconds(25), StartUtc.AddSeconds(35));
            Assert.That(effective, Is.EqualTo(15));
            Assert.That(factory.AccrueIncome(effective), Is.EqualTo(165));
            Assert.That(factory.State.Balance, Is.EqualTo(165));
            Assert.That(boost.IsActive(StartUtc.AddSeconds(35)), Is.False);
        }

        [TestCase(-10, -5, 5)]
        [TestCase(-5, 5, 15)]
        [TestCase(-5, 35, 70)]
        [TestCase(0, 30, 60)]
        [TestCase(30, 35, 5)]
        [TestCase(15, 15, 0)]
        public void EffectiveSeconds_HandlesIntervalsAroundBothWindowBoundaries(double from, double to, double expected)
        {
            var boost = new BoostService(new TestConfigSource());
            boost.TryActivate(StartUtc);
            Assert.That(boost.GetEffectiveElapsedSeconds(StartUtc.AddSeconds(from), StartUtc.AddSeconds(to)),
                Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void Income_QueryingExpiredState_DoesNotLoseBoostedPartOfPastInterval()
        {
            var boost = new BoostService(new TestConfigSource());
            boost.TryActivate(StartUtc);
            Assert.That(boost.IsActive(StartUtc.AddSeconds(35)), Is.False);
            Assert.That(boost.GetEffectiveElapsedSeconds(StartUtc.AddSeconds(25), StartUtc.AddSeconds(35)), Is.EqualTo(15));
        }

        [Test]
        public void DisabledBoost_CannotActivateAndIncomeStaysNormal()
        {
            var source = new TestConfigSource { BoostEnabled = false };
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            Assert.That(boost.Enabled, Is.False);
            Assert.That(boost.TryActivate(StartUtc), Is.False);
            Assert.That(boost.StartedAtUtc, Is.Null);
            Assert.That(boost.EndsAtUtc, Is.Null);
            Assert.That(boost.IsActive(StartUtc), Is.False);
            Assert.That(boost.GetCurrentMultiplier(StartUtc), Is.EqualTo(1));
            Assert.That(boost.GetRemainingSeconds(StartUtc), Is.Zero);
            Assert.That(factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(StartUtc, StartUtc.AddSeconds(5))), Is.EqualTo(55));
        }

        [Test]
        public void Income_BeforeFirstActivation_RemainsNormal()
        {
            var boost = new BoostService(new TestConfigSource());
            Assert.That(boost.GetEffectiveElapsedSeconds(StartUtc, StartUtc.AddSeconds(5)), Is.EqualTo(5));
        }

        [Test]
        public void Configuration_CustomDurationAndMultiplierAreSnapshotted()
        {
            var source = new TestConfigSource { BoostDuration = 10, BoostMultiplier = 3 };
            var boost = new BoostService(source);
            source.BoostEnabled = false;
            source.BoostDuration = 99;
            source.BoostMultiplier = 99;
            Assert.That(boost.TryActivate(StartUtc), Is.True);
            Assert.That(boost.EndsAtUtc, Is.EqualTo(StartUtc.AddSeconds(10)));
            Assert.That(boost.GetCurrentMultiplier(StartUtc), Is.EqualTo(3));
            Assert.That(boost.GetEffectiveElapsedSeconds(StartUtc, StartUtc.AddSeconds(15)), Is.EqualTo(35));
        }

        [Test]
        public void Income_SmallFrameIntervals_PreserveFractionalCurrency()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            boost.TryActivate(StartUtc);
            var from = StartUtc;
            for (var frame = 0; frame < 10; frame++)
            {
                var to = from.AddMilliseconds(10);
                Assert.That(factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(from, to)), Is.EqualTo(0.22).Within(1e-12));
                from = to;
            }
            Assert.That(factory.State.Balance, Is.EqualTo(2.2).Within(1e-12));
        }

        [Test]
        public void Income_ConsecutiveBoosts_SettlePreviousWindowBeforeReactivation()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            boost.TryActivate(StartUtc);
            factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(StartUtc, StartUtc.AddSeconds(35)));
            Assert.That(boost.TryActivate(StartUtc.AddSeconds(35)), Is.True);
            factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(StartUtc.AddSeconds(35), StartUtc.AddSeconds(40)));
            Assert.That(factory.State.Balance, Is.EqualTo(825));
        }

        [Test]
        public void EffectiveSeconds_ReversedInterval_IsRejected()
        {
            var boost = new BoostService(new TestConfigSource());
            Assert.Throws<ArgumentOutOfRangeException>(() => boost.GetEffectiveElapsedSeconds(StartUtc.AddSeconds(1), StartUtc));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Configuration_InvalidDuration_IsRejected(double duration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoostService(new TestConfigSource { BoostDuration = duration }));
        }

        [TestCase(0.5)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Configuration_InvalidMultiplier_IsRejected(double multiplier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoostService(new TestConfigSource { BoostMultiplier = multiplier }));
        }

        [Test]
        public void Activate_OutOfRangeEnd_DoesNotPartiallyChangeState()
        {
            var boost = new BoostService(new TestConfigSource());
            Assert.Throws<ArgumentOutOfRangeException>(() => boost.TryActivate(DateTimeOffset.MaxValue));
            Assert.That(boost.StartedAtUtc, Is.Null);
            Assert.That(boost.EndsAtUtc, Is.Null);
        }
    }
}
