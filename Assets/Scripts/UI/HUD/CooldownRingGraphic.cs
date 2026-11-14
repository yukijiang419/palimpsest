using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CooldownRingGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float progress = 1f;
        [SerializeField, Min(0f)] private float thickness = 3f;
        [SerializeField] private Color trackColor = new(0.17f, 0.18f, 0.18f, 0.95f);
        private const int Segments = 64;

        public float Progress
        {
            get => progress;
            set
            {
                float next = Mathf.Clamp01(value);
                if (progress == next)
                {
                    return;
                }

                progress = next;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (outer <= 0f)
            {
                return;
            }

            float inner = Mathf.Max(0f, outer - thickness);
            AddArc(vh, rect.center, inner, outer, 1f, trackColor);
            AddArc(vh, rect.center, inner, outer, progress, color);
        }

        private static void AddArc(VertexHelper vh, Vector2 center, float inner,
            float outer, float amount, Color tint)
        {
            int count = Mathf.CeilToInt(amount * Segments);
            for (int segment = 0; segment < count; segment++)
            {
                float a = segment / (float)Segments * Mathf.PI * 2f;
                float b = Mathf.Min((segment + 1f) / Segments, amount) * Mathf.PI * 2f;
                Vector2 from = new(Mathf.Sin(a), Mathf.Cos(a));
                Vector2 to = new(Mathf.Sin(b), Mathf.Cos(b));
                int index = vh.currentVertCount;
                vh.AddVert(center + from * outer, tint, Vector2.zero);
                vh.AddVert(center + to * outer, tint, Vector2.zero);
                vh.AddVert(center + to * inner, tint, Vector2.zero);
                vh.AddVert(center + from * inner, tint, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
