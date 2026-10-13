using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace JewelryMiniFactory.Economy
{
    public sealed class GameState
    {
        public double Balance { get; private set; }
        public IReadOnlyDictionary<string, MachineState> Machines { get; }
        private readonly List<string> processedTransactions;
        public IReadOnlyList<string> ProcessedTransactionIds { get; }

        internal GameState(double balance, IDictionary<string, MachineState> machines, IEnumerable<string> transactionIds = null)
        {
            Balance = balance;
            Machines = new ReadOnlyDictionary<string, MachineState>(machines);
            processedTransactions = transactionIds == null ? new List<string>() : new List<string>(transactionIds);
            ProcessedTransactionIds = processedTransactions.AsReadOnly();
        }

        internal void RecordTransaction(string id) => processedTransactions.Add(id);

        internal void SetBalance(double value)
        {
            MachineConfig.ValidateAmount(value, nameof(value));
            Balance = value;
        }
    }
}
