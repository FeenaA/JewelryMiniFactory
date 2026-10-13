using System;
using System.Collections.Generic;
using JewelryMiniFactory.Economy;
using JewelryMiniFactory.UI;
using TMPro;
using UnityEngine;

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

        private const float UiRefreshInterval = 0.1f;
        private FactoryService factory;
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
            machinePopup.Hide();
            RefreshUi();
        }

        private void Update()
        {
            if (factory == null) return;
            factory.AccrueIncome(Time.deltaTime);
            uiRefreshElapsed += Time.deltaTime;
            if (uiRefreshElapsed < UiRefreshInterval) return;
            uiRefreshElapsed = 0;
            RefreshUi();
        }

        private void OnUnlockRequested(BuildingView building)
        {
            if (!isActiveAndEnabled || factory == null) return;
            factory.TryUnlock(building.MachineId);
            RefreshUi();
        }

        private void OnOpenRequested(BuildingView building)
        {
            if (!isActiveAndEnabled || factory == null ||
                factory.State.Machines[building.MachineId].Status != MachineStatus.Unlocked) return;
            selectedBuilding = building;
            machinePopup.Show();
            RefreshUi();
        }

        private void OnUpgradeRequested()
        {
            if (!isActiveAndEnabled || factory == null || selectedBuilding == null || !machinePopup.IsOpen) return;
            factory.TryUpgrade(selectedBuilding.MachineId);
            RefreshUi();
        }

        private void OnPopupClosed() => selectedBuilding = null;

        private void RefreshUi()
        {
            var balance = factory.State.Balance;
            balanceText.text = $"Coins: {Math.Floor(balance):0}";
            totalProductionText.text = $"Production: {factory.GetTotalIncomePerSecond():0.##}/sec";
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
                factory.GetIncomePerSecond(id), cost, canUpgrade);
        }

        private void OnDestroy()
        {
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
