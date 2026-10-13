using System;

namespace JewelryMiniFactory.Economy
{
    public sealed class MachineConfig
    {
        public string Id { get; }
        public bool InitiallyUnlocked { get; }
        public int InitialLevel { get; }
        public double BaseIncome { get; }
        public double UnlockCost { get; }
        public double BaseUpgradeCost { get; }

        public MachineConfig(string id, bool initiallyUnlocked, int initialLevel,
            double baseIncome, double unlockCost, double baseUpgradeCost)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Machine ID is required.", nameof(id));
            if (initialLevel < 1) throw new ArgumentOutOfRangeException(nameof(initialLevel));
            ValidateAmount(baseIncome, nameof(baseIncome));
            ValidateAmount(unlockCost, nameof(unlockCost));
            ValidateAmount(baseUpgradeCost, nameof(baseUpgradeCost));
            Id = id;
            InitiallyUnlocked = initiallyUnlocked;
            InitialLevel = initialLevel;
            BaseIncome = baseIncome;
            UnlockCost = unlockCost;
            BaseUpgradeCost = baseUpgradeCost;
        }

        internal static void ValidateAmount(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, "Value must be finite and nonnegative.");
        }
    }
}
