using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    /// <summary>Reveals and dissolves a transient sword ribbon without changing hit geometry.</summary>
    public sealed class CombatSlashVisual : MonoBehaviour
    {
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock properties;
        private Color tint;
        private float duration;
        private float elapsed;
        private Color[] sourceColors;
        private Color[] animatedColors;
        private Vector2[] coordinates;
        private bool directional;
        private float finalScale;

        public void Initialize(Mesh shape, MeshRenderer target, Color color, float seconds, bool sweep = false, float endScale = 1.06f)
        {
            mesh = shape;
            meshRenderer = target;
            tint = color;
            duration = Mathf.Max(0.01f, seconds);
            directional = sweep;
            finalScale = endScale;
            if (directional)
            {
                sourceColors = mesh.colors;
                animatedColors = new Color[sourceColors.Length];
                coordinates = mesh.uv;
                mesh.MarkDynamic();
            }
            properties = new MaterialPropertyBlock();
            AnimateRibbon(0f);
            Apply(1f);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float phase = Mathf.Clamp01(elapsed / duration);
            // Normal cuts stay close to contact; spatial cuts can collapse inward.
            transform.localScale = Vector3.one * Mathf.Lerp(1f, finalScale, phase);
            AnimateRibbon(phase);
            Apply((1f - phase) * (1f - phase));
            if (phase >= 1f) Destroy(gameObject);
        }

        private void AnimateRibbon(float phase)
        {
            if (!directional) return;
            float head = Mathf.Lerp(0.45f, 1.12f, Mathf.Clamp01(phase / 0.3f));
            float tail = Mathf.Lerp(-0.2f, 1f, phase * phase);
            for (int i = 0; i < animatedColors.Length; i++)
            {
                float t = coordinates[i].x;
                float reveal = Mathf.SmoothStep(0f, 1f, (head - t) / 0.14f);
                float erase = Mathf.SmoothStep(0f, 1f, (t - tail) / 0.24f);
                animatedColors[i] = sourceColors[i];
                animatedColors[i].a *= reveal * erase;
            }
            mesh.colors = animatedColors;
        }

        private void Apply(float fade)
        {
            properties.SetColor("_Tint", tint);
            properties.SetFloat("_Fade", fade);
            meshRenderer.SetPropertyBlock(properties);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
