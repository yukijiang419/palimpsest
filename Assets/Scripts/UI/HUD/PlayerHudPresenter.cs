using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class PlayerHudPresenter : MonoBehaviour
    {
        [SerializeField] private HealthShield playerHealth;
        [SerializeField] private Image staminaFill;
        [SerializeField] private TMP_Text coinText;
        [SerializeField] private Image healthFill;
        private PlayerStamina stamina;
        private PlayerWallet wallet;
        private WeaponManager weapons;
        private WeaponData displayedWeapon;
        [SerializeField] private TMP_Text weaponName;
        [SerializeField] private TMP_Text weaponKey;
        [SerializeField] private WeaponSilhouetteGraphic silhouette;
        [SerializeField] private RectTransform weaponPanel;
        [SerializeField] private RectTransform staminaRoot;
        [SerializeField] private Image walletIcon;
        [SerializeField] private Image weaponIcon;
        private PlayerInputReader inputReader;
        [SerializeField] private RectTransform statsPanel;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private TMP_Text statsHint;
        private bool statsOpen;
        [SerializeField] private RectTransform controlHints;
        [SerializeField] private RectTransform combatHints;
        [SerializeField] private RectTransform puzzleHints;
        private PlayerMoveController motor;
        public HealthShield EffectOwner => playerHealth;
        public RectTransform HealthEffectAnchor => healthFill != null ? healthFill.transform.parent as RectTransform : null;

        private void OnEnable()
        {
            stamina = playerHealth.GetComponent<PlayerStamina>();
            wallet = playerHealth.GetComponent<PlayerWallet>();
            weapons = playerHealth.GetComponentInChildren<WeaponManager>();
            if (weapons == null)
            {
                weapons = FindFirstObjectByType<WeaponManager>();
            }

            inputReader = playerHealth.GetComponentInParent<PlayerInputReader>();
            motor = playerHealth.GetComponent<PlayerMoveController>();
            displayedWeapon = null;
            playerHealth.VitalsChanged += RefreshHealth;
            stamina.StaminaChanged += RefreshStamina;
            wallet.CoinsChanged += RefreshCoins;
            if (weaponPanel != null)
            {
                weaponPanel.gameObject.SetActive(true);
            }

            coinText.gameObject.SetActive(true);
            RefreshHealth(playerHealth);
            RefreshCoins(wallet.Coins);
            // Initial stamina is supplied by its Start event; re-enabled HUDs refresh immediately.
            if (stamina.IsInitialized)
            {
                RefreshStamina(stamina.CurrentStamina, stamina.MaxStamina);
            }
        }

        private void LateUpdate()
        {
            UpdateSideStamina();
            var current = weapons != null ? weapons.Current : null;
            weaponPanel.gameObject.SetActive(current != null);
            UpdateStatsPanel(current);
            UpdateControlHints();
            if (current == null || current == displayedWeapon)
            {
                return;
            }

            displayedWeapon = current;
            weaponName.text = current.weaponName;
            weaponKey.text = $"[{current.slot}]";
            silhouette.TwoHanded = current.isTwoHanded;
            silhouette.SetVerticesDirty();
            // Show the rendered weapon icon (same as the shop); the vector silhouette is only a fallback.
            if (current.icon != null)
            {
                weaponIcon.sprite = current.icon;
            }

            if (weaponIcon != null)
            {
                weaponIcon.gameObject.SetActive(current.icon != null);
            }

            silhouette.gameObject.SetActive(current.icon == null);
        }

        private void UpdateSideStamina()
        {
            if (staminaRoot == null || playerHealth == null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                staminaRoot.gameObject.SetActive(false);
                return;
            }

            var body = playerHealth.GetComponent<CharacterController>();
            Vector3 center = body != null ? body.bounds.center : playerHealth.transform.position + Vector3.up;
            Vector3 screenPoint = camera.WorldToScreenPoint(center);
            bool visible = screenPoint.z > 0f && screenPoint.x >= 0 && screenPoint.x <= Screen.width
                && screenPoint.y >= 0 && screenPoint.y <= Screen.height && !playerHealth.IsDead;
            staminaRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            var canvas = staminaRoot.GetComponentInParent<Canvas>();
            screenPoint.x += 140f * canvas.scaleFactor;
            screenPoint.y += 20f * canvas.scaleFactor;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)staminaRoot.parent,
                screenPoint, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var point);
            staminaRoot.anchoredPosition = point;
        }

        public bool TryGetStaminaScreenPoint(out Vector2 point)
        {
            point = default;
            if (staminaRoot == null || !staminaRoot.gameObject.activeInHierarchy)
            {
                return false;
            }

            var canvas = staminaRoot.GetComponentInParent<Canvas>();
            point = RectTransformUtility.WorldToScreenPoint(
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, staminaRoot.position);
            return true;
        }

        // O toggles a detail panel left of the weapon panel; refreshed every frame so shop upgrades show immediately.
        private void UpdateStatsPanel(WeaponData current)
        {
            bool blocked = inputReader != null && inputReader.IsGameplayBlocked;
            if (!blocked && inputReader != null && inputReader.WeaponStatsPressed)
            {
                statsOpen = !statsOpen;
            }

            statsHint.gameObject.SetActive(current != null);
            bool visible = statsOpen && current != null;
            statsPanel.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            float bonus = weapons.CurrentDamageBonus;
            string damage = bonus > 0f
                ? $"{current.damage + bonus:0.#}  <color=#F5CC70>(+{bonus:0.#})</color>"
                : $"{current.damage:0.#}";
            const string col = "<pos=48%>";
            statsText.text =
                $"<b>{current.weaponName}</b>\n" +
                $"Damage{col}{damage}\n" +
                $"Range{col}{current.range:0.##} m\n" +
                $"Arc{col}{current.arcDegrees:0} deg\n" +
                $"Attack speed{col}{(current.attackSpeed > 0f ? current.attackSpeed : 1f):0.##}x\n" +
                $"Grip{col}{(current.isTwoHanded ? "Two-handed" : "One-handed")}";
        }

        private void UpdateControlHints()
        {
            bool puzzle = motor != null && motor.IsGroundedPuzzle;
            combatHints.gameObject.SetActive(!puzzle);
            puzzleHints.gameObject.SetActive(puzzle);
            controlHints.sizeDelta = new Vector2(280, puzzle ? puzzleHints.sizeDelta.y : combatHints.sizeDelta.y);
            controlHints.gameObject.SetActive(inputReader != null && !inputReader.IsGameplayBlocked);
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.VitalsChanged -= RefreshHealth;
            }

            if (stamina != null)
            {
                stamina.StaminaChanged -= RefreshStamina;
            }

            if (wallet != null)
            {
                wallet.CoinsChanged -= RefreshCoins;
            }

            if (weaponPanel != null)
            {
                weaponPanel.gameObject.SetActive(false);
            }

            if (staminaRoot != null)
            {
                staminaRoot.gameObject.SetActive(false);
            }

            if (statsPanel != null)
            {
                statsPanel.gameObject.SetActive(false);
            }

            if (controlHints != null)
            {
                controlHints.gameObject.SetActive(false);
            }
        }

        private void RefreshHealth(HealthShield source)
        {
            ArchiveUi.SetBar(healthFill, source.CurrentHealth, source.MaxHealth);
        }

        private void RefreshStamina(float current, float maximum)
        {
            ArchiveUi.SetBar(staminaFill, current, maximum);
        }

        private void RefreshCoins(int coins) => coinText.text = coins.ToString("N0");
    }
}
