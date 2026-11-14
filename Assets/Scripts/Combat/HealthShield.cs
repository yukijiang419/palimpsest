using System;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Presentation;
using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    [DefaultExecutionOrder(-100)]
    public sealed class HealthShield : MonoBehaviour
    {
        [field: SerializeField] public Faction Faction { get; private set; } = Faction.Player;

        // Optional per-object values for enemies such as the Boss; 0 keeps the shared enemy config.
        [SerializeField, Min(0f)] private float maxHealthOverride;
        [SerializeField, Min(0f)] private float maxShieldOverride;
        [SerializeField, Tooltip("While the shield is up, no attack can damage it (only ReduceShield can). Used by the Boss.")]
        private bool shieldBlocksAllDamage;

        public bool IsShieldImmune => shieldBlocksAllDamage && CurrentShield > 0f;

        public float CurrentHealth { get; private set; }
        public float MaxHealth { get; private set; }
        public float BonusMaxHealth { get; private set; }   // Part of MaxHealth bought in the shop
        public float CurrentShield { get; private set; }
        public float MaxShield { get; private set; }
        public float DamageReduction { get; private set; }
        public float Vulnerability { get; private set; } = 1f;
        public float ReductionExpiresAt => reductionExpiresAt;
        public float VulnerabilityExpiresAt => vulnerabilityExpiresAt;
        public bool IsDead { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsRespawnProtected => Time.time < respawnProtectionEndsAt;

        public event Action<HealthShield> VitalsChanged;
        public event Action<HealthShield> Died;
        public event Action<Vector3?, bool> ShieldImpacted;

        private float reductionExpiresAt;
        private float vulnerabilityExpiresAt;
        private float respawnProtectionEndsAt;

        private void Start()
        {
            GameplayConfig config = ConfigSystem.Instance.Config;

            if (Faction == Faction.Player)
            {
                Initialize(config.Player.MaxHealth, 0f);
            }
            else
            {
                Initialize(maxHealthOverride > 0f ? maxHealthOverride : config.Enemy.MaxHealth,
                    maxShieldOverride > 0f ? maxShieldOverride : config.Enemy.MaxShield);
            }
        }

        internal void Initialize(float maxHealth, float maxShield)
        {
            MaxHealth = maxHealth;
            MaxShield = maxShield;
            CurrentHealth = maxHealth;
            CurrentShield = maxShield;
            IsDead = maxHealth <= 0f;
            IsInitialized = true;
            DamageReduction = 0f;
            reductionExpiresAt = 0f;
            Vulnerability = 1f;
            vulnerabilityExpiresAt = 0f;
            respawnProtectionEndsAt = 0f;
            VitalsChanged?.Invoke(this);
        }

        public bool Revive(float protectionSeconds)
        {
            if (!IsInitialized || !IsDead) return false;
            IsDead = false;
            CurrentHealth = MaxHealth;
            CurrentShield = MaxShield;
            DamageReduction = 0f;
            reductionExpiresAt = 0f;
            Vulnerability = 1f;
            vulnerabilityExpiresAt = 0f;
            respawnProtectionEndsAt = Time.time + Mathf.Max(0f, protectionSeconds);
            VitalsChanged?.Invoke(this);
            return true;
        }

        public void Heal(float amount)
        {
            if (!IsInitialized || IsDead || amount <= 0f)
            {
                return;
            }

            float nextHealth = CurrentHealth + amount;
            if (nextHealth > MaxHealth)
            {
                nextHealth = MaxHealth;
            }

            if (nextHealth != CurrentHealth)
            {
                CurrentHealth = nextHealth;
                VitalsChanged?.Invoke(this);
            }
        }

        // Raises the health cap and heals by the same amount so the new capacity is usable right away.
        public void IncreaseMaxHealth(float amount)
        {
            if (!IsInitialized || IsDead || amount <= 0f)
            {
                return;
            }

            MaxHealth += amount;
            BonusMaxHealth += amount;
            CurrentHealth += amount;
            VitalsChanged?.Invoke(this);
        }

        public void RestoreForSceneTravel(float health, float shield)
        {
            if (!IsInitialized || IsDead) return;
            CurrentHealth = Mathf.Clamp(health, 1f, MaxHealth);
            CurrentShield = Mathf.Clamp(shield, 0f, MaxShield);
            VitalsChanged?.Invoke(this);
        }

        // Removes shield directly, bypassing immunity; the Boss loses shield this way when its minions die.
        public void ReduceShield(float amount)
        {
            if (!IsInitialized || IsDead || amount <= 0f || CurrentShield <= 0f)
            {
                return;
            }

            CurrentShield = Mathf.Max(0f, CurrentShield - amount);
            NotifyShieldImpact(null, CurrentShield <= 0f);
            VitalsChanged?.Invoke(this);
        }

        internal void ApplyDamageReduction(float reduction, float duration)
        {
            DamageReduction = reduction;
            reductionExpiresAt = duration > 0f ? Time.time + duration : 0f;
        }

        internal void ApplyVulnerability(float multiplier, float duration)
        {
            Vulnerability = multiplier;
            vulnerabilityExpiresAt = duration > 0f ? Time.time + duration : 0f;
        }

        internal void ClearExpiredStatuses()
        {
            float now = Time.time;
            if (reductionExpiresAt > 0f && reductionExpiresAt <= now)
            {
                DamageReduction = 0f;
                reductionExpiresAt = 0f;
            }

            if (vulnerabilityExpiresAt > 0f && vulnerabilityExpiresAt <= now)
            {
                Vulnerability = 1f;
                vulnerabilityExpiresAt = 0f;
            }
        }

        internal void ApplyResolvedDamage(DamageResult result)
        {
            ApplyResolvedImpact(result, null);
        }

        internal void NotifyShieldImpact(Vector3? position, bool broken)
        {
            ShieldImpacted?.Invoke(position, broken);
        }

        internal void ApplyResolvedImpact(DamageResult result, Vector3? position)
        {
            float previousShield = CurrentShield;
            CurrentShield -= result.ShieldLoss;
            CurrentHealth -= result.HealthLoss;
            CombatAudioController.PlayDamage(this, result, previousShield);
            if (previousShield > 0f && result.ShieldLoss > 0f)
            {
                NotifyShieldImpact(position, CurrentShield <= 0f);
            }
            VitalsChanged?.Invoke(this);

            if (!IsDead && CurrentHealth <= 0f)
            {
                IsDead = true;
                Died?.Invoke(this);
            }
        }
        [System.Serializable] public sealed class SaveState { public float CurrentHealth; public float MaxHealth; public float BonusMaxHealth; public float CurrentShield; public float MaxShield; public float DamageReduction; public float Vulnerability; public float reductionExpiresAt; public float vulnerabilityExpiresAt; public float respawnProtectionEndsAt; public bool IsDead; }
        public SaveState CaptureState() => new SaveState { CurrentHealth = CurrentHealth, MaxHealth = MaxHealth, BonusMaxHealth = BonusMaxHealth, CurrentShield = CurrentShield, MaxShield = MaxShield, DamageReduction = DamageReduction, Vulnerability = Vulnerability, reductionExpiresAt = Mathf.Max(0f, reductionExpiresAt - Time.time), vulnerabilityExpiresAt = Mathf.Max(0f, vulnerabilityExpiresAt - Time.time), respawnProtectionEndsAt = Mathf.Max(0f, respawnProtectionEndsAt - Time.time), IsDead = IsDead };
        public void RestoreState(SaveState s)
        {
            if (s == null) return;

            CurrentHealth = s.CurrentHealth; MaxHealth = s.MaxHealth; BonusMaxHealth = s.BonusMaxHealth; CurrentShield = s.CurrentShield; MaxShield = s.MaxShield; DamageReduction = s.DamageReduction; Vulnerability = s.Vulnerability; reductionExpiresAt = Time.time + s.reductionExpiresAt; vulnerabilityExpiresAt = Time.time + s.vulnerabilityExpiresAt; respawnProtectionEndsAt = Time.time + s.respawnProtectionEndsAt; IsDead = s.IsDead;
            IsInitialized = true; VitalsChanged?.Invoke(this);
        }

    }
}
