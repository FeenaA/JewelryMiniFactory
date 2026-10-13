using System;
using System.Collections.Generic;
using JewelryMiniFactory.Economy;
using JewelryMiniFactory.UI;
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

        private const float UiRefreshInterval = 0.1f;
        private FactoryService factory;
        private BoostService boost;
        private DateTimeOffset lastIncomeUtc;
        private bool applicationPaused;
        private BuildingView selectedBuilding;
        private float uiRefreshElapsed;

        private void Start()
        {
            try
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

                factory = newFactory;
                boost = newBoost;
                lastIncomeUtc = DateTimeOffset.UtcNow;
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
            machinePopup.Hide();
            RefreshUi(lastIncomeUtc);
        }

        private void Update()
        {
            if (factory == null || applicationPaused) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
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
        }

        private void OnBoostRequested()
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            boost.TryActivate(now);
            RefreshUi(now);
        }

        private void OnEnable()
        {
            if (factory != null) lastIncomeUtc = DateTimeOffset.UtcNow;
        }

        private void OnApplicationPause(bool paused)
        {
            if (factory == null) return;
            var now = DateTimeOffset.UtcNow;
            if (paused && !applicationPaused) AccrueIncomeUntil(now);
            applicationPaused = paused;
            // Boost ages in UTC while paused, but offline income is outside this stage.
            if (!paused) lastIncomeUtc = now;
            RefreshUi(now);
        }

        private void OnUnlockRequested(BuildingView building)
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            factory.TryUnlock(building.MachineId);
            RefreshUi(now);
        }

        private void OnOpenRequested(BuildingView building)
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused ||
                factory.State.Machines[building.MachineId].Status != MachineStatus.Unlocked) return;
            selectedBuilding = building;
            machinePopup.Show();
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            RefreshUi(now);
        }

        private void OnUpgradeRequested()
        {
            if (!isActiveAndEnabled || factory == null || applicationPaused || selectedBuilding == null || !machinePopup.IsOpen) return;
            var now = DateTimeOffset.UtcNow;
            AccrueIncomeUntil(now);
            factory.TryUpgrade(selectedBuilding.MachineId);
            RefreshUi(now);
        }

        private void OnPopupClosed() => selectedBuilding = null;

        private void RefreshUi(DateTimeOffset now)
        {
            var balance = factory.State.Balance;
            var productionMultiplier = boost.GetCurrentMultiplier(now);
            balanceText.text = $"Coins: {Math.Floor(balance):0}";
            totalProductionText.text = $"Production: {factory.GetTotalIncomePerSecond() * productionMultiplier:0.##}/sec";
            RefreshBoostUi(now);
            foreach (var building in buildings)
            {
                var state = factory.State.Machines[building.MachineId];
                var unlockCost = factory.GetUnlockCost(building.MachineId);
                building.Render(state.Status == MachineStatus.Unlocked, state.Level,
                    unlockCost, balance >= unlockCost);
            }

            if (selectedBuilding == null || !machinePopup.IsOpen) return;
            var id = selectedBuilding.MachineId;
            var machine = factory.State.Machines[id];
            var cost = factory.GetNextUpgradeCost(id);
            var canUpgrade = machine.Status == MachineStatus.Unlocked && machine.Level < int.MaxValue &&
                !double.IsInfinity(cost) && !double.IsNaN(cost) && balance >= cost;
            machinePopup.Render(selectedBuilding.DisplayName, machine.Level,
                factory.GetIncomePerSecond(id) * productionMultiplier, cost, canUpgrade);
        }

        private void RefreshBoostUi(DateTimeOffset now)
        {
            if (boostButton == null) return;
            boostButton.gameObject.SetActive(boost.Enabled);
            if (!boost.Enabled) return;
            var active = boost.IsActive(now);
            boostButton.interactable = !active;
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
