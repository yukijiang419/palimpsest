using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    // A small vector silhouette stays sharp without adding texture assets to the WebGL build.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WeaponSilhouetteGraphic : MaskableGraphic
    {
        public bool TwoHanded { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Vector2 P(float x, float y) => new(r.x + x * r.width, r.y + y * r.height);
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                int i = vh.currentVertCount;
                vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero);
                vh.AddVert(c, color, Vector2.zero); vh.AddVert(d, color, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            }
            float thickness = TwoHanded ? 0.11f : 0.065f;
            Quad(P(0.25f, 0.45f), P(0.82f, 0.65f + thickness), P(0.96f, 0.75f), P(0.80f, 0.65f - thickness));
            Quad(P(0.23f, 0.22f), P(0.26f, 0.23f), P(0.32f, 0.72f), P(0.29f, 0.73f));
            Quad(P(0.10f, 0.37f), P(0.27f, 0.44f), P(0.28f, 0.51f), P(0.09f, 0.44f));
        }
    }
}
