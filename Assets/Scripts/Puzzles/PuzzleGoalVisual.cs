using UnityEngine;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Rendering;

namespace Caffeine.Gameplay.Puzzles
{
    public sealed class PuzzleGoalVisual : MonoBehaviour
    {
        public GameObject emptyCore;
        public GameObject filledCore;
        public Renderer[] glowRenderers;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const float SeatPulseSeconds = .45f;
        private MaterialPropertyBlock properties;
        private bool isSolved;
        private float pulseUntil;

        public void SetSolved(bool solved)
        {
            if (emptyCore != null) emptyCore.SetActive(!solved);
            if (filledCore != null) filledCore.SetActive(solved);
            if (!solved) pulseUntil = 0f;
            isSolved = solved;
            RefreshGlow();
        }

        public void PlaySeat()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            pulseUntil = Time.time + SeatPulseSeconds;
            var settings = ConfigSystem.Instance.Config.Effects;
            Color color = glowRenderers[0].sharedMaterial.color;
            Vector3 position = filledCore.transform.position;
            Bounds bounds = glowRenderers[0].bounds;
            foreach (var surface in glowRenderers)
            {
                bounds.Encapsulate(surface.bounds);
            }
            // Emit at the exposed footprint corners, outside the seated block.
            float height = bounds.min.y + .1f;
            Vector3[] corners =
            {
                new(bounds.min.x - .06f, height, bounds.min.z - .06f),
                new(bounds.max.x + .06f, height, bounds.min.z - .06f),
                new(bounds.min.x - .06f, height, bounds.max.z + .06f),
                new(bounds.max.x + .06f, height, bounds.max.z + .06f)
            };
            int dustCount = settings.SeatParticles / 2;
            ArchiveEffectBurst.Play(transform, position, new Color(.65f, .59f, .48f),
                dustCount, settings.SeatSeconds, .1f, false, .25f, corners);
            ArchiveEffectBurst.Play(transform, position, color,
                settings.SeatParticles - dustCount, settings.SeatSeconds, .1f, true, .4f, corners);
        }

        private void Update()
        {
            if (pulseUntil <= 0f) return;
            RefreshGlow();
            if (Time.time >= pulseUntil) pulseUntil = 0f;
        }

        private void OnEnable() => RefreshGlow();

        private void RefreshGlow()
        {
            if (glowRenderers == null) return;
            properties ??= new MaterialPropertyBlock();
            float pulse = Application.isPlaying
                ? Mathf.Clamp01((pulseUntil - Time.time) / SeatPulseSeconds) : 0f;
            float intensity = (isSolved ? .45f : .18f) + pulse * .65f;
            foreach (var surface in glowRenderers)
            {
                if (surface == null) continue;
                surface.GetPropertyBlock(properties);
                properties.SetColor(EmissionColor, surface.sharedMaterial.color * intensity);
                surface.SetPropertyBlock(properties);
            }
        }
    }
}
