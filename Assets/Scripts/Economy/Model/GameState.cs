using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace JewelryMiniFactory.Economy
{
    public sealed class GameState
    {
        public double Balance { get; private set; }
        public IReadOnlyDictionary<string, MachineState> Machines { get; }

        internal GameState(double balance, IDictionary<string, MachineState> machines)
        {
            Balance = balance;
            Machines = new ReadOnlyDictionary<string, MachineState>(machines);
        }

        internal void SetBalance(double value)
        {
            MachineConfig.ValidateAmount(value, nameof(value));
            Balance = value;
        }
    }
}
