using UnityEngine;

namespace Caffeine.Gameplay.UI
{
    // One north-up campus image shared by both map views.
    public sealed class CampusMapData : ScriptableObject
    {
        [SerializeField] private Texture2D texture;
        [SerializeField] private Rect worldRect;

        public Texture2D Texture => texture;
        public Rect WorldRect => worldRect;
        public bool IsValid => texture != null && worldRect.width > 0f && worldRect.height > 0f;

        public Rect ViewUv(Vector3 center, Vector2 halfExtents)
        {
            return new Rect(
                (center.x - halfExtents.x - worldRect.xMin) / worldRect.width,
                (center.z - halfExtents.y - worldRect.yMin) / worldRect.height,
                halfExtents.x * 2f / worldRect.width,
                halfExtents.y * 2f / worldRect.height);
        }
    }
}
