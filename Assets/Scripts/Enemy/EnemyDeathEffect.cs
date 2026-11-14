using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Caffeine.Gameplay.Enemy
{
    [DisallowMultipleComponent]
    public sealed class EnemyDeathEffect : MonoBehaviour
    {
        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int MinYId = Shader.PropertyToID("_MinY");
        private static readonly int MaxYId = Shader.PropertyToID("_MaxY");

        [SerializeField, Min(0.1f)] private float duration = 1.25f;
        [SerializeField, Min(0f)] private float ragdollBeforeDissolve = 0.45f;
        [SerializeField] private Color edgeColor = new(0.1f, 0.9f, 1f, 1f);

        private readonly List<Material> deathMaterials = new();
        private bool isPlaying;

        public void Play(Action completed)
        {
            if (isPlaying)
            {
                return;
            }

            isPlaying = true;
            Renderer[] renderers = EnemyHitFeedback.CharacterRenderers(this);
            DisableNonCharacterVisuals(renderers);

            StartCoroutine(Animate(completed));
        }

        private bool PrepareMaterials(Renderer[] renderers)
        {
            Shader deathShader = Shader.Find("Caffeine/Enemy Death Dissolve");
            if (deathShader == null)
            {
                Debug.LogWarning("Enemy death shader was not found; skipping the dissolve effect.", this);
                return false;
            }

            if (renderers.Length == 0)
            {
                return false;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            float minY = bounds.min.y;
            float maxY = bounds.max.y;
            foreach (Renderer target in renderers)
            {
                Material[] sourceMaterials = target.materials;
                Material[] replacements = new Material[sourceMaterials.Length];
                for (int index = 0; index < sourceMaterials.Length; index++)
                {
                    Material source = sourceMaterials[index];
                    Material replacement = new(deathShader)
                    {
                        name = $"{source.name} (Death)"
                    };

                    if (source.HasProperty(MainTextureId))
                    {
                        replacement.SetTexture(MainTextureId, source.GetTexture(MainTextureId));
                    }

                    if (source.HasProperty(ColorId))
                    {
                        replacement.SetColor(ColorId, source.GetColor(ColorId));
                    }

                    replacement.SetColor(EdgeColorId, edgeColor);
                    replacement.SetFloat(MinYId, minY);
                    replacement.SetFloat(MaxYId, maxY);
                    replacement.SetFloat(DissolveId, 0f);
                    replacements[index] = replacement;
                    deathMaterials.Add(replacement);
                }

                target.materials = replacements;
            }

            return deathMaterials.Count > 0;
        }

        private IEnumerator Animate(Action completed)
        {
            float fallElapsed = 0f;
            while (fallElapsed < ragdollBeforeDissolve)
            {
                fallElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!PrepareMaterials(EnemyHitFeedback.CharacterRenderers(this)))
            {
                completed?.Invoke();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float dissolve = Mathf.SmoothStep(0f, 1.35f, progress);
                foreach (Material material in deathMaterials)
                {
                    material.SetFloat(DissolveId, dissolve);
                }

                yield return null;
            }

            completed?.Invoke();
        }

        private void DisableNonCharacterVisuals(Renderer[] character)
        {
            foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = false;
            }

            foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (Array.IndexOf(character, meshRenderer) >= 0)
                {
                    continue;
                }

                meshRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in deathMaterials)
            {
                Destroy(material);
            }
        }
    }
}
