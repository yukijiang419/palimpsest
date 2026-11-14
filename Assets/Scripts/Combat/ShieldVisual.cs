using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Rendering;
using UnityEngine;

namespace Caffeine.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ShieldVisual : MonoBehaviour
    {
        private static readonly int ShieldStrengthId = Shader.PropertyToID("_ShieldStrength");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        [SerializeField] private HealthShield healthShield;
        [SerializeField] private Renderer shieldRenderer;
        [SerializeField, Min(.05f), Tooltip("Seconds for the shield to burn away when broken.")]
        private float dissolveDuration = 1f;

        private MaterialPropertyBlock propertyBlock;
        private float impactRemaining;
        private float impactSeconds;
        private float dissolveRemaining;
        private bool shieldShown;
        private bool hasRefreshed;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            healthShield.VitalsChanged += Refresh;
            healthShield.ShieldImpacted += PlayImpact;
        }

        private void Start()
        {
            Refresh(healthShield);
        }

        private void OnDisable()
        {
            healthShield.VitalsChanged -= Refresh;
            healthShield.ShieldImpacted -= PlayImpact;
            impactRemaining = 0f;
            dissolveRemaining = 0f;
            shieldRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_ImpactStrength", 0f);
            propertyBlock.SetFloat(DissolveId, shieldShown ? 0f : 1f);
            shieldRenderer.SetPropertyBlock(propertyBlock);
            shieldRenderer.enabled = shieldShown;
        }

        private void PlayImpact(Vector3? position, bool broken)
        {
            var settings = ConfigSystem.Instance.Config.Effects;
            if (broken)
            {
                Bounds bounds = shieldRenderer.bounds;
                ArchiveEffectBurst.Play(transform, bounds.center, settings.ShieldColor,
                    settings.ShieldBreakParticles, settings.ShieldBreakSeconds,
                    Mathf.Min(bounds.extents.x, bounds.extents.z), true, .35f);
                return;
            }
            impactSeconds = settings.ShieldHitSeconds;
            impactRemaining = impactSeconds;
            Vector3 direction = position.HasValue
                ? shieldRenderer.transform.InverseTransformPoint(position.Value).normalized : Vector3.up;
            shieldRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector("_ImpactDirection", direction);
            propertyBlock.SetColor("_ImpactColor", settings.ShieldColor);
            propertyBlock.SetFloat("_ImpactLocalized", position.HasValue ? 1f : 0f);
            propertyBlock.SetFloat("_ImpactProgress", 0f);
            propertyBlock.SetFloat("_ImpactStrength", 1f);
            shieldRenderer.SetPropertyBlock(propertyBlock);
        }

        private void Update()
        {
            if (impactRemaining <= 0f && dissolveRemaining <= 0f)
            {
                return;
            }

            shieldRenderer.GetPropertyBlock(propertyBlock);
            if (impactRemaining > 0f)
            {
                impactRemaining = Mathf.Max(0f, impactRemaining - Time.deltaTime);
                propertyBlock.SetFloat("_ImpactProgress", 1f - impactRemaining / impactSeconds);
                propertyBlock.SetFloat("_ImpactStrength", impactRemaining / impactSeconds);
            }

            if (dissolveRemaining > 0f)
            {
                // Breaking burns the shield from 0 to 1.
                dissolveRemaining = Mathf.Max(0f, dissolveRemaining - Time.deltaTime);
                propertyBlock.SetFloat(DissolveId, 1f - dissolveRemaining / dissolveDuration);
            }
            shieldRenderer.SetPropertyBlock(propertyBlock);

            if (dissolveRemaining <= 0f && !shieldShown)
            {
                shieldRenderer.enabled = false;
            }
        }

        private void Refresh(HealthShield source)
        {
            bool hasShield = source.IsInitialized && source.CurrentShield > 0f;
            bool broke = hasRefreshed && shieldShown && !hasShield;
            shieldShown = hasShield;
            hasRefreshed = true;

            shieldRenderer.GetPropertyBlock(propertyBlock);
            if (hasShield)
            {
                // Keep the last fill level while breaking so the dissolve has a shape to burn.
                propertyBlock.SetFloat(ShieldStrengthId, source.CurrentShield / source.MaxShield);
            }

            if (broke)
            {
                dissolveRemaining = dissolveDuration;
            }
            else if (hasShield || dissolveRemaining <= 0f)
            {
                // Every other change snaps straight to the current state.
                dissolveRemaining = 0f;
                propertyBlock.SetFloat(DissolveId, hasShield ? 0f : 1f);
                shieldRenderer.enabled = hasShield;
            }

            shieldRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
