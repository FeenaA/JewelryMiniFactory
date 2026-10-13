namespace JewelryMiniFactory.Economy
{
    public enum MachineStatus { Locked, Unlocked }

    public sealed class MachineState
    {
        public string Id { get; }
        public MachineStatus Status { get; private set; }
        public int Level { get; private set; }

        internal MachineState(MachineConfig config)
        {
            Id = config.Id;
            Status = config.InitiallyUnlocked ? MachineStatus.Unlocked : MachineStatus.Locked;
            Level = config.InitiallyUnlocked ? config.InitialLevel : 0;
        }

        internal MachineState(string id, MachineStatus status, int level)
        {
            Id = id;
            Status = status;
            Level = level;
        }

        internal void Unlock(int level)
        {
            Status = MachineStatus.Unlocked;
            Level = level;
        }

        internal void Upgrade() => Level++;
    }
}
