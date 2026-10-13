using System;
using System.Collections.Generic;
using System.Linq;
using JewelryMiniFactory.Analytics;
using JewelryMiniFactory.Economy;
using JewelryMiniFactory.UI;
using JewelryMiniFactory.Purchasing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JewelryMiniFactory.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private FactoryConfig factoryConfig;
        [SerializeField] private TMP_Text balanceText;
        [SerializeField] private TMP_Text totalProductionText;
        [SerializeField] private BuildingView[] buildings;
        [SerializeField] private MachinePopupView machinePopup;
        [SerializeField] private Button boostButton;
        [SerializeField] private TMP_Text boostButtonText;
        [SerializeField] private Button buyCoinsButton;
        [SerializeField] private TMP_Text buyCoinsButtonText;

        private const float UiRefreshInterval = 0.1f;
        private const float AutoSaveInterval = 15;
        private readonly SaveService saveService = new SaveService();
        private readonly AnalyticsService analytics = new AnalyticsService(new ConsoleAnalyticsProvider());
        private DateTimeOffset? reportedBoostEndUtc;
        private FactoryService factory;
        private BoostService boost;
        private IapPurchaseService iap;
        private bool purchaseHistoryReady;
        private int inputBlockedThroughFrame = -1;
        private bool IsGameInputBlocked => (iap != null && iap.BlocksGameplayInput) || Time.frameCount <= inputBlockedThroughFrame;
        private DateTimeOffset lastIncomeUtc;
        private bool applicationPaused;
        private bool pauseRequested;
        private bool focusLost;
        private double maxOfflineDuration;
        private float saveElapsed;
        private BuildingView selectedBuilding;
        private float uiRefreshElapsed;

        private void Start()
        {
            try
            {
                InitializeGame(true);
                analytics.Track("game_started");
            }
            catch (Exception exception)
            {
                Debug.LogError("GameController initialization failed: " + exception.Message, this);
                enabled = false;
                return;
            }

            foreach (var building in buildings)
            {
                building.UnlockRequested += OnUnlockRequested;
                building.OpenRequested += OnOpenRequested;
            }
            machinePopup.UpgradeRequested += OnUpgradeRequested;
            machinePopup.Closed += OnPopupClosed;
            if (boostButton != null) boostButton.onClick.AddListener(OnBoostRequested);
            if (buyCoinsButton != null) buyCoinsButton.onClick.AddListener(OnBuyCoinsRequested);
            machinePopup.Hide();
            RefreshUi(DateTimeOffset.UtcNow);
            SaveProgress();
            InitializeIap();
        }

        private void InitializeGame(bool loadSavedProgress)
        {
            if (factoryConfig == null || balanceText == null || totalProductionText == null ||
                machinePopup == null || !machinePopup.HasRequiredReferences || buildings == null)
                throw new InvalidOperationException("Assign Factory Config, both HUD texts, Buildings and all MachinePopupView references in the Inspector.");

            var newFactory = new FactoryService(factoryConfig);
            var newBoost = new BoostService(factoryConfig);
            if (newBoost.Enabled && (boostButton == null || boostButtonText == null))
                throw new InvalidOperationException("Assign Canvas/BoostButton and its Text (TMP) in the Inspector.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var building in buildings)
            {
                if (building == null || !building.HasRequiredReferences)
                    throw new InvalidOperationException("Each BuildingView needs its Machine Id, buttons, state objects and text references.");
                if (!newFactory.State.Machines.ContainsKey(building.MachineId) || !ids.Add(building.MachineId))
                    throw new InvalidOperationException("Building Machine Ids must be unique and match Factory Config: " + building.MachineId);
            }
            if (ids.Count != newFactory.State.Machines.Count)
                throw new InvalidOperationException("Assign one BuildingView for every configured machine.");

            maxOfflineDuration = factoryConfig.MaxOfflineDuration;
            if (double.IsNaN(maxOfflineDuration) || double.IsInfinity(maxOfflineDuration) || maxOfflineDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(factoryConfig.MaxOfflineDuration));
            var now = DateTimeOffset.UtcNow;
            if (loadSavedProgress && saveService.TryLoad(factoryConfig, out var restoredFactory, out var restoredBoost, out var restoredUtc))
            {
                factory = restoredFactory;
                boost = restoredBoost;
                lastIncomeUtc = restoredUtc;
                ApplyOfflineIncomeAndCheckpoint(now);
            }
            else
            {
                factory = newFactory;
                boost = newBoost;
                lastIncomeUtc = now;
            }
            purchaseHistoryReady = saveService.RestorePurchaseHistory(factory);
        }

        private async void InitializeIap()
        {
            try
            {
                if (factoryConfig.CoinsPackSmallReward < 1) throw new ArgumentOutOfRangeException(nameof(factoryConfig.CoinsPackSmallReward));
                if (buyCoinsButton == null || buyCoinsButtonText == null)
                    Debug.LogWarning("Assign the purchase Button and its Text (TMP) to GameController to enable the purchase UI.", this);
                iap = new IapPurchaseService(FulfillPurchase);
                iap.StateChanged += OnIapStateChanged;
                iap.PurchaseFailed += OnPurchaseFailed;
                RefreshIapUi();
                await iap.InitializeAsync();
            }
            catch (Exception exception) { Debug.LogWarning("IAP unavailable: " + exception.Message, this); }
        }

        private bool FulfillPurchase(string transactionId)
        {
            if (factory == null || !purchaseHistoryReady) return false;
            var now = DateTimeOffset.UtcNow;
            if (!applicationPaused) AccrueIncomeUntil(now);
            var wasProcessed = factory.State.ProcessedTransactionIds.Contains(transactionId);
            var fulfilled = factory.TryCreditPurchase(transactionId, factoryConfig.CoinsPackSmallReward, (balance, ids) =>
            {
                var data = SaveService.Capture(factory, boost, lastIncomeUtc);
                data.Balance = balance;
                data.ProcessedTransactionIds = new List<string>(ids);
                return saveService.TrySaveData(data);
            });
            if (fulfilled)
            {
                if (!wasProcessed)
                    analytics.Track("purchase_succeeded", new Dictionary<string, object>
                    {
                        ["product_id"] = IapPurchaseService.ProductId,
                        ["amount"] = factoryConfig.CoinsPackSmallReward
                    });
                saveElapsed = 0;
                RefreshUi(now);
            }
            return fulfilled;
        }

        private void OnPurchaseFailed(string reason, bool cancelled)
        {
            analytics.Track("purchase_failed", new Dictionary<string, object>
            {
                ["product_id"] = IapPurchaseService.ProductId,
                ["reason"] = reason,
                ["cancelled"] = cancelled
            });
        }

        private void OnBuyCoinsRequested()
        {
            if (isActiveAndEnabled && !applicationPaused && !IsGameInputBlocked && purchaseHistoryReady) iap?.Buy();
        }

        private void OnIapStateChanged()
        {
            // Fake Store invokes callbacks before removing its dialog. Swallow that click's entire frame.
            inputBlockedThroughFrame = Time.frameCount;
            if (factory != null) RefreshUi(DateTimeOffset.UtcNow);
        }

        private void RefreshIapUi()
        {
            if (buyCoinsButton == null || buyCoinsButtonText == null) return;
            buyCoinsButton.interactable = !IsGameInputBlocked && purchaseHistoryReady && iap != null && iap.CanPurchase;
            buyCoinsButtonText.text = !purchaseHistoryReady ? "STORE UNAVAILABLE" :
                iap == null ? "STORE LOADING" : iap.IsBusy ? "PURCHASE PROCESSING" :
                iap.IsReady ? $"BUY {factoryConfig.CoinsPackSmallReward} COINS" :
                iap.Status == "STORE LOADING" ? "STORE LOADING" : "STORE UNAVAILABLE";
        }

        public void RestartProgress()
        {
            if (!Application.IsPlaying(gameObject))
            {
                if (!saveService.TryClear()) return;
                factory = null;
                boost = null;
                lastIncomeUtc = default;
                selectedBuilding = null;
                saveElapsed = 0;
                uiRefreshElapsed = 0;
                Debug.Log("JewelryMiniFactory progress restarted.", this);
                return;
            }

            try { InitializeGame(false); }
            catch (Exception exception)
            {
                Debug.LogError("Could not restart progress: " + exception.Message, this);
                return;
            }
            // A new live session must not inherit pause flags from the Inspector confirmation dialog.
            pauseRequested = false;
            focusLost = false;
            applicationPaused = !isActiveAndEnabled;
            var cleared = saveService.TryClear();
            purchaseHistoryReady = saveService.RestorePurchaseHistory(factory);
            selectedBuilding = null;
            saveElapsed = 0;
            uiRefreshElapsed = 0;
            machinePopup.Hide();
            RefreshUi(DateTimeOffset.UtcNow);
            // Checkpoint the fresh session immediately; lifecycle callbacks now use only this state.
            SaveProgress();
            if (cleared) Debug.Log("JewelryMiniFactory progress restarted.", this);
        }

        private void Update()
        {
            if (factory == null || applicationPaused) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            saveElapsed += Time.unscaledDeltaTime;
            if (saveElapsed >= AutoSaveInterval) SaveProgress();
            uiRefreshElapsed += Time.unscaledDeltaTime;
            if (uiRefreshElapsed < UiRefreshInterval) return;
            uiRefreshElapsed = 0;
            RefreshUi(now);
        }

        private void AccrueIncomeUntil(DateTimeOffset now)
        {
            // Do not credit an interval twice if the device clock moves backwards.
            if (now < lastIncomeUtc) return;
            factory.AccrueIncome(boost.GetEffectiveElapsedSeconds(lastIncomeUtc, now));
            lastIncomeUtc = now;
            TrackBoostFinished(now);
        }

        private void TrackBoostFinished(DateTimeOffset now)
        {
            if (!boost.EndsAtUtc.HasValue || now < boost.EndsAtUtc.Value || reportedBoostEndUtc == boost.EndsAtUtc) return;
            reportedBoostEndUtc = boost.EndsAtUtc;
            analytics.Track("boost_finished", new Dictionary<string, object>
            {
                ["duration"] = (boost.EndsAtUtc.Value - boost.StartedAtUtc.Value).TotalSeconds,
                ["multiplier"] = boost.Multiplier
            });
        }

        private void OnBoostRequested()
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused || IsGameInputBlocked) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            if (boost.TryActivate(now))
            {
                SaveProgress();
                analytics.Track("boost_started", new Dictionary<string, object>
                {
                    ["duration"] = boost.DurationSeconds,
                    ["multiplier"] = boost.Multiplier
                });
            }
            RefreshUi(now);
        }

        private void OnEnable()
        {
            UpdateApplicationActivity();
        }

        private void OnApplicationPause(bool paused)
        {
            pauseRequested = paused;
            UpdateApplicationActivity();
        }

        private void OnApplicationFocus(bool focused)
        {
            // Game View losing focus to the Inspector is not Android backgrounding.
            if (Application.isEditor) return;
            focusLost = !focused;
            UpdateApplicationActivity();
        }

        private void OnDisable() => UpdateApplicationActivity();

        private void UpdateApplicationActivity()
        {
            var background = pauseRequested || focusLost || !isActiveAndEnabled;
            if (factory == null)
            {
                applicationPaused = background;
                return;
            }
            if (applicationPaused == background) return;
            var now = DateTimeOffset.UtcNow;
            if (background)
            {
                AccrueIncomeUntil(now);
                applicationPaused = true;
                SaveProgress();
            }
            else
            {
                applicationPaused = false;
                ApplyOfflineIncomeAndCheckpoint(now);
            }
            RefreshUi(now);
        }

        private void ApplyOfflineIncomeAndCheckpoint(DateTimeOffset now)
        {
            try
            {
                var offlineSeconds = Math.Min(Math.Max(0, (now - lastIncomeUtc).TotalSeconds), maxOfflineDuration);
                var amount = SaveService.ApplyOfflineIncome(factory, boost, ref lastIncomeUtc, now, maxOfflineDuration);
                if (amount > 0)
                    analytics.Track("offline_income_applied", new Dictionary<string, object>
                    {
                        ["amount"] = amount,
                        ["offline_seconds"] = offlineSeconds
                    });
            }
            catch (ArgumentException exception)
            {
                // Overflow is rejected atomically by FactoryService; discard this invalid interval.
                if (now > lastIncomeUtc) lastIncomeUtc = now;
                Debug.LogWarning("Offline income was rejected: " + exception.Message, this);
            }
            TrackBoostFinished(now);
            SaveProgress();
        }

        private void SaveProgress()
        {
            saveService.TrySave(factory, boost, lastIncomeUtc);
            saveElapsed = 0;
        }

        private void OnApplicationQuit()
        {
            if (factory == null) return;
            if (!applicationPaused) AccrueIncomeUntil(DateTimeOffset.UtcNow);
            applicationPaused = true;
            SaveProgress();
        }

        private void OnUnlockRequested(BuildingView building)
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused || IsGameInputBlocked) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            var cost = factory.GetUnlockCost(building.MachineId);
            if (factory.TryUnlock(building.MachineId))
            {
                SaveProgress();
                TrackMachineAction("machine_unlocked", building.MachineId, cost);
            }
            RefreshUi(now);
        }

        private void OnOpenRequested(BuildingView building)
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused || IsGameInputBlocked ||
                factory.State.Machines[building.MachineId].Status != MachineStatus.Unlocked) return;
            selectedBuilding = building;
            machinePopup.Show();
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            RefreshUi(now);
        }

        private void OnUpgradeRequested()
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused || IsGameInputBlocked || selectedBuilding == null || !machinePopup.IsOpen) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            var cost = factory.GetNextUpgradeCost(selectedBuilding.MachineId);
            if (factory.TryUpgrade(selectedBuilding.MachineId))
            {
                SaveProgress();
                TrackMachineAction("machine_upgraded", selectedBuilding.MachineId, cost);
            }
            RefreshUi(now);
        }

        private void OnPopupClosed() => selectedBuilding = null;

        private void TrackMachineAction(string eventName, string id, double cost)
        {
            analytics.Track(eventName, new Dictionary<string, object>
            {
                ["machine_id"] = id,
                ["level"] = factory.State.Machines[id].Level,
                ["cost"] = cost
            });
        }

        private void RefreshUi(DateTimeOffset now)
        {
            var inputAllowed = !IsGameInputBlocked;
            var balance = factory.State.Balance;
            var productionMultiplier = boost.GetCurrentMultiplier(now);
            balanceText.text = $"Coins: {Math.Floor(balance):0}";
            totalProductionText.text = $"Production: {factory.GetTotalIncomePerSecond() * productionMultiplier:0.##}/sec";
            RefreshBoostUi(now);
            RefreshIapUi();
            foreach (var building in buildings)
            {
                var state = factory.State.Machines[building.MachineId];
                var unlockCost = factory.GetUnlockCost(building.MachineId);
                building.Render(state.Status == MachineStatus.Unlocked, state.Level,
                    unlockCost, balance >= unlockCost, inputAllowed);
            }

            machinePopup.SetInteractionEnabled(inputAllowed);

            if (selectedBuilding == null || !machinePopup.IsOpen) return;
            var id = selectedBuilding.MachineId;
            var machine = factory.State.Machines[id];
            var cost = factory.GetNextUpgradeCost(id);
            var canUpgrade = machine.Status == MachineStatus.Unlocked && machine.Level < int.MaxValue &&
                !double.IsInfinity(cost) && !double.IsNaN(cost) && balance >= cost && inputAllowed;
            machinePopup.Render(selectedBuilding.DisplayName, machine.Level,
                factory.GetIncomePerSecond(id) * productionMultiplier, cost, canUpgrade);
        }

        private void RefreshBoostUi(DateTimeOffset now)
        {
            if (boostButton == null) return;
            boostButton.gameObject.SetActive(boost.Enabled);
            if (!boost.Enabled) return;
            var active = boost.IsActive(now);
            boostButton.interactable = !active && !IsGameInputBlocked;
            if (!active)
            {
                boostButtonText.text = $"BOOST ×{boost.Multiplier:0.##}";
                return;
            }
            var seconds = (long)Math.Ceiling(boost.GetRemainingSeconds(now));
            boostButtonText.text = $"BOOST {seconds / 60:00}:{seconds % 60:00}";
        }

        private void OnDestroy()
        {
            if (buyCoinsButton != null) buyCoinsButton.onClick.RemoveListener(OnBuyCoinsRequested);
            if (iap != null)
            {
                iap.StateChanged -= OnIapStateChanged;
                iap.PurchaseFailed -= OnPurchaseFailed;
                iap.Dispose();
            }
            if (boostButton != null) boostButton.onClick.RemoveListener(OnBoostRequested);
            if (buildings != null)
                foreach (var building in buildings)
                {
                    if (building == null) continue;
                    building.UnlockRequested -= OnUnlockRequested;
                    building.OpenRequested -= OnOpenRequested;
                }
            if (machinePopup == null) return;
            machinePopup.UpgradeRequested -= OnUpgradeRequested;
            machinePopup.Closed -= OnPopupClosed;
        }
    }
}
