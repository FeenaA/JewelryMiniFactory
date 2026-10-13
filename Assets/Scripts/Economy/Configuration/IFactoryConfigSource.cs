using System.Collections.Generic;

namespace JewelryMiniFactory.Economy
{
    public interface IFactoryConfigSource
    {
        double InitialBalance { get; }
        double UpgradeCostMultiplier { get; }
        IReadOnlyList<MachineConfig> GetMachines();
    }
}
