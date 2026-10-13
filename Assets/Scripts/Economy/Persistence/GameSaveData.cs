using System;
using System.Collections.Generic;

namespace JewelryMiniFactory.Economy
{
    [Serializable]
    public sealed class GameSaveData
    {
        public int Version;
        public double Balance;
        public List<MachineSaveData> Machines;
        public long LastIncomeUtcTicks;
        public long BoostStartedUtcTicks;
        public long BoostEndsUtcTicks;
        public List<string> ProcessedTransactionIds;
    }

    [Serializable]
    public sealed class PurchaseHistoryData
    {
        public List<string> TransactionIds;
    }

    [Serializable]
    public sealed class MachineSaveData
    {
        public string Id;
        public MachineStatus Status;
        public int Level;
    }
}
