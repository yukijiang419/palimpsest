using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    // batch cores, fragments, trails and bar responses in one pooled mesh
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MazeOutcomeParticleGraphic : MaskableGraphic
    {
        internal MazeOutcomeParticleLayer Layer;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            maskable = false;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Layer != null)
            {
                Layer.Draw(mesh);
            }
        }
    }
}
