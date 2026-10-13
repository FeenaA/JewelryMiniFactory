using System.Collections.Generic;

namespace JewelryMiniFactory.Economy
{
    public interface IFactoryConfigSource
    {
        double InitialBalance { get; }
        double UpgradeCostMultiplier { get; }
        bool BoostEnabled { get; }
        double BoostDuration { get; }
        double BoostMultiplier { get; }
        IReadOnlyList<MachineConfig> GetMachines();
    }
}
