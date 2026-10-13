using System;
using System.Collections.Generic;
using UnityEngine;

namespace JewelryMiniFactory.Economy
{
    public sealed class SaveService
    {
        public const string SaveKey = "JewelryMiniFactory.Save.v1";
        public const string PurchaseHistoryKey = "JewelryMiniFactory.PurchaseHistory.v1";
        private const int SaveVersion = 1;

        public bool TryClear()
        {
            try
            {
                var ids = new HashSet<string>(ReadPurchaseHistory(), StringComparer.Ordinal);
                if (TryDeserialize(PlayerPrefs.GetString(SaveKey), out var previous) && previous.ProcessedTransactionIds != null)
                    foreach (var id in previous.ProcessedTransactionIds)
                        if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                // Restart deletes gameplay progress, but cannot allow an old paid order to grant twice.
                PlayerPrefs.SetString(PurchaseHistoryKey, JsonUtility.ToJson(new PurchaseHistoryData { TransactionIds = new List<string>(ids) }));
                PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not clear progress: " + exception.Message);
                return false;
            }
        }

        public bool TryLoad(IFactoryConfigSource source, out FactoryService factory,
            out BoostService boost, out DateTimeOffset lastIncomeUtc)
        {
            factory = null;
            boost = null;
            lastIncomeUtc = default;
            try
            {
                if (!PlayerPrefs.HasKey(SaveKey)) return false;
                var json = PlayerPrefs.GetString(SaveKey);
                if (TryDeserialize(json, out var data) && TryRestore(data, source, out factory, out boost, out lastIncomeUtc))
                {
                    RestorePurchaseHistory(factory);
                    return true;
                }
                Debug.LogWarning("Saved progress is invalid; starting a new game.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not load progress; starting a new game: " + exception.Message);
            }
            return false;
        }

        public bool TrySave(FactoryService factory, BoostService boost, DateTimeOffset lastIncomeUtc)
        {
            try { return TrySaveData(Capture(factory, boost, lastIncomeUtc)); }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not capture progress: " + exception.Message);
                return false;
            }
        }

        public bool TrySaveData(GameSaveData data)
        {
            var hadSave = PlayerPrefs.HasKey(SaveKey);
            var previous = PlayerPrefs.GetString(SaveKey, "");
            try
            {
                PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                // Do not leave an uncommitted reward/ID staged for a later unrelated PlayerPrefs.Save.
                try
                {
                    if (hadSave) PlayerPrefs.SetString(SaveKey, previous);
                    else PlayerPrefs.DeleteKey(SaveKey);
                }
                catch (Exception) { }
                Debug.LogWarning("Could not save progress: " + exception.Message);
                return false;
            }
        }

        public static GameSaveData Capture(FactoryService factory, BoostService boost, DateTimeOffset lastIncomeUtc)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (boost == null) throw new ArgumentNullException(nameof(boost));
            var data = new GameSaveData
            {
                Version = SaveVersion,
                Balance = factory.State.Balance,
                LastIncomeUtcTicks = lastIncomeUtc.UtcDateTime.Ticks,
                ProcessedTransactionIds = new List<string>(factory.State.ProcessedTransactionIds),
                Machines = new List<MachineSaveData>(factory.State.Machines.Count)
            };
            foreach (var machine in factory.State.Machines.Values)
                data.Machines.Add(new MachineSaveData { Id = machine.Id, Status = machine.Status, Level = machine.Level });
            if (boost.StartedAtUtc.HasValue && boost.EndsAtUtc.Value > lastIncomeUtc)
            {
                data.BoostStartedUtcTicks = boost.StartedAtUtc.Value.UtcDateTime.Ticks;
                data.BoostEndsUtcTicks = boost.EndsAtUtc.Value.UtcDateTime.Ticks;
            }
            return data;
        }

        public bool RestorePurchaseHistory(FactoryService factory)
        {
            try
            {
                factory.RememberTransactions(ReadPurchaseHistory());
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Purchase history is invalid; purchases are disabled: " + exception.Message);
                return false;
            }
        }

        private static IReadOnlyList<string> ReadPurchaseHistory()
        {
            var json = PlayerPrefs.GetString(PurchaseHistoryKey, "");
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            var history = JsonUtility.FromJson<PurchaseHistoryData>(json);
            if (history?.TransactionIds == null) throw new ArgumentException("Invalid purchase history.");
            foreach (var id in history.TransactionIds)
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Invalid purchase history ID.");
            return history.TransactionIds;
        }

        public static bool TryDeserialize(string json, out GameSaveData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try { data = JsonUtility.FromJson<GameSaveData>(json); }
            catch (Exception) { return false; }
            return data != null;
        }

        // Restoration is all-or-nothing and returns new services, not writable game-state setters.
        public static bool TryRestore(GameSaveData data, IFactoryConfigSource source,
            out FactoryService factory, out BoostService boost, out DateTimeOffset lastIncomeUtc)
        {
            factory = null;
            boost = null;
            lastIncomeUtc = default;
            try
            {
                if (data == null || data.Version != SaveVersion || data.LastIncomeUtcTicks <= 0) return false;
                var last = new DateTimeOffset(data.LastIncomeUtcTicks, TimeSpan.Zero);
                var restoredBoost = new BoostService(source);
                if (data.BoostStartedUtcTicks != 0 || data.BoostEndsUtcTicks != 0)
                {
                    if (data.BoostStartedUtcTicks <= 0 || data.BoostEndsUtcTicks <= data.BoostStartedUtcTicks ||
                        data.BoostStartedUtcTicks > data.LastIncomeUtcTicks) return false;
                    restoredBoost.RestoreWindow(new DateTimeOffset(data.BoostStartedUtcTicks, TimeSpan.Zero),
                        new DateTimeOffset(data.BoostEndsUtcTicks, TimeSpan.Zero));
                }
                var restoredFactory = FactoryService.Restore(source, data);
                factory = restoredFactory;
                boost = restoredBoost;
                lastIncomeUtc = last;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Credits the first capped part of absence using the existing Boost and economy services.
        /// Advances the checkpoint to now, discarding excess time. Future checkpoints are retained
        /// when the device clock moves backwards, so previously credited time cannot be reused.
        /// </summary>
        public static double ApplyOfflineIncome(FactoryService factory, BoostService boost,
            ref DateTimeOffset lastIncomeUtc, DateTimeOffset nowUtc, double maxOfflineDuration)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (boost == null) throw new ArgumentNullException(nameof(boost));
            MachineConfig.ValidateAmount(maxOfflineDuration, nameof(maxOfflineDuration));
            if (nowUtc <= lastIncomeUtc) return 0;
            var elapsed = Math.Min((nowUtc - lastIncomeUtc).TotalSeconds, maxOfflineDuration);
            var cappedEnd = elapsed == (nowUtc - lastIncomeUtc).TotalSeconds ? nowUtc : lastIncomeUtc.AddSeconds(elapsed);
            var income = factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(lastIncomeUtc, cappedEnd));
            lastIncomeUtc = nowUtc.ToUniversalTime();
            return income;
        }
    }
}
