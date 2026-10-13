using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace JewelryMiniFactory.Economy.Tests
{
    public sealed class FactoryServiceTests
    {
        private sealed class TestConfigSource : IFactoryConfigSource
        {
            public double InitialBalance { get; set; }
            public double UpgradeCostMultiplier { get; set; } = 1.5;
            public IReadOnlyList<MachineConfig> Machines { get; set; }
            public IReadOnlyList<MachineConfig> GetMachines() => Machines;
        }

        private static FactoryService CreateFactory(double balance = 0)
        {
            return new FactoryService(new TestConfigSource
            {
                InitialBalance = balance,
                Machines = new[]
                {
                    new MachineConfig("GeodeMine", true, 1, 1, 0, 10),
                    new MachineConfig("GeodeCrusher", false, 1, 5, 50, 40),
                    new MachineConfig("JewelryWorkshop", false, 1, 20, 250, 120)
                }
            });
        }

        [Test]
        public void NewGame_HasConfiguredBalanceAndMachineStates()
        {
            var factory = CreateFactory();
            Assert.That(factory.State.Balance, Is.Zero);
            Assert.That(factory.State.Machines.Count, Is.EqualTo(3));
            Assert.That(factory.State.Machines["GeodeMine"].Status, Is.EqualTo(MachineStatus.Unlocked));
            Assert.That(factory.State.Machines["GeodeMine"].Level, Is.EqualTo(1));
            Assert.That(factory.State.Machines["GeodeCrusher"].Status, Is.EqualTo(MachineStatus.Locked));
            Assert.That(factory.State.Machines["JewelryWorkshop"].Level, Is.Zero);
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(1));
        }

        [TestCase("GeodeCrusher", 50, 0, 6)]
        [TestCase("GeodeCrusher", 75, 25, 6)]
        [TestCase("JewelryWorkshop", 250, 0, 21)]
        public void Unlock_WithEnoughBalance_SpendsPriceAndStartsProduction(
            string id, double balance, double remaining, double totalIncome)
        {
            var factory = CreateFactory(balance);
            Assert.That(factory.TryUnlock(id), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(remaining));
            Assert.That(factory.State.Machines[id].Status, Is.EqualTo(MachineStatus.Unlocked));
            Assert.That(factory.State.Machines[id].Level, Is.EqualTo(1));
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(totalIncome));
        }

        [TestCase(0)]
        [TestCase(49.75)]
        public void Unlock_WithInsufficientBalance_DoesNotChangeState(double balance)
        {
            var factory = CreateFactory(balance);
            var machine = factory.State.Machines["GeodeCrusher"];
            Assert.That(factory.TryUnlock(machine.Id), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(balance));
            Assert.That(machine.Status, Is.EqualTo(MachineStatus.Locked));
            Assert.That(machine.Level, Is.Zero);
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(1));
        }

        [TestCase(10, 0)]
        [TestCase(30, 20)]
        public void Upgrade_WithEnoughBalance_SpendsPriceAndIncreasesLevelAndIncome(double balance, double remaining)
        {
            var factory = CreateFactory(balance);
            Assert.That(factory.GetNextUpgradeCost("GeodeMine"), Is.EqualTo(10));
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(remaining));
            Assert.That(factory.State.Machines["GeodeMine"].Level, Is.EqualTo(2));
            Assert.That(factory.GetIncomePerSecond("GeodeMine"), Is.EqualTo(2));
            Assert.That(factory.GetNextUpgradeCost("GeodeMine"), Is.EqualTo(15));
        }

        [Test]
        public void Upgrade_WithFractionalPriceAndExactBalance_SpendsEntireBalance()
        {
            var factory = CreateFactory(47.5);
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.True);
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(22.5));
            Assert.That(factory.GetNextUpgradeCost("GeodeMine"), Is.EqualTo(22.5));
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.True);
            Assert.That(factory.State.Balance, Is.Zero);
            Assert.That(factory.State.Machines["GeodeMine"].Level, Is.EqualTo(4));
            Assert.That(factory.GetIncomePerSecond("GeodeMine"), Is.EqualTo(4));
            Assert.That(factory.GetNextUpgradeCost("GeodeMine"), Is.EqualTo(33.75));
        }

        [Test]
        public void Upgrade_WithInsufficientBalance_DoesNotChangeState()
        {
            var factory = CreateFactory(9.75);
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(9.75));
            Assert.That(factory.State.Machines["GeodeMine"].Level, Is.EqualTo(1));
            Assert.That(factory.GetIncomePerSecond("GeodeMine"), Is.EqualTo(1));
            Assert.That(factory.GetNextUpgradeCost("GeodeMine"), Is.EqualTo(10));
        }

        [Test]
        public void Upgrade_LockedMachine_DoesNotChangeStateEvenWithEnoughBalance()
        {
            var factory = CreateFactory(1000);
            Assert.That(factory.TryUpgrade("GeodeCrusher"), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(1000));
            Assert.That(factory.State.Machines["GeodeCrusher"].Status, Is.EqualTo(MachineStatus.Locked));
            Assert.That(factory.State.Machines["GeodeCrusher"].Level, Is.Zero);
            Assert.That(factory.GetIncomePerSecond("GeodeCrusher"), Is.Zero);
        }

        [TestCase(null)]
        [TestCase("unknown")]
        [TestCase("GeodeMine")]
        public void Unlock_UnavailableMachine_DoesNotChangeState(string id)
        {
            var factory = CreateFactory(100);
            Assert.That(factory.TryUnlock(id), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(100));
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(1));
            Assert.That(factory.State.Machines["GeodeMine"].Level, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("unknown")]
        public void Upgrade_UnknownMachine_DoesNotChangeState(string id)
        {
            var factory = CreateFactory(100);
            Assert.That(factory.TryUpgrade(id), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(100));
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(1));
        }

        [TestCase(0, 0)]
        [TestCase(0.25, 6.75)]
        [TestCase(10, 270)]
        public void AccrueIncome_UsesCurrentProductionAndElapsedSeconds(double seconds, double expected)
        {
            var factory = CreateFactory(310);
            Assert.That(factory.TryUnlock("GeodeCrusher"), Is.True);
            Assert.That(factory.TryUnlock("JewelryWorkshop"), Is.True);
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.True);
            Assert.That(factory.GetTotalIncomePerSecond(), Is.EqualTo(27));
            Assert.That(factory.AccrueIncome(seconds), Is.EqualTo(expected).Within(1e-9));
            Assert.That(factory.State.Balance, Is.EqualTo(expected).Within(1e-9));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void AccrueIncome_InvalidTime_ThrowsWithoutChangingBalance(double seconds)
        {
            var factory = CreateFactory(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => factory.AccrueIncome(seconds));
            Assert.That(factory.State.Balance, Is.EqualTo(5));
        }

        [Test]
        public void AccrueIncome_OverflowingIncome_ThrowsWithoutChangingBalance()
        {
            var factory = CreateFactory(10);
            Assert.That(factory.TryUpgrade("GeodeMine"), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => factory.AccrueIncome(double.MaxValue));
            Assert.That(factory.State.Balance, Is.Zero);
        }

        [Test]
        public void AccrueIncome_OverflowingBalance_ThrowsWithoutChangingBalance()
        {
            var factory = CreateFactory(double.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => factory.AccrueIncome(double.MaxValue));
            Assert.That(factory.State.Balance, Is.EqualTo(double.MaxValue));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Configuration_InvalidBalance_IsRejected(double balance)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateFactory(balance));
        }

        [TestCase(0.5)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Configuration_InvalidMultiplier_IsRejected(double multiplier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FactoryService(new TestConfigSource
            {
                UpgradeCostMultiplier = multiplier,
                Machines = new[] { new MachineConfig("Custom", true, 1, 1, 0, 1) }
            }));
        }

        [Test]
        public void Configuration_EmptyOrDuplicateMachines_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new FactoryService(new TestConfigSource
            {
                Machines = new MachineConfig[0]
            }));
            var config = new MachineConfig("Duplicate", true, 1, 1, 0, 1);
            Assert.Throws<ArgumentException>(() => new FactoryService(new TestConfigSource
            {
                Machines = new[] { config, config }
            }));
        }

        [Test]
        public void MachineConfiguration_InvalidIdOrLevel_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new MachineConfig("", true, 1, 1, 0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MachineConfig("Custom", true, 0, 1, 0, 10));
        }

        [Test]
        public void Configuration_CustomValuesAreUsedAndMultiplierIsSnapshotted()
        {
            var source = new TestConfigSource
            {
                InitialBalance = 100,
                UpgradeCostMultiplier = 2,
                Machines = new[] { new MachineConfig("Custom", true, 3, 7, 8, 4) }
            };
            var factory = new FactoryService(source);
            source.UpgradeCostMultiplier = 9;
            Assert.That(factory.GetIncomePerSecond("Custom"), Is.EqualTo(21));
            Assert.That(factory.GetNextUpgradeCost("Custom"), Is.EqualTo(16));
            Assert.That(factory.TryUpgrade("Custom"), Is.True);
            Assert.That(factory.State.Balance, Is.EqualTo(84));
            Assert.That(factory.GetNextUpgradeCost("Custom"), Is.EqualTo(32));
        }

        [Test]
        public void Upgrade_ZeroBaseCost_RemainsFreeWithOverflowingPower()
        {
            var factory = new FactoryService(new TestConfigSource
            {
                UpgradeCostMultiplier = double.MaxValue,
                Machines = new[] { new MachineConfig("Free", true, 3, 0, 0, 0) }
            });
            Assert.That(factory.GetNextUpgradeCost("Free"), Is.Zero);
            Assert.That(factory.TryUpgrade("Free"), Is.True);
            Assert.That(factory.State.Balance, Is.Zero);
        }

        [Test]
        public void Upgrade_MaximumLevel_DoesNotChangeState()
        {
            var factory = new FactoryService(new TestConfigSource
            {
                Machines = new[] { new MachineConfig("Max", true, int.MaxValue, 0, 0, 0) }
            });
            Assert.That(factory.TryUpgrade("Max"), Is.False);
            Assert.That(factory.State.Machines["Max"].Level, Is.EqualTo(int.MaxValue));
            Assert.That(factory.State.Balance, Is.Zero);
        }

        [Test]
        public void Upgrade_InfinitePrice_DoesNotChangeState()
        {
            var factory = new FactoryService(new TestConfigSource
            {
                InitialBalance = double.MaxValue,
                UpgradeCostMultiplier = double.MaxValue,
                Machines = new[] { new MachineConfig("Expensive", true, 3, 1, 0, 1) }
            });
            Assert.That(factory.GetNextUpgradeCost("Expensive"), Is.EqualTo(double.PositiveInfinity));
            Assert.That(factory.TryUpgrade("Expensive"), Is.False);
            Assert.That(factory.State.Balance, Is.EqualTo(double.MaxValue));
            Assert.That(factory.State.Machines["Expensive"].Level, Is.EqualTo(3));
        }
    }
}
