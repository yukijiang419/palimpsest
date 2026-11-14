using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay
{
    [DisallowMultipleComponent]
    // dealing the game pause logic and the timescale
    public sealed class GameTimeController : MonoBehaviour
    {
        private float mazeTimeScale;
        private float hitStopEndsAt;
        private bool mazeActive;
        private bool tutorialPaused;
        private bool shopPaused;
        private bool boundaryTransitionPaused;
        private bool questOverviewPaused;
        private int shopClosedFrame = -1;
        private int mazeClosedFrame = -1;
        public bool IsPaused => tutorialPaused || shopPaused || boundaryTransitionPaused || questOverviewPaused;
        public bool IsShopPaused => shopPaused;
        public bool IsQuestOverviewPaused => questOverviewPaused;
        public bool BlocksPauseMenu => shopPaused || boundaryTransitionPaused || questOverviewPaused || mazeActive
            || shopClosedFrame == Time.frameCount || mazeClosedFrame == Time.frameCount;

        private void Start()
        {
            mazeTimeScale = ConfigSystem.Instance.Config.Simulation.MazeTimeScale;
        }

        private void Update()
        {
            if (hitStopEndsAt > 0f && Time.unscaledTime >= hitStopEndsAt)
            {
                hitStopEndsAt = 0f;
                ApplyTimeScale();
            }
        }

        public void SetMazeActive(bool active)
        {
            if (mazeActive && !active) mazeClosedFrame = Time.frameCount;
            mazeActive = active;
            ApplyTimeScale();
        }

        public void SetTutorialPaused(bool paused)
        {
            tutorialPaused = paused;
            ApplyTimeScale();
        }

        public void SetShopPaused(bool paused)
        {
            if (shopPaused && !paused)
            {
                shopClosedFrame = Time.frameCount;
            }

            shopPaused = paused;
            ApplyTimeScale();
        }

        public void SetBoundaryTransitionPaused(bool paused)
        {
            boundaryTransitionPaused = paused;
            ApplyTimeScale();
        }

        public void SetQuestOverviewPaused(bool paused)
        {
            questOverviewPaused = paused;
            ApplyTimeScale();
        }

        public void RequestHitStop(float duration)
        {
            float requestedEnd = Time.unscaledTime + duration;
            if (requestedEnd > hitStopEndsAt)
            {
                hitStopEndsAt = requestedEnd;
            }

            ApplyTimeScale();
        }

        private void OnDisable()
        {
            hitStopEndsAt = 0f;
            mazeActive = false;
            tutorialPaused = false;
            shopPaused = false;
            boundaryTransitionPaused = false;
            questOverviewPaused = false;
            Time.timeScale = 1f;
        }

        private void ApplyTimeScale()
        {
            Time.timeScale = SelectTimeScale(
                hitStopEndsAt > Time.unscaledTime,
                mazeActive,
                mazeTimeScale,
                IsPaused);
        }

        internal static float SelectTimeScale(bool hitStopActive, bool isMazeActive, float slowScale, bool isTutorialPaused = false)
        {
            if (isTutorialPaused || hitStopActive)
            {
                return 0f;
            }

            return isMazeActive ? slowScale : 1f;
        }
    }
}
