using System;
using System.Collections.Generic;

namespace JewelryMiniFactory.Economy
{
    /// <summary>Owns a new game and takes a validated configuration snapshot at construction.</summary>
    public sealed class FactoryService
    {
        private readonly Dictionary<string, MachineConfig> configs = new Dictionary<string, MachineConfig>(StringComparer.Ordinal);
        private readonly double upgradeCostMultiplier;
        public GameState State { get; }

        public FactoryService(IFactoryConfigSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var initialBalance = source.InitialBalance;
            MachineConfig.ValidateAmount(initialBalance, nameof(source.InitialBalance));
            upgradeCostMultiplier = source.UpgradeCostMultiplier;
            MachineConfig.ValidateAmount(upgradeCostMultiplier, nameof(source.UpgradeCostMultiplier));
            if (upgradeCostMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(source.UpgradeCostMultiplier));
            var machines = source.GetMachines();
            if (machines == null || machines.Count == 0) throw new ArgumentException("At least one machine is required.", nameof(source));
            var states = new Dictionary<string, MachineState>(StringComparer.Ordinal);
            foreach (var config in machines)
            {
                if (config == null || configs.ContainsKey(config.Id))
                    throw new ArgumentException("Machine configurations must be non-null with unique IDs.", nameof(source));
                configs.Add(config.Id, config);
                states.Add(config.Id, new MachineState(config));
            }
            State = new GameState(initialBalance, states);
        }

        public bool TryUnlock(string id)
        {
            if (id == null || !State.Machines.TryGetValue(id, out var machine) || machine.Status != MachineStatus.Locked)
                return false;
            var config = configs[id];
            if (State.Balance < config.UnlockCost) return false;
            State.SetBalance(State.Balance - config.UnlockCost);
            machine.Unlock(config.InitialLevel);
            return true;
        }

        public bool TryUpgrade(string id)
        {
            if (id == null || !State.Machines.TryGetValue(id, out var machine) ||
                machine.Status != MachineStatus.Unlocked || machine.Level == int.MaxValue) return false;
            var cost = GetNextUpgradeCost(id);
            if (double.IsInfinity(cost) || State.Balance < cost) return false;
            State.SetBalance(State.Balance - cost);
            machine.Upgrade();
            return true;
        }

        public double GetIncomePerSecond(string id)
        {
            var machine = State.Machines[id];
            return machine.Status == MachineStatus.Unlocked ? configs[id].BaseIncome * machine.Level : 0;
        }

        /// <summary>Locked machines have no available upgrade and report zero cost.</summary>
        public double GetNextUpgradeCost(string id)
        {
            var machine = State.Machines[id];
            if (machine.Status == MachineStatus.Locked) return 0;
            var baseCost = configs[id].BaseUpgradeCost;
            return baseCost == 0 ? 0 : baseCost * Math.Pow(upgradeCostMultiplier, machine.Level - 1);
        }

        public double GetTotalIncomePerSecond()
        {
            double income = 0;
            foreach (var id in configs.Keys) income += GetIncomePerSecond(id);
            return income;
        }

        /// <summary>Accrues seconds supplied by the caller; does not read Unity time.</summary>
        public double AccrueIncome(double elapsedSeconds)
        {
            MachineConfig.ValidateAmount(elapsedSeconds, nameof(elapsedSeconds));
            if (elapsedSeconds == 0) return 0;
            var income = GetTotalIncomePerSecond() * elapsedSeconds;
            MachineConfig.ValidateAmount(income, nameof(income));
            State.SetBalance(State.Balance + income);
            return income;
        }
    }
}
