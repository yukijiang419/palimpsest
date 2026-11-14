using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Enemy;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        [SerializeField] private HealthShield healthShield;
        [SerializeField] private Image healthFill;
        [SerializeField] private Image shieldFill;
        [SerializeField] private Transform healthFace;
        [SerializeField] private EnemyController enemy;
        [SerializeField] private UI.CooldownRingGraphic hackCooldownRing;

        private Transform cameraTransform;
        private float barWidth;

        public bool TryGetLockAnchorScreenPoint(out Vector2 point)
        {
            point = default;
            if (!TryGetEffectScreenRect(false, out Rect health)
                || !TryGetEffectScreenRect(true, out Rect shield)) return false;
            // The authored tracks include a three-unit border, independent of the remaining fill.
            var fill = shieldFill.rectTransform;
            Vector3 top = Camera.main.WorldToScreenPoint(fill.TransformPoint(new Vector3(barWidth * 0.5f, 7f, 0f)));
            point = new Vector2(health.center.x, Mathf.Max(top.y, Mathf.Max(health.yMax, shield.yMax)));
            return true;
        }

        // keep full-width anchors valid when the shield fill becomes empty
        public bool TryGetEffectScreenRect(bool shield, out Rect screenRect)
        {
            screenRect = default;
            if (!isActiveAndEnabled || !healthFace.gameObject.activeInHierarchy
                || enemy == null || !enemy.IsAlive || Camera.main == null)
            {
                return false;
            }

            RectTransform fill = (shield ? shieldFill : healthFill).rectTransform;
            Camera camera = Camera.main;
            Vector3 left = camera.WorldToScreenPoint(fill.TransformPoint(Vector3.zero));
            Vector3 right = camera.WorldToScreenPoint(fill.TransformPoint(new Vector3(barWidth, 0, 0)));
            Vector3 top = camera.WorldToScreenPoint(fill.TransformPoint(new Vector3(barWidth * 0.5f, 4, 0)));
            Vector2 center = ((Vector2)left + (Vector2)right) * 0.5f;
            if (left.z <= 0f || right.z <= 0f || center.x < 0 || center.x > Screen.width
                || center.y < 0 || center.y > Screen.height)
            {
                return false;
            }

            float height = Mathf.Max(4f, Mathf.Abs(top.y - center.y) * 2f);
            screenRect = new Rect(Mathf.Min(left.x, right.x), center.y - height * 0.5f,
                Mathf.Abs(right.x - left.x), height);
            return true;
        }

        private void Awake()
        {
            cameraTransform = Camera.main != null ? Camera.main.transform : null;
            healthFill.color = UI.ArchiveUi.Red;
            shieldFill.color = UI.ArchiveUi.Shield;
            healthFill.raycastTarget = shieldFill.raycastTarget = false;
            barWidth = ((RectTransform)healthFill.transform.parent).rect.width;
            Configure(healthFill, -8);
            Configure(shieldFill, 8);
            AddTrack(healthFill, "health-track", -8);
            AddTrack(shieldFill, "stamina-track", 8);
            hackCooldownRing.color = UI.ArchiveUi.Cyan;
            hackCooldownRing.raycastTarget = false;
        }

        private void OnEnable()
        {
            healthShield.VitalsChanged += Refresh;
        }

        private void Start()
        {
            Refresh(healthShield);
        }

        private void OnDisable()
        {
            healthShield.VitalsChanged -= Refresh;
        }

        private void LateUpdate()
        {
            if (cameraTransform != null) healthFace.rotation = cameraTransform.rotation;
            hackCooldownRing.Progress = enemy.MazeCooldownProgress;
        }

        private void Refresh(HealthShield source)
        {
            healthFill.rectTransform.sizeDelta = new Vector2(barWidth * Ratio(source.CurrentHealth, source.MaxHealth), 8);
            shieldFill.rectTransform.sizeDelta = new Vector2(barWidth * Ratio(source.CurrentShield, source.MaxShield), 8);
        }

        private static void Configure(Image image, float y)
        {
            image.type = Image.Type.Simple;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(0, y);
        }

        private void AddTrack(Image fill, string sprite, float y)
        {
            var track = UI.ArchiveUi.Rect("Archive " + sprite, fill.transform.parent, new Vector2(0, .5f),
                new Vector2(0, .5f), new Vector2(-3, y), new Vector2(barWidth + 6, 14));
            UI.ArchiveUi.Surface(track, sprite);
            track.SetAsFirstSibling();
        }

        private static float Ratio(float current, float maximum)
        {
            return maximum > 0f ? current / maximum : 0f;
        }
    }
}
