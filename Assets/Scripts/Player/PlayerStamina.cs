using System;
using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    // manage the player stamina, including spending and recovery
    public sealed class PlayerStamina : MonoBehaviour
    {
        private GameplayConfig.PlayerSettings playerConfig;
        private float currentStamina;
        private float recoveryStartsAt;
        private float bonusMaxStamina;

        public event Action<float, float> StaminaChanged;

        public bool IsInitialized => playerConfig != null;
        public float CurrentStamina => currentStamina;
        public float MaxStamina => playerConfig.MaxStamina + bonusMaxStamina;
        public float BonusMaxStamina => bonusMaxStamina;

        private void Start()
        {
            playerConfig = ConfigSystem.Instance.Config.Player;
            currentStamina = playerConfig.MaxStamina;
            recoveryStartsAt = Time.time;
            StaminaChanged?.Invoke(currentStamina, MaxStamina);
        }

        private void Update()
        {
            if (currentStamina >= MaxStamina || Time.time < recoveryStartsAt)
            {
                return;
            }

            currentStamina += playerConfig.StaminaRecoveryPerSecond * Time.deltaTime;
            if (currentStamina > MaxStamina)
            {
                currentStamina = MaxStamina;
            }

            StaminaChanged?.Invoke(currentStamina, MaxStamina);
        }

        public void Refill()
        {
            if (!IsInitialized) return;
            currentStamina = MaxStamina;
            recoveryStartsAt = Time.time;
            StaminaChanged?.Invoke(currentStamina, MaxStamina);
        }

        // Raises the stamina cap and refills by the same amount.
        public void IncreaseMaxStamina(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            bonusMaxStamina += amount;
            currentStamina += amount;
            StaminaChanged?.Invoke(currentStamina, MaxStamina);
        }

        public bool TrySpend(float amount)
        {
            if (currentStamina < amount)
            {
                return false;
            }

            Spend(amount);
            return true;
        }

        public bool Drain(float amount)
        {
            if (currentStamina <= 0f)
            {
                return false;
            }

            float spent = amount < currentStamina ? amount : currentStamina;
            Spend(spent);
            return currentStamina > 0f;
        }

        private void Spend(float amount)
        {
            currentStamina -= amount;
            recoveryStartsAt = Time.time + playerConfig.StaminaRecoveryDelay;
            StaminaChanged?.Invoke(currentStamina, MaxStamina);
        }
        [System.Serializable] public sealed class SaveState { public float currentStamina; public float recoveryStartsAt; public float bonusMaxStamina; }
        public SaveState CaptureState() => new SaveState { currentStamina = currentStamina, recoveryStartsAt = Mathf.Max(0f, recoveryStartsAt - Time.time), bonusMaxStamina = bonusMaxStamina };
        public void RestoreState(SaveState s)
        {
            if (s == null) return;

            currentStamina = s.currentStamina; recoveryStartsAt = Time.time + s.recoveryStartsAt; bonusMaxStamina = s.bonusMaxStamina;
            StaminaChanged?.Invoke(currentStamina, MaxStamina);
        }

    }
}
