using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JewelryMiniFactory.UI
{
    [DisallowMultipleComponent]
    public sealed class BuildingView : MonoBehaviour
    {
        [SerializeField] private string machineId;
        [SerializeField] private string displayName;
        [SerializeField] private Button buildingButton;
        [SerializeField] private GameObject lockedState;
        [SerializeField] private GameObject unlockedState;
        [SerializeField] private Button unlockButton;
        [SerializeField] private TMP_Text unlockCostText;
        [SerializeField] private TMP_Text levelText;

        private Image buildingImage;

        public string MachineId => machineId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? machineId : displayName;
        public bool HasRequiredReferences => !string.IsNullOrWhiteSpace(machineId) && buildingButton != null &&
            lockedState != null && unlockedState != null && unlockButton != null && unlockCostText != null && levelText != null;

        public event Action<BuildingView> UnlockRequested;
        public event Action<BuildingView> OpenRequested;

        private void Awake()
        {
            buildingImage = GetComponent<Image>();
        }

        private void OnEnable()
        {
            if (buildingButton != null) buildingButton.onClick.AddListener(RequestOpen);
            if (unlockButton != null) unlockButton.onClick.AddListener(RequestUnlock);
        }

        private void OnDisable()
        {
            if (buildingButton != null) buildingButton.onClick.RemoveListener(RequestOpen);
            if (unlockButton != null) unlockButton.onClick.RemoveListener(RequestUnlock);
        }

        public void Render(bool unlocked, int level, double unlockCost, bool canUnlock)
        {
            lockedState.SetActive(!unlocked);
            unlockedState.SetActive(unlocked);
            // The root Button's disabled tint reduces alpha. Let the Image own its state colour.
            buildingButton.transition = Selectable.Transition.None;
            buildingButton.interactable = unlocked;
            buildingImage.CrossFadeColor(Color.white, 0, true, true);
            buildingImage.color = unlocked ? Color.white : new Color32(170, 170, 170, 255);
            unlockButton.interactable = !unlocked && canUnlock;
            unlockCostText.text = $"{unlockCost:0}";
            levelText.text = $"Lv. {level}";
        }

        private void RequestUnlock() => UnlockRequested?.Invoke(this);
        private void RequestOpen() => OpenRequested?.Invoke(this);
    }
}
