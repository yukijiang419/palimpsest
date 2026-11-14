using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Player;
using UnityEngine;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class TargetLockPresenter : MonoBehaviour
    {
        [SerializeField] private RectTransform marker;
        private PlayerTargetLock targetLock;
        private Canvas canvas;
        private Transform displayedTarget;
        private EnemyHealthBar enemyBar;
        private BossEncounterPresenter bossBar;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            marker.gameObject.SetActive(false);
        }

        private void Start() => targetLock = FindFirstObjectByType<PlayerTargetLock>();

        private void LateUpdate()
        {
            bool visible = targetLock != null && targetLock.IsLocked && targetLock.IsTargetVisible
                && !targetLock.IsTrackingPaused;
            Transform target = visible ? targetLock.LockedTransform : null;
            if (displayedTarget != target)
            {
                displayedTarget = target;
                enemyBar = target != null ? target.GetComponentInChildren<EnemyHealthBar>() : null;
                bossBar = target != null ? target.GetComponent<BossEncounterPresenter>() : null;
            }
            Vector2 point = default;
            if (visible)
            {
                var boss = target.GetComponent<BossController>();
                visible = enemyBar != null ? enemyBar.TryGetLockAnchorScreenPoint(out point)
                    : bossBar != null && boss != null && bossBar.BossHealth == boss.Health
                      && bossBar.TryGetLockAnchorScreenPoint(out point);
                visible &= point.x >= 0f && point.x <= Screen.width && point.y >= 0f && point.y <= Screen.height;
            }
            marker.gameObject.SetActive(visible);
            if (!visible) return;
            var parent = (RectTransform)marker.parent;
            UnityEngine.Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, point, uiCamera, out Vector2 position))
                // Bottom-center pivot is the tip; keep the six-unit gap constant under Canvas scaling.
                marker.anchoredPosition = position + Vector2.up * 6f;
        }

        private void OnDisable()
        {
            if (marker != null) marker.gameObject.SetActive(false);
        }
    }
}
