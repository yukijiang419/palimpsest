using System;
using Caffeine.Gameplay.Rendering;
using Caffeine.Gameplay.Config;
using Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Caffeine.Gameplay.UI
{
    // Timeline controls shot timing; prefab anchors define camera poses.
    public sealed class OpeningSequenceController : MonoBehaviour
    {
        [Serializable]
        public sealed class Shot
        {
            public Transform start;
            public Transform end;
            public Transform lookAt;
            public float fieldOfView = 55;
            [TextArea(2, 4)] public string caption;
        }
        public PlayableDirector director;
        public CinemachineVirtualCamera cinematicCamera;
        public Shot[] shots;
        public event Action Completed;
        public event Action<string, float, bool> PresentationChanged;
        public bool IsPlaying { get; private set; }
        public int CurrentShotIndex { get; private set; } = -1;
        private CinemachineBrain brain;
        private ICinemachineCamera gameCamera;
        private bool oldIgnoreTime;
        private CinemachineBlendDefinition oldBlend;
        private CinemachineBrain.UpdateMethod oldUpdateMethod;
        private CinemachineBrain.BrainUpdateMethod oldBlendUpdateMethod;
        private TemporalAnomalyClock anomalyClock;
        private bool oldAnomalyTime;
        private Vector3 gamePosition;
        private Quaternion gameRotation;
        private float gameFov;
        private bool finished;
        private bool captured;

        public void Play()
        {
            if (IsPlaying || finished) return;
            var mainCamera = Camera.main;
            if (director == null || !(director.playableAsset is TimelineAsset) || cinematicCamera == null
                || shots == null || shots.Length == 0 || mainCamera == null)
            {
                Debug.LogError("Opening sequence assets are missing. Continuing to the tutorial.", this);
                Finish();
                return;
            }
            foreach (var shot in shots)
                if (shot.start == null || shot.end == null || shot.lookAt == null)
                {
                    Debug.LogError("Opening shot anchors are missing. Continuing to the tutorial.", this);
                    Finish();
                    return;
                }
            bool hasTrack = false;
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks())
                hasTrack |= track is OpeningShotTrack;
            if (!hasTrack || director.playableAsset.duration <= 0 || mainCamera.GetComponent<CinemachineBrain>() == null)
            {
                Debug.LogError("Opening Timeline or Cinemachine brain is missing. Continuing to the tutorial.", this);
                Finish();
                return;
            }
            gamePosition = mainCamera.transform.position;
            gameRotation = mainCamera.transform.rotation;
            gameFov = mainCamera.fieldOfView;
            brain = mainCamera.GetComponent<CinemachineBrain>();
            if (brain != null)
            {
                gameCamera = brain.ActiveVirtualCamera;
                oldIgnoreTime = brain.m_IgnoreTimeScale;
                oldBlend = brain.m_DefaultBlend;
                oldUpdateMethod = brain.m_UpdateMethod;
                oldBlendUpdateMethod = brain.m_BlendUpdateMethod;
                brain.m_IgnoreTimeScale = true;
                brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0);
                // Update the camera while physics is paused.
                brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.LateUpdate;
                brain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.LateUpdate;
            }
            anomalyClock = FindFirstObjectByType<TemporalAnomalyClock>();
            if (anomalyClock != null)
            {
                oldAnomalyTime = anomalyClock.UseUnscaledTime;
                anomalyClock.UseUnscaledTime = true;
            }
            captured = true;
            cinematicCamera.Priority = 100;
            cinematicCamera.gameObject.SetActive(true);
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks())
                if (track is OpeningShotTrack) director.SetGenericBinding(track, this);
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.stopped += OnStopped;
            IsPlaying = true;
            director.Play();
            director.Evaluate();
        }

        public void EvaluateShot(int index, float progress, float duration)
        {
            if (!IsPlaying || index < 0 || index >= shots.Length) return;
            var settings = ConfigSystem.Instance.Config.Story;
            var shot = shots[index];
            CurrentShotIndex = index;
            float t = Mathf.Clamp01(progress);
            float movement = Mathf.SmoothStep(0, 1, t);
            Vector3 position = Vector3.Lerp(shot.start.position, shot.end.position, movement);
            Quaternion rotation = Quaternion.LookRotation(shot.lookAt.position - position);
            float fov = shot.fieldOfView;
            if (index == shots.Length - 1)
            {
                // Blend to the settled gameplay camera before the opening ends.
                if (gameCamera != null && gameCamera.IsValid)
                {
                    gamePosition = gameCamera.State.FinalPosition;
                    gameRotation = gameCamera.State.FinalOrientation;
                    gameFov = gameCamera.State.Lens.FieldOfView;
                }
                float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(settings.BlendStart, settings.BlendEnd, t));
                position = Vector3.Lerp(position, gamePosition, blend);
                rotation = Quaternion.Slerp(rotation, gameRotation, blend);
                fov = Mathf.Lerp(fov, gameFov, blend);
            }
            cinematicCamera.transform.SetPositionAndRotation(position, rotation);
            cinematicCamera.m_Lens.FieldOfView = fov;
            float fadeInSeconds = index == 0 ? settings.FirstFadeInSeconds : settings.FadeInSeconds;
            float fade = 1 - Mathf.SmoothStep(0, 1, t * duration / fadeInSeconds);
            // Fade between shots; keep the final gameplay pose visible.
            if (index < shots.Length - 1)
                fade = Mathf.Max(fade, Mathf.SmoothStep(0, 1, ((t - 1) * duration + settings.FadeOutSeconds) / settings.FadeOutSeconds));
            PresentationChanged?.Invoke(shot.caption, fade, index == 0);
        }

        public void Skip() => Finish();
        private void OnStopped(PlayableDirector source) => Finish();
        private void Finish()
        {
            if (finished) return;
            finished = true;
            Cleanup();
            Completed?.Invoke();
        }
        private void Cleanup()
        {
            IsPlaying = false;
            if (director != null) { director.stopped -= OnStopped; director.Stop(); }
            if (cinematicCamera != null) { cinematicCamera.Priority = -100; cinematicCamera.gameObject.SetActive(false); }
            if (!captured) return;
            captured = false;
            if (brain != null)
            {
                brain.ManualUpdate();
                brain.m_IgnoreTimeScale = oldIgnoreTime;
                brain.m_DefaultBlend = oldBlend;
                brain.m_UpdateMethod = oldUpdateMethod;
                brain.m_BlendUpdateMethod = oldBlendUpdateMethod;
            }
            if (anomalyClock != null) anomalyClock.UseUnscaledTime = oldAnomalyTime;
        }
        private void OnDestroy() => Finish();
    }
}
