using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace JewelryMiniFactory.Economy.Tests
{
    public sealed class SaveServiceTests
    {
        private static readonly DateTimeOffset StartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private sealed class TestConfigSource : IFactoryConfigSource
        {
            public double InitialBalance => 0;
            public double UpgradeCostMultiplier => 1.5;
            public bool BoostEnabled => true;
            public double BoostDuration => 30;
            public double BoostMultiplier => 2;
            public double MaxOfflineDuration => 10800;
            public double MineIncome { get; set; } = 1;
            public IReadOnlyList<MachineConfig> GetMachines() => new[]
            {
                new MachineConfig("GeodeMine", true, 1, MineIncome, 0, 10),
                new MachineConfig("GeodeCrusher", false, 1, 5, 50, 40),
                new MachineConfig("JewelryWorkshop", false, 1, 20, 250, 120)
            };
        }

        private static GameSaveData NewSave()
        {
            var source = new TestConfigSource();
            return SaveService.Capture(new FactoryService(source), new BoostService(source), StartUtc);
        }

        [Test]
        public void Snapshot_RestoresFractionalBalanceLevelsStatusesAndActiveBoost()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            factory.AccrueIncome(100);
            factory.TryUnlock("GeodeCrusher");
            factory.TryUpgrade("GeodeMine");
            factory.AccrueIncome(0.25);
            boost.TryActivate(StartUtc);
            var data = SaveService.Capture(factory, boost, StartUtc);
            Assert.That(SaveService.TryRestore(data, source, out var restored, out var restoredBoost, out var lastUtc), Is.True);
            Assert.That(restored.State.Balance, Is.EqualTo(41.75));
            Assert.That(restored.State.Machines["GeodeMine"].Level, Is.EqualTo(2));
            Assert.That(restored.State.Machines["GeodeCrusher"].Status, Is.EqualTo(MachineStatus.Unlocked));
            Assert.That(restored.State.Machines["GeodeCrusher"].Level, Is.EqualTo(1));
            Assert.That(restored.State.Machines["JewelryWorkshop"].Status, Is.EqualTo(MachineStatus.Locked));
            Assert.That(restored.State.Machines["JewelryWorkshop"].Level, Is.Zero);
            Assert.That(restored.GetTotalIncomePerSecond(), Is.EqualTo(7));
            Assert.That(lastUtc, Is.EqualTo(StartUtc));
            Assert.That(restoredBoost.EndsAtUtc, Is.EqualTo(StartUtc.AddSeconds(30)));
            Assert.That(restoredBoost.IsActive(StartUtc.AddSeconds(5)), Is.True);
            data.Balance = 999;
            data.Machines[0].Level = 999;
            Assert.That(restored.State.Balance, Is.EqualTo(41.75));
            Assert.That(restored.State.Machines["GeodeMine"].Level, Is.EqualTo(2));
        }

        [Test]
        public void OfflineIncome_AccruesAllOpenMachinesAfterRestoration()
        {
            var source = new TestConfigSource();
            var data = NewSave();
            data.Balance = 0.125;
            data.Machines[0].Level = 2;
            data.Machines[1].Status = MachineStatus.Unlocked;
            data.Machines[1].Level = 1;
            Assert.That(SaveService.TryRestore(data, source, out var factory, out var boost, out var last), Is.True);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, StartUtc.AddSeconds(10), source.MaxOfflineDuration), Is.EqualTo(70));
            Assert.That(factory.State.Balance, Is.EqualTo(70.125));
            Assert.That(last, Is.EqualTo(StartUtc.AddSeconds(10)));
        }

        [TestCase(10800, 10800)]
        [TestCase(0, 0)]
        public void OfflineIncome_CapsDurationAndDiscardsExcess(double cap, double expected)
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            var last = StartUtc;
            var now = StartUtc.AddHours(5);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, now, cap), Is.EqualTo(expected));
            Assert.That(last, Is.EqualTo(now));
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, now, cap), Is.Zero);
            Assert.That(factory.State.Balance, Is.EqualTo(expected));
        }

        [Test]
        public void OfflineIncome_ExpiredBoost_SplitsAcceleratedAndNormalParts()
        {
            var source = new TestConfigSource();
            var boost = new BoostService(source);
            boost.TryActivate(StartUtc);
            var data = SaveService.Capture(new FactoryService(source), boost, StartUtc.AddSeconds(10));
            Assert.That(SaveService.TryRestore(data, source, out var factory, out var restoredBoost, out var last), Is.True);
            var now = StartUtc.AddSeconds(40);
            Assert.That(restoredBoost.IsActive(now), Is.False);
            // 20 seconds with x2 + 10 seconds with x1.
            Assert.That(SaveService.ApplyOfflineIncome(factory, restoredBoost, ref last, now, 10800), Is.EqualTo(50));
            Assert.That(factory.State.Balance, Is.EqualTo(50));
            Assert.That(restoredBoost.GetCurrentMultiplier(now), Is.EqualTo(1));
            var savedAgain = SaveService.Capture(factory, restoredBoost, last);
            Assert.That(savedAgain.BoostEndsUtcTicks, Is.Zero);
        }

        [Test]
        public void OfflineIncome_CapUsesFirstPartOfAbsenceWithBoostOverlap()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            boost.TryActivate(StartUtc);
            var last = StartUtc.AddSeconds(25);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, StartUtc.AddSeconds(40), 10), Is.EqualTo(15));
            Assert.That(last, Is.EqualTo(StartUtc.AddSeconds(40)));
        }

        [Test]
        public void OfflineIncome_RepeatedResumeAndRestart_DoNotRecreditInterval()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            factory.AccrueIncome(5);
            var last = StartUtc.AddSeconds(5);
            var resume = StartUtc.AddSeconds(15);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, resume, 10800), Is.EqualTo(10));
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, resume, 10800), Is.Zero);
            var data = SaveService.Capture(factory, boost, last);
            Assert.That(SaveService.TryRestore(data, source, out factory, out boost, out last), Is.True);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, resume, 10800), Is.Zero);
            Assert.That(factory.State.Balance, Is.EqualTo(15));
            factory.AccrueIncome(5);
            last = StartUtc.AddSeconds(20);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, StartUtc.AddSeconds(25), 10800), Is.EqualTo(5));
            Assert.That(factory.State.Balance, Is.EqualTo(25));
        }

        [Test]
        public void OfflineIncome_BackwardsClock_RetainsCheckpointUntilClockCatchesUp()
        {
            var source = new TestConfigSource();
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            var last = StartUtc.AddSeconds(10);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, StartUtc.AddSeconds(5), 10800), Is.Zero);
            Assert.That(last, Is.EqualTo(StartUtc.AddSeconds(10)));
            var data = SaveService.Capture(factory, boost, last);
            Assert.That(SaveService.TryRestore(data, source, out factory, out boost, out last), Is.True);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, StartUtc.AddSeconds(10), 10800), Is.Zero);
            Assert.That(SaveService.ApplyOfflineIncome(factory, boost, ref last, StartUtc.AddSeconds(11), 10800), Is.EqualTo(1));
        }

        [TestCase("version")]
        [TestCase("negative_balance")]
        [TestCase("nan_balance")]
        [TestCase("infinite_balance")]
        [TestCase("missing_machines")]
        [TestCase("duplicate_id")]
        [TestCase("unknown_id")]
        [TestCase("invalid_status")]
        [TestCase("invalid_level")]
        [TestCase("locked_level")]
        [TestCase("locked_initial_machine")]
        [TestCase("missing_time")]
        [TestCase("invalid_time")]
        [TestCase("boost_end_without_start")]
        [TestCase("boost_reversed")]
        [TestCase("boost_future_start")]
        public void Restore_InvalidData_IsRejectedWithoutPartialServices(string corruption)
        {
            var data = NewSave();
            switch (corruption)
            {
                case "version": data.Version = 999; break;
                case "negative_balance": data.Balance = -1; break;
                case "nan_balance": data.Balance = double.NaN; break;
                case "infinite_balance": data.Balance = double.PositiveInfinity; break;
                case "missing_machines": data.Machines = null; break;
                case "duplicate_id": data.Machines[1].Id = data.Machines[0].Id; break;
                case "unknown_id": data.Machines[1].Id = "Unknown"; break;
                case "invalid_status": data.Machines[1].Status = (MachineStatus)99; break;
                case "invalid_level": data.Machines[0].Level = 0; break;
                case "locked_level": data.Machines[1].Level = 1; break;
                case "locked_initial_machine": data.Machines[0].Status = MachineStatus.Locked; data.Machines[0].Level = 0; break;
                case "missing_time": data.LastIncomeUtcTicks = 0; break;
                case "invalid_time": data.LastIncomeUtcTicks = long.MaxValue; break;
                case "boost_end_without_start": data.BoostEndsUtcTicks = StartUtc.AddSeconds(30).Ticks; break;
                case "boost_reversed": data.BoostStartedUtcTicks = StartUtc.Ticks; data.BoostEndsUtcTicks = StartUtc.AddSeconds(-1).Ticks; break;
                case "boost_future_start": data.BoostStartedUtcTicks = StartUtc.AddSeconds(1).Ticks; data.BoostEndsUtcTicks = StartUtc.AddSeconds(30).Ticks; break;
            }
            Assert.That(SaveService.TryRestore(data, new TestConfigSource(), out var factory, out var boost, out var last), Is.False);
            Assert.That(factory, Is.Null);
            Assert.That(boost, Is.Null);
            Assert.That(last, Is.EqualTo(default(DateTimeOffset)));
        }

        [Test]
        public void Restore_MissingSave_IsRejectedAndNewSessionKeepsDefaults()
        {
            var source = new TestConfigSource();
            Assert.That(SaveService.TryRestore(null, source, out _, out _, out _), Is.False);
            var factory = new FactoryService(source);
            Assert.That(factory.State.Balance, Is.Zero);
            Assert.That(factory.State.Machines["GeodeMine"].Level, Is.EqualTo(1));
        }

        [Test]
        public void OfflineIncome_Overflow_DoesNotChangeBalanceOrCheckpoint()
        {
            var source = new TestConfigSource { MineIncome = double.MaxValue / 4 };
            var factory = new FactoryService(source);
            var boost = new BoostService(source);
            factory.AccrueIncome(3);
            var before = factory.State.Balance;
            var last = StartUtc;
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveService.ApplyOfflineIncome(factory, boost, ref last,
                StartUtc.AddSeconds(2), 10800));
            Assert.That(factory.State.Balance, Is.EqualTo(before));
            Assert.That(last, Is.EqualTo(StartUtc));
        }

        [Test, Category("UnityRuntime")]
        public void Clear_RemovesOnlyGameSaveAndIsSafeToRepeat()
        {
            var hadSave = PlayerPrefs.HasKey(SaveService.SaveKey);
            var previousSave = PlayerPrefs.GetString(SaveService.SaveKey);
            var unrelatedKey = "JewelryMiniFactory.Test.Unrelated." + Guid.NewGuid().ToString("N");
            try
            {
                PlayerPrefs.SetString(SaveService.SaveKey, "old progress");
                PlayerPrefs.SetString(unrelatedKey, "keep");
                var service = new SaveService();
                Assert.That(service.TryClear(), Is.True);
                Assert.That(PlayerPrefs.HasKey(SaveService.SaveKey), Is.False);
                Assert.That(PlayerPrefs.GetString(unrelatedKey), Is.EqualTo("keep"));
                Assert.That(service.TryClear(), Is.True);
                Assert.That(PlayerPrefs.HasKey(SaveService.SaveKey), Is.False);
                Assert.That(service.TryLoad(new TestConfigSource(), out _, out _, out _), Is.False);
            }
            finally
            {
                if (hadSave) PlayerPrefs.SetString(SaveService.SaveKey, previousSave);
                else PlayerPrefs.DeleteKey(SaveService.SaveKey);
                PlayerPrefs.DeleteKey(unrelatedKey);
                PlayerPrefs.Save();
            }
        }

        [Test, Category("UnityRuntime")]
        public void Json_RoundTrip_PreservesFractionalBalanceAndUtcTicks()
        {
            var data = NewSave();
            data.Balance = 42.125;
            data.LastIncomeUtcTicks += 12345;
            var json = JsonUtility.ToJson(data);
            Assert.That(SaveService.TryDeserialize(json, out var parsed), Is.True);
            Assert.That(SaveService.TryRestore(parsed, new TestConfigSource(), out var factory, out _, out var last), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(42.125));
            Assert.That(last.UtcDateTime.Ticks, Is.EqualTo(data.LastIncomeUtcTicks));
        }

        [TestCase("not json"), Category("UnityRuntime")]
        [TestCase("{}")]
        [TestCase("{\"Version\":1}")]
        public void Json_CorruptOrIncompleteInput_CannotRestore(string json)
        {
            var deserialized = SaveService.TryDeserialize(json, out var data);
            Assert.That(deserialized && SaveService.TryRestore(data, new TestConfigSource(), out _, out _, out _), Is.False);
        }
    }
}
