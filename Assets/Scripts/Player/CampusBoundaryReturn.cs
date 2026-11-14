using System.Collections;
using Caffeine.Gameplay.CameraSystem;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.UI;
using Caffeine.Gameplay.Config;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Caffeine.Gameplay.Player
{
    // Return safeguard behind the visible, solid campus perimeter.
    [DisallowMultipleComponent]
    public sealed class CampusBoundaryReturn : MonoBehaviour
    {
        private GameplayConfig.BoundarySettings Settings => ConfigSystem.Instance.Config.Boundary;

        [SerializeField] private MeshCollider campusGround;
        [SerializeField] private Transform birthPoint;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerMoveController motor;
        [SerializeField] private PlayerAttackController attack;
        [SerializeField] private ThirdPersonCameraController cameraController;
        [SerializeField] private GameTimeController gameTime;
        [SerializeField] private TutorialPresenter screens;
        [SerializeField] private HealthShield health;
        [SerializeField] private MazeSystem maze;

        private Canvas fadeCanvas;
        private Image fadeImage;
        private Coroutine transition;
        private bool ownsLocks;
        public bool TryGetPlayableBounds(out Bounds bounds)
        {
            return Settings.TryGetPlayableBounds(out bounds);
        }

        private void Update()
        {
            if (transition != null || SceneManager.GetActiveScene().name != "StartScene" ||
                birthPoint == null || input == null || motor == null ||
                gameTime == null || screens == null || health == null || !health.IsInitialized ||
                health.IsDead || screens.CurrentScreen != "Playing" || gameTime.IsPaused)
                return;

            if (!Settings.ContainsPosition(input.transform.position, Settings.ReturnOutsideMargin))
                transition = StartCoroutine(ReturnToBirthPoint());
        }

        private IEnumerator ReturnToBirthPoint()
        {
            // Lock before the first yield, so a slide cannot advance another frame.
            input.SetBoundaryTransitionBlocked(true);
            motor.StopMoving();
            maze?.CancelSession();
            attack?.SetAttackBlocked(true);
            gameTime.SetBoundaryTransitionPaused(true);
            ownsLocks = true;
            EnsureFadeCanvas();
            fadeCanvas.gameObject.SetActive(true);

            yield return Fade(0f, 1f, Settings.FadeOutSeconds);
            motor.TeleportTo(birthPoint.position, birthPoint.rotation);
            cameraController?.ResetAfterTeleport(birthPoint.rotation);
            yield return new WaitForSecondsRealtime(Settings.HoldSeconds);
            yield return Fade(1f, 0f, Settings.FadeInSeconds);

            fadeCanvas.gameObject.SetActive(false);
            ReleaseLocks();
            transition = null;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, elapsed / seconds));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            fadeImage.color = new Color(0f, 0f, 0f, to);
        }

        private void EnsureFadeCanvas()
        {
            if (fadeCanvas != null) return;
            var root = new GameObject("Campus Boundary Fade", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            fadeCanvas = root.GetComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fadeCanvas.overrideSorting = true;
            fadeCanvas.sortingOrder = 200;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var rect = ArchiveUi.Stretch("Blackout", root.transform);
            fadeImage = ArchiveUi.Paint(rect, new Color(0, 0, 0, 0), true);
        }

        private void ReleaseLocks()
        {
            if (!ownsLocks) return;
            ownsLocks = false;
            if (attack != null && health != null && !health.IsDead) attack.SetAttackBlocked(false);
            if (gameTime != null) gameTime.SetBoundaryTransitionPaused(false);
            if (input != null) input.SetBoundaryTransitionBlocked(false);
        }

        private void OnDisable()
        {
            if (transition != null) StopCoroutine(transition);
            transition = null;
            if (fadeCanvas != null) fadeCanvas.gameObject.SetActive(false);
            ReleaseLocks();
        }

        private void OnDrawGizmosSelected()
        {
            var config = FindFirstObjectByType<ConfigSystem>()?.Config;
            if (config == null || config.Boundary.PlayableOutline == null) return;
            Vector2[] outline = config.Boundary.PlayableOutline;
            float groundY = campusGround != null ? campusGround.bounds.center.y : 0f;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 a = new(outline[i].x, groundY + .1f, outline[i].y);
                Vector2 next = outline[(i + 1) % outline.Length];
                Vector3 b = new(next.x, groundY + .1f, next.y);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a + Vector3.up * config.Boundary.BlockerHeight,
                    b + Vector3.up * config.Boundary.BlockerHeight);
                Gizmos.DrawLine(a, a + Vector3.up * config.Boundary.BlockerHeight);
            }
        }
    }
}
