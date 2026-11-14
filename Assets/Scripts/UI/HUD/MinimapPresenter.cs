using System.Collections.Generic;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class MinimapPresenter : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private Merchant questSource;
        [SerializeField] private TutorialPresenter screens;
        [SerializeField] private CampusMapData mapData;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private RectTransform playerIcon;
        [SerializeField] private RectTransform taskIcon;
        [SerializeField] private RectTransform taskArrow;
        [SerializeField] private EnemySystem enemySystem;
        [SerializeField] private RectTransform enemyIconTemplate;
        [SerializeField] private RectTransform shopIcon;
        private float worldRadius => ConfigSystem.Instance.Config.UI.MinimapWorldRadius;
        private float refreshRate => ConfigSystem.Instance.Config.UI.MinimapRefreshRate;
        private CampusBoundaryReturn campusBoundary;
        private GameTimeController gameTime;
        private MapView minimapView;
        private MapView fullMapView;
        private Vector3 mapCenter;
        private Vector2 mapHalfExtents;
        private float nextRefresh;

        // Both views use the same marker sources, with their own UI objects and enemy pool.
        private sealed class MapView
        {
            public RawImage Image;
            public RectTransform Player, Task, Arrow, EnemyTemplate, Shop;
            public readonly List<RectTransform> Enemies = new();
        }

        public bool IsVisible => visibility != null && visibility.alpha > 0f;
        public bool CanShowFullMap => isActiveAndEnabled && minimapView != null && player != null
            && campusBoundary != null && campusBoundary.TryGetPlayableBounds(out _);

        private void Awake()
        {
            if (gameObject.scene.name != "StartScene")
            {
                enabled = false;
                return;
            }
            if (player == null)
            {
                var input = FindFirstObjectByType<PlayerInputReader>();
                if (input != null) player = input.transform;
            }
            if (questSource == null) questSource = FindFirstObjectByType<Merchant>();
            if (screens == null) screens = FindFirstObjectByType<TutorialPresenter>();
            if (enemySystem == null) enemySystem = FindFirstObjectByType<EnemySystem>();
            campusBoundary = FindFirstObjectByType<CampusBoundaryReturn>();
            gameTime = FindFirstObjectByType<GameTimeController>();
            if (mapData == null || !mapData.IsValid || mapImage == null || visibility == null
                || playerIcon == null || taskIcon == null || taskArrow == null
                || enemyIconTemplate == null || shopIcon == null)
            {
                Debug.LogError("Minimap is missing its baked campus map or UI references.", this);
                enabled = false;
                return;
            }

            var rim = transform.Find("Copper rim") as RectTransform;
            if (rim != null) ArchiveUi.Surface(rim, "map-ring");
            var north = transform.Find("North") as RectTransform;
            if (north != null) north.anchoredPosition = new Vector2(0, 12);
            if (north != null) ArchiveUi.ApplyFont(north.GetComponentInChildren<TMP_Text>(), ArchiveUi.FontRole.Technical);
            mapImage.texture = mapData.Texture;
            visibility.interactable = false;
            visibility.blocksRaycasts = false;
            visibility.alpha = 0f;
            minimapView = new MapView { Image = mapImage, Player = playerIcon, Task = taskIcon,
                Arrow = taskArrow, EnemyTemplate = enemyIconTemplate, Shop = shopIcon };
            var hint = ArchiveUi.Text(transform, "M · Map    Esc · Menu", new Vector2(0f, -306f), new Vector2(300f, 30f),
                20f, TextAlignmentOptions.Center);
            hint.color = ArchiveUi.Paper;
        }

        private void OnEnable() => nextRefresh = 0f;

        private void LateUpdate()
        {
            if (fullMapView != null && screens != null && screens.IsMapShowing)
            {
                visibility.alpha = 0f;
                if (Time.unscaledTime >= nextRefresh) RefreshMap(fullMapView, true);
                return;
            }
            bool visible = player != null && mapData != null && mapData.IsValid && screens != null && !screens.IsShowing
                && (gameTime == null || !gameTime.IsQuestOverviewPaused)
                && SceneManager.GetActiveScene().name == "StartScene";
            visibility.alpha = visible ? 1f : 0f;
            if (!visible) return;

            playerIcon.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
            if (Time.unscaledTime < nextRefresh) return;
            RefreshMap(minimapView, false);
        }

        private void RefreshMap(MapView view, bool fullMap)
        {
            nextRefresh = Time.unscaledTime + 1f / refreshRate;
            Vector3 center = fullMap ? mapCenter : player.position;
            Vector2 halfExtents = fullMap ? mapHalfExtents : Vector2.one * worldRadius;
            view.Image.uvRect = mapData.ViewUv(center, halfExtents);
            view.Player.anchoredPosition = fullMap ? ProjectMarker(view, player.position, 12f, out _) : Vector2.zero;
            view.Player.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
            RefreshEnemies(view);
            RefreshShop(view);
            RefreshTask(view);
        }

        private void RefreshEnemies(MapView view)
        {
            int visibleCount = 0;
            if (enemySystem != null)
            {
                foreach (var enemy in enemySystem.OrderedTargets)
                {
                    if (enemy == null || !enemy.IsAlive || !enemy.gameObject.activeInHierarchy) continue;
                    Vector2 position = ProjectMarker(view, enemy.transform.position, 10f, out bool outside);
                    if (outside) continue;
                    if (visibleCount == view.Enemies.Count)
                    {
                        var icon = Instantiate(view.EnemyTemplate, view.EnemyTemplate.parent);
                        icon.name = "Enemy marker";
                        icon.SetSiblingIndex(1);
                        view.Enemies.Add(icon);
                    }
                    var marker = view.Enemies[visibleCount++];
                    marker.anchoredPosition = position;
                    marker.gameObject.SetActive(true);
                }
            }
            for (int i = visibleCount; i < view.Enemies.Count; i++) view.Enemies[i].gameObject.SetActive(false);
        }

        private void RefreshShop(MapView view)
        {
            view.Shop.gameObject.SetActive(false);
            if (questSource == null || !questSource.gameObject.activeInHierarchy) return;
            Vector2 position = ProjectMarker(view, questSource.transform.position, 16f, out bool outside);
            view.Shop.anchoredPosition = position;
            view.Shop.gameObject.SetActive(!outside);
        }

        private void RefreshTask(MapView view)
        {
            bool hasTask = questSource != null && questSource.IsTaskGuidanceVisible
                && questSource.CurrentTaskTarget.HasValue;
            view.Task.gameObject.SetActive(false);
            view.Arrow.gameObject.SetActive(false);
            if (!hasTask) return;

            float inset = view.Arrow.rect.width * 0.5f + 4f;
            Vector2 position = ProjectMarker(view, questSource.CurrentTaskTarget.Value, inset, out bool atEdge);
            var marker = atEdge ? view.Arrow : view.Task;
            if (!atEdge && view.Shop.gameObject.activeSelf && (position - view.Shop.anchoredPosition).sqrMagnitude < 1f)
            {
                position += new Vector2(14f, 14f);
                position = view == fullMapView ? ClampToRect(position, view.Image.rectTransform.rect.size * 0.5f, inset)
                    : Vector2.ClampMagnitude(position, view.Image.rectTransform.rect.width * 0.5f - inset);
            }
            marker.gameObject.SetActive(true);
            marker.anchoredPosition = position;
            view.Arrow.localRotation = Quaternion.Euler(0f, 0f,
                -Mathf.Atan2(position.x, position.y) * Mathf.Rad2Deg);
        }

        private Vector2 ProjectMarker(MapView view, Vector3 target, float inset, out bool outside)
        {
            if (view == fullMapView)
                return ProjectFullMapTarget(mapCenter, target, mapHalfExtents,
                    view.Image.rectTransform.rect.size * 0.5f, inset, out outside);
            return ProjectTarget(player.position, target, worldRadius,
                view.Image.rectTransform.rect.width * 0.5f, inset, out outside);
        }

        public void ShowFullMap(Transform parent)
        {
            if (!CanShowFullMap) return;
            CloseFullMap();
            campusBoundary.TryGetPlayableBounds(out Bounds bounds);
            float aspect = bounds.size.x / bounds.size.z;
            Vector2 viewportSize = new Vector2(Mathf.Min(1444f, 708f * aspect), Mathf.Min(708f, 1444f / aspect));
            var viewport = ArchiveUi.Rect("Campus map viewport", parent, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -112f), viewportSize);
            viewport.gameObject.AddComponent<RectMask2D>();
            var image = viewport.gameObject.AddComponent<RawImage>();
            image.texture = mapData.Texture;
            image.raycastTarget = false;
            fullMapView = new MapView { Image = image,
                Player = CloneMarker(playerIcon, viewport), Task = CloneMarker(taskIcon, viewport),
                Arrow = CloneMarker(taskArrow, viewport), EnemyTemplate = CloneMarker(enemyIconTemplate, viewport),
                Shop = CloneMarker(shopIcon, viewport) };
            fullMapView.Player.gameObject.SetActive(true);
            fullMapView.EnemyTemplate.gameObject.SetActive(false);
            mapCenter = bounds.center;
            mapHalfExtents = FitFullMap(bounds, viewport.rect.width / viewport.rect.height,
                ConfigSystem.Instance.Config.UI.FullMapPadding);
            visibility.alpha = 0f;
            RefreshMap(fullMapView, true);
        }

        private static RectTransform CloneMarker(RectTransform source, Transform parent)
        {
            var marker = Instantiate(source, parent);
            marker.name = source.name;
            marker.anchoredPosition = Vector2.zero;
            marker.localRotation = Quaternion.identity;
            return marker;
        }

        public void BuildMapLegend(Transform parent)
        {
            RectTransform[] icons = { playerIcon, taskIcon, enemyIconTemplate, shopIcon };
            string[] labels = { "You", "Current task", "Enemy", "Merchant" };
            for (int i = 0; i < icons.Length; i++)
            {
                var marker = CloneMarker(icons[i], parent);
                ArchiveUi.Place(marker, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(64f + i * 230f, -876f), new Vector2(24f, 24f));
                marker.gameObject.SetActive(true);
                ArchiveUi.Text(parent, labels[i], new Vector2(86f + i * 230f, -858f), new Vector2(190f, 36f), 22f);
            }
        }

        public void CloseFullMap()
        {
            if (fullMapView == null) return;
            if (fullMapView.Image != null)
            {
                fullMapView.Image.gameObject.SetActive(false);
                Destroy(fullMapView.Image.gameObject);
            }
            fullMapView = null;
            nextRefresh = 0f;
        }

        public static Vector2 FitFullMap(Bounds bounds, float aspect, float padding)
        {
            float size = Mathf.Max(bounds.extents.z, bounds.extents.x / aspect) / (1f - 2f * padding);
            return new Vector2(size * aspect, size);
        }

        public static Vector2 ProjectFullMapTarget(Vector3 origin, Vector3 target, Vector2 worldHalfExtents,
            Vector2 pixelHalfExtents, float inset, out bool outside)
        {
            Vector3 delta = target - origin;
            Vector2 position = new Vector2(delta.x / worldHalfExtents.x * pixelHalfExtents.x,
                delta.z / worldHalfExtents.y * pixelHalfExtents.y);
            outside = Mathf.Abs(position.x) > pixelHalfExtents.x || Mathf.Abs(position.y) > pixelHalfExtents.y;
            return ClampToRect(position, pixelHalfExtents, inset);
        }

        private static Vector2 ClampToRect(Vector2 position, Vector2 halfExtents, float inset)
        {
            Vector2 limit = Vector2.Max(Vector2.zero, halfExtents - Vector2.one * inset);
            return new Vector2(Mathf.Clamp(position.x, -limit.x, limit.x), Mathf.Clamp(position.y, -limit.y, limit.y));
        }

        // Ignore height differences; keep the entire icon inside the circular mask.
        public static Vector2 ProjectTarget(Vector3 origin, Vector3 target, float worldRadius,
            float pixelRadius, float inset, out bool atEdge)
        {
            Vector3 delta = target - origin;
            Vector2 position = new Vector2(delta.x, delta.z) * (pixelRadius / worldRadius);
            atEdge = position.sqrMagnitude > pixelRadius * pixelRadius;
            return Vector2.ClampMagnitude(position, Mathf.Max(0f, pixelRadius - inset));
        }

        private void OnDisable()
        {
            CloseFullMap();
            if (visibility != null) visibility.alpha = 0f;
        }
    }
}
