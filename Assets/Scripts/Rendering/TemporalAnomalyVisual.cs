using UnityEngine;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Player;

namespace Caffeine.Gameplay.Rendering
{
    [DisallowMultipleComponent]
    public sealed class TemporalAnomalyVisual : MonoBehaviour
    {
        private static readonly int PhaseId = Shader.PropertyToID("_AnomalyPhase");
        private static readonly int IntervalId = Shader.PropertyToID("_AnomalyInterval");
        private static readonly int DurationId = Shader.PropertyToID("_AnomalyDuration");
        private static readonly int OffsetId = Shader.PropertyToID("_AnomalyOffset");
        private static readonly int FullBlinkId = Shader.PropertyToID("_AnomalyFullBlink");
        private static readonly int PassThroughId = Shader.PropertyToID("_PassThroughHint");

        [SerializeField] private Renderer[] targetRenderers;
        [SerializeField, Min(0.1f)] private float interval = 7f;
        [SerializeField, Min(0f)] private float duration = 0.25f;
        [SerializeField, Range(0f, 0.2f)] private float offset = 0.08f;
        [SerializeField] private float phase;
        [SerializeField] private bool fullBlink;
        [SerializeField] private bool hintPassThrough;
        private PlayerMoveController player;
        private MaterialPropertyBlock hintBlock;
        private bool playerNearby;
        private float pulseStarted;

        public void ConfigurePassThroughHint(bool enabled)
        {
            hintPassThrough = enabled;
            if (!enabled)
            {
                ClearPassThroughHint();
            }
        }

        private void Update()
        {
            if (!hintPassThrough || targetRenderers == null)
            {
                return;
            }

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerMoveController>();
            }

            var settings = ConfigSystem.Instance.Config.TemporalHints;
            bool nearby = false;
            if (player != null)
            {
                foreach (var renderer in targetRenderers)
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    if (renderer.bounds.SqrDistance(player.transform.position) <= settings.ApproachDistance * settings.ApproachDistance)
                    {
                        nearby = true;
                        break;
                    }
                }
            }
            if (!nearby && !playerNearby)
            {
                return;
            }
            if (nearby && !playerNearby)
            {
                pulseStarted = Time.time;
            }

            playerNearby = nearby;
            float pulse = nearby ? .5f + .5f * Mathf.Cos((Time.time - pulseStarted) * Mathf.PI * 2f / settings.PulseSeconds) : 0f;
            foreach (var renderer in targetRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(hintBlock);
                hintBlock.SetFloat(PassThroughId, pulse);
                renderer.SetPropertyBlock(hintBlock);
                hintBlock.Clear();
            }
        }

        private void OnDisable()
        {
            ClearPassThroughHint();
        }

        private void ClearPassThroughHint()
        {
            playerNearby = false;
            if (targetRenderers == null || hintBlock == null)
            {
                return;
            }

            foreach (var renderer in targetRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(hintBlock);
                hintBlock.SetFloat(PassThroughId, 0f);
                renderer.SetPropertyBlock(hintBlock);
                hintBlock.Clear();
            }
        }

        public void Configure(Renderer[] renderers, float intervalSeconds, float burstSeconds,
            float offsetMetres, float phaseSeconds, bool blinkEntireModel)
        {
            targetRenderers = renderers;
            interval = Mathf.Max(0.1f, intervalSeconds);
            duration = Mathf.Clamp(burstSeconds, 0f, interval);
            offset = Mathf.Clamp(offsetMetres, 0f, 0.2f);
            phase = phaseSeconds;
            fullBlink = blinkEntireModel;
            Apply();
        }

        private void OnEnable()
        {
            hintBlock = new MaterialPropertyBlock();
            Apply();
            ClearPassThroughHint();
        }

        private void OnValidate()
        {
            Apply();
            if (!hintPassThrough)
            {
                ClearPassThroughHint();
            }
        }

        private void Apply()
        {
            if (targetRenderers == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            foreach (var renderer in targetRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                block.SetFloat(PhaseId, phase);
                block.SetFloat(IntervalId, Mathf.Max(0.1f, interval));
                block.SetFloat(DurationId, Mathf.Clamp(duration, 0f, interval));
                block.SetFloat(OffsetId, offset);
                block.SetFloat(FullBlinkId, fullBlink ? 1f : 0f);
                renderer.SetPropertyBlock(block);
                block.Clear();
            }
        }
    }

}
