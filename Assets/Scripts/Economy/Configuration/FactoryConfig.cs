using System;
using System.Collections.Generic;
using UnityEngine;

namespace JewelryMiniFactory.Economy
{
    [CreateAssetMenu(fileName = "FactoryConfig", menuName = "Jewelry Mini Factory/Factory Config")]
    public sealed class FactoryConfig : ScriptableObject, IFactoryConfigSource
    {
        [SerializeField, Min(0)] private double initialBalance = 0;
        [SerializeField, Min(1)] private double upgradeCostMultiplier = 1.5;
        [Header("Boost")]
        [SerializeField] private bool boostEnabled = true;
        [SerializeField, Min(0.01f)] private double boostDuration = 30;
        [SerializeField, Min(1)] private double boostMultiplier = 2;
        [Header("Machines")]
        [SerializeField] private MachineSettings[] machines =
        {
            new MachineSettings("GeodeMine", true, 1, 1, 0, 10),
            new MachineSettings("GeodeCrusher", false, 1, 5, 50, 40),
            new MachineSettings("JewelryWorkshop", false, 1, 20, 250, 120)
        };

        public double InitialBalance => initialBalance;
        public double UpgradeCostMultiplier => upgradeCostMultiplier;
        public bool BoostEnabled => boostEnabled;
        public double BoostDuration => boostDuration;
        public double BoostMultiplier => boostMultiplier;

        public IReadOnlyList<MachineConfig> GetMachines()
        {
            if (machines == null) throw new InvalidOperationException("Machines are not configured.");
            var result = new List<MachineConfig>(machines.Length);
            foreach (var machine in machines)
            {
                if (machine == null) throw new InvalidOperationException("Machine configuration is missing.");
                result.Add(machine.ToConfig());
            }
            return result.AsReadOnly();
        }

        [Serializable]
        private sealed class MachineSettings
        {
            [SerializeField] private string id;
            [SerializeField] private bool initiallyUnlocked;
            [Tooltip("Level assigned on initial creation or when the machine is unlocked.")]
            [SerializeField, Min(1)] private int initialLevel;
            [SerializeField, Min(0)] private double baseIncome;
            [SerializeField, Min(0)] private double unlockCost;
            [SerializeField, Min(0)] private double baseUpgradeCost;

            public MachineSettings(string id, bool initiallyUnlocked, int initialLevel,
                double baseIncome, double unlockCost, double baseUpgradeCost)
            {
                this.id = id;
                this.initiallyUnlocked = initiallyUnlocked;
                this.initialLevel = initialLevel;
                this.baseIncome = baseIncome;
                this.unlockCost = unlockCost;
                this.baseUpgradeCost = baseUpgradeCost;
            }

            public MachineConfig ToConfig() => new MachineConfig(id, initiallyUnlocked,
                initialLevel, baseIncome, unlockCost, baseUpgradeCost);
        }
    }
}
