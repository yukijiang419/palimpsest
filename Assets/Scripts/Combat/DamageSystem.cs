using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    public enum Faction
    {
        Player,
        Enemy
    }

    public enum DamageSource
    {
        NormalAttack,
        Path
    }

    public readonly struct DamageRequest
    {
        public DamageRequest(DamageSource source, Faction attackerFaction, float baseDamage)
        {
            Source = source;
            AttackerFaction = attackerFaction;
            BaseDamage = baseDamage;
        }

        public DamageSource Source { get; }
        public Faction AttackerFaction { get; }
        public float BaseDamage { get; }
    }

    public readonly struct DamageResult
    {
        public static DamageResult Empty => new(0f, 0f);

        public DamageResult(float shieldLoss, float healthLoss)
        {
            ShieldLoss = shieldLoss;
            HealthLoss = healthLoss;
        }

        public float ShieldLoss { get; }
        public float HealthLoss { get; }
        public bool HasEffect => ShieldLoss > 0f || HealthLoss > 0f;
    }

    public sealed class DamageSystem : MonoBehaviour
    {
        private GameplayConfig.DamageSettings damageConfig;

        private void Start()
        {
            damageConfig = ConfigSystem.Instance.Config.Damage;
        }

        public DamageResult Resolve(HealthShield target, DamageRequest request, Vector3? hitPosition = null)
        {
            if (!target || !target.IsInitialized || target.IsDead || target.IsRespawnProtected)
            {
                return DamageResult.Empty;
            }

            if (request.Source == DamageSource.NormalAttack && request.AttackerFaction == target.Faction)
            {
                return DamageResult.Empty;
            }

            if (target.IsShieldImmune)
            {
                if (request.Source == DamageSource.NormalAttack && request.BaseDamage > 0f)
                {
                    Caffeine.Gameplay.Presentation.CombatAudioController.Play(
                        Caffeine.Gameplay.Presentation.CombatSound.ShieldHit, target.transform.position + Vector3.up);
                    target.NotifyShieldImpact(hitPosition, false);
                }
                return DamageResult.Empty;
            }

            target.ClearExpiredStatuses();
            DamageResult result = Calculate(
                target.CurrentHealth,
                target.CurrentShield,
                request.BaseDamage,
                target.Vulnerability,
                target.DamageReduction,
                damageConfig.Normal.MinimumDamage,
                request.Source == DamageSource.NormalAttack ? damageConfig.Normal.ShieldDamageReduction : 0f);

            if (result.HasEffect)
            {
                target.ApplyResolvedImpact(result, hitPosition);
            }

            return result;
        }

        public DamageResult BreakShield(HealthShield target)
        {
            if (!target || !target.IsInitialized || target.IsDead || target.CurrentShield <= 0f || target.IsShieldImmune)
            {
                return DamageResult.Empty;
            }

            DamageResult result = new(target.CurrentShield, 0f);
            target.ApplyResolvedDamage(result);
            return result;
        }

        // Calculate one hit without changing the target.
        internal static DamageResult Calculate(
            float currentHealth,
            float currentShield,
            float baseDamage,
            float vulnerability,
            float reduction,
            float minimumDamage,
            float shieldReduction)
        {
            float damage = baseDamage * vulnerability * (1f - reduction);
            if (currentShield > 0f)
            {
                damage *= 1f - shieldReduction;
            }

            if (damage < minimumDamage)
            {
                damage = minimumDamage;
            }

            if (currentShield > 0f)
            {
                float shieldLoss = damage < currentShield ? damage : currentShield;
                return new DamageResult(shieldLoss, 0f);
            }

            float healthLoss = damage < currentHealth ? damage : currentHealth;
            return new DamageResult(0f, healthLoss);
        }
    }
}
