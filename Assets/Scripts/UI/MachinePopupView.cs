using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JewelryMiniFactory.UI
{
    [DisallowMultipleComponent]
    public sealed class MachinePopupView : MonoBehaviour
    {
        [SerializeField] private Button backdrop;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text productionText;
        [SerializeField] private TMP_Text costText;

        public bool IsOpen => gameObject.activeInHierarchy;
        public bool HasRequiredReferences => backdrop != null && actionButton != null && titleText != null &&
            levelText != null && productionText != null && costText != null;

        public event Action UpgradeRequested;
        public event Action Closed;

        private void OnEnable()
        {
            if (backdrop != null) backdrop.onClick.AddListener(Hide);
            if (actionButton != null) actionButton.onClick.AddListener(RequestUpgrade);
        }

        private void OnDisable()
        {
            if (backdrop != null) backdrop.onClick.RemoveListener(Hide);
            if (actionButton != null) actionButton.onClick.RemoveListener(RequestUpgrade);
        }

        public void Show()
        {
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        public void Render(string title, int level, double income, double nextUpgradeCost, bool canUpgrade)
        {
            titleText.text = title;
            levelText.text = $"Level: {level}";
            productionText.text = $"Production: {income:0.##}/sec";
            costText.text = $"Cost: {nextUpgradeCost:0}";
            actionButton.interactable = canUpgrade;
        }

        private void RequestUpgrade() => UpgradeRequested?.Invoke();
    }
}
