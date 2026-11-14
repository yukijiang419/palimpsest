using System.Collections.Generic;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.UI;
using Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Caffeine.Gameplay.Quests
{
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class QuestOverviewController : MonoBehaviour
    {
        private enum Phase { Idle, Entering, Flying, Pausing, Returning }

        private Merchant merchant;
        private PlayerInputReader input;
        private GameTimeController gameTime;
        private TutorialPresenter screens;
        private MerchantQuestDefinition pending;
        private Transform destination;
        private GameplayConfig.QuestSettings settings;
        private CinemachineBrain brain;
        private CinemachineVirtualCamera overviewCamera;
        private Transform lookAt;
        private CinemachineBlendDefinition savedBlend;
        private CinemachineBlenderSettings savedCustomBlends;
        private bool savedIgnoreTimeScale;
        private Phase phase;
        private float phaseSeconds;
        private Vector3[] route;
        private float[] routeDistances;
        private float[] stopDistances;
        private Vector3[] stopTargets;
        private Vector3[] stopViews;
        private int stopIndex;
        private float legStart;
        private float legEnd;
        private float legDuration;
        private Vector3 departurePosition;
        private Vector3 departureLookAt;
        private float flyoverHeight;
        private readonly Dictionary<Canvas, bool> canvasStates = new();
        public bool IsPlaying => phase != Phase.Idle;

        public void Initialize(Merchant source, PlayerInputReader player, GameTimeController time,
            TutorialPresenter tutorial)
        {
            merchant = source;
            input = player;
            gameTime = time;
            screens = tutorial;
            settings = ConfigSystem.Instance.Config.Quests;
        }

        public void Queue(MerchantQuestDefinition quest, Transform entrance)
        {
            if (quest == null || !quest.HasPuzzle || QuestProgress.ForRun(quest.QuestId).OverviewShown) return;
            pending = quest;
            destination = entrance;
        }

        private void Update()
        {
            if (!IsPlaying)
            {
                if (pending != null && merchant != null && merchant.isActiveAndEnabled && !merchant.IsSessionOpen
                    && (screens == null || !screens.IsShowing) && input != null && !input.IsGameplayBlocked
                    && gameTime != null && !gameTime.IsPaused)
                {
                    BeginOverview();
                }
                return;
            }

            phaseSeconds += Time.unscaledDeltaTime;
            if (phase != Phase.Returning && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                Skip();
                return;
            }
            if (phase == Phase.Entering && phaseSeconds >= settings.OverviewTransitionSeconds && !brain.IsBlending)
            {
                BeginLeg(0f);
            }
            else if (phase == Phase.Flying)
            {
                float t = Mathf.SmoothStep(0f, 1f, phaseSeconds / legDuration);
                ApplyPose(Mathf.Lerp(legStart, legEnd, t), false);
                if (phaseSeconds >= legDuration)
                {
                    phase = Phase.Pausing;
                    phaseSeconds = 0f;
                }
            }
            else if (phase == Phase.Pausing)
            {
                ApplyPose(legEnd, false);
                if (phaseSeconds >= settings.OverviewStopSeconds)
                {
                    stopIndex++;
                    if (stopIndex == stopDistances.Length) Skip();
                    else BeginLeg(legEnd);
                }
            }
            else if (phase == Phase.Returning && phaseSeconds >= settings.OverviewTransitionSeconds && !brain.IsBlending)
            {
                FinishOverview(true);
            }
        }

        private void BeginOverview()
        {
            var quest = pending;
            pending = null;
            if (QuestProgress.ForRun(quest.QuestId).Stage != QuestStage.Travelling) return;
            QuestProgress.ForRun(quest.QuestId).OverviewShown = true;
            var director = FindFirstObjectByType<CampusEncounterDirector>();
            var viewCamera = UnityEngine.Camera.main;
            brain = viewCamera != null ? viewCamera.GetComponent<CinemachineBrain>() : null;
            if (destination == null || director == null || brain == null || brain.ActiveVirtualCamera == null
                || !director.TryGetRoundOverview(quest.Puzzle.puzzleIndex, out var centers, out var enemies))
            {
                Debug.LogWarning($"Task {quest.Puzzle.puzzleIndex} flyover skipped: camera, entrance or round data is unavailable.", this);
                screens?.TryShowCombatTutorialAfterQuestAccepted();
                return;
            }

            var stops = new List<Vector3> { input.transform.position };
            stops.AddRange(centers);
            stops.Add(destination.position);
            if (!TryBuildRoute(stops, out route, out stopDistances, out var error))
            {
                Debug.LogWarning($"Task {quest.Puzzle.puzzleIndex} flyover skipped: {error}", this);
                screens?.TryShowCombatTutorialAfterQuestAccepted();
                return;
            }
            routeDistances = new float[route.Length];
            flyoverHeight = route[0].y + settings.OverviewHeight;
            for (int i = 1; i < route.Length; i++)
                routeDistances[i] = routeDistances[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            stopTargets = stops.GetRange(1, stops.Count - 1).ToArray();
            stopViews = new Vector3[stopTargets.Length];
            int firstGroup = ConfigSystem.Instance.Config.Encounters.RoundStart(quest.Puzzle.puzzleIndex) + 1;
            for (int i = 0; i < stopTargets.Length; i++)
            {
                var subjects = new List<Vector3>();
                foreach (var enemy in enemies)
                    if (enemy.EncounterGroup == firstGroup + i) subjects.Add(PlayerTargetLock.GetTargetPoint(enemy.transform));
                if (subjects.Count == 0) subjects.Add(stopTargets[i] + Vector3.up);
                Vector3 approach = PointOnRoute(Mathf.Max(0f, stopDistances[i] - settings.OverviewLookAhead));
                Vector3 direction = approach - stopTargets[i];
                direction.y = 0f;
                direction = direction.sqrMagnitude > .01f ? direction.normalized : Vector3.back;
                stopViews[i] = stopTargets[i] + direction * settings.OverviewLookAhead;
                stopViews[i].y = flyoverHeight;
                Vector3 focus = Vector3.zero;
                foreach (var subject in subjects) focus += subject;
                focus /= subjects.Count;
                stopTargets[i] = focus - Vector3.up;
            }

            savedBlend = brain.m_DefaultBlend;
            savedCustomBlends = brain.m_CustomBlends;
            savedIgnoreTimeScale = brain.m_IgnoreTimeScale;
            brain.m_CustomBlends = null;
            brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut,
                settings.OverviewTransitionSeconds);
            brain.m_IgnoreTimeScale = true;
            phase = Phase.Entering;
            phaseSeconds = 0f;
            stopIndex = 0;
            gameTime.SetQuestOverviewPaused(true);
            input.SetQuestOverviewBlocked(true);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                canvasStates.Add(canvas, canvas.enabled);
                canvas.enabled = false;
            }

            var cameraObject = new GameObject("Quest overview camera");
            cameraObject.SetActive(false);
            cameraObject.transform.SetParent(transform, false);
            overviewCamera = cameraObject.AddComponent<CinemachineVirtualCamera>();
            overviewCamera.m_Lens = LensSettings.FromCamera(viewCamera);
            overviewCamera.m_Lens.FieldOfView = 60f;
            overviewCamera.Priority = brain.ActiveVirtualCamera.Priority + 10;
            lookAt = new GameObject("Flyover look ahead").transform;
            lookAt.SetParent(transform, false);
            overviewCamera.LookAt = lookAt;
            ApplyPose(0f, true);
            cameraObject.SetActive(true);
        }

        private void BeginLeg(float from)
        {
            departurePosition = overviewCamera.transform.position;
            departureLookAt = lookAt.position;
            phase = Phase.Flying;
            phaseSeconds = 0f;
            legStart = from;
            legEnd = Mathf.Max(from, stopDistances[stopIndex] - settings.OverviewLookAhead);
            float total = Mathf.Max(1f, routeDistances[routeDistances.Length - 1] - settings.OverviewLookAhead);
            legDuration = Mathf.Max(.3f, (legEnd - legStart) / total * settings.OverviewFlyoverSeconds);
        }

        private Vector3 PointOnRoute(float distance)
        {
            for (int i = 1; i < route.Length; i++)
            {
                if (distance <= routeDistances[i])
                {
                    float length = routeDistances[i] - routeDistances[i - 1];
                    return Vector3.Lerp(route[i - 1], route[i], length > 0f ? (distance - routeDistances[i - 1]) / length : 0f);
                }
            }
            return route[route.Length - 1];
        }

        private void ApplyPose(float distance, bool immediate)
        {
            Vector3 position = PointOnRoute(distance) + Vector3.up * settings.OverviewHeight;
            lookAt.position = PointOnRoute(distance + settings.OverviewLookAhead) + Vector3.up;
            float remaining = Mathf.Max(0f, stopDistances[stopIndex] - settings.OverviewLookAhead - distance);
            float approach = Mathf.SmoothStep(0f, 1f, 1f - remaining / 16f);
            position = Vector3.Lerp(position, stopViews[stopIndex], approach);
            lookAt.position = Vector3.Lerp(lookAt.position, stopTargets[stopIndex] + Vector3.up, approach);
            if (phase == Phase.Flying)
            {
                float departureLength = Mathf.Max(.01f, Mathf.Min(16f, (legEnd - legStart) * .5f));
                float departure = 1f - Mathf.SmoothStep(0f, 1f, (distance - legStart) / departureLength);
                position = Vector3.Lerp(position, departurePosition, departure);
                lookAt.position = Vector3.Lerp(lookAt.position, departureLookAt, departure);
            }
            position.y = flyoverHeight;
            Quaternion rotation = Quaternion.LookRotation(lookAt.position - position, Vector3.up);
            if (!immediate)
                rotation = Quaternion.Slerp(overviewCamera.transform.rotation, rotation, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            overviewCamera.transform.SetPositionAndRotation(position, rotation);
        }

        internal static bool TryBuildRoute(IReadOnlyList<Vector3> stops, out Vector3[] points,
            out float[] arrivals, out string error)
        {
            var route = new List<Vector3>();
            var distances = new List<float>();
            var path = new NavMeshPath();
            float length = 0f;
            for (int i = 1; i < stops.Count; i++)
            {
                if (!NavMesh.SamplePosition(stops[i - 1], out var from, 4f, NavMesh.AllAreas)
                    || !NavMesh.SamplePosition(stops[i], out var to, 4f, NavMesh.AllAreas)
                    || !NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path)
                    || path.status != NavMeshPathStatus.PathComplete)
                {
                    points = null;
                    arrivals = null;
                    error = $"route segment {i} ({(i == 1 ? "Start" : "Squad " + (i - 1))} to "
                        + $"{(i == stops.Count - 1 ? "Entrance" : "Squad " + i)}) has no complete NavMesh path.";
                    return false;
                }
                foreach (var corner in path.corners)
                {
                    if (route.Count > 0)
                    {
                        float step = Vector3.Distance(route[route.Count - 1], corner);
                        if (step < .001f) continue;
                        length += step;
                    }
                    route.Add(corner);
                }
                distances.Add(length);
            }
            points = route.ToArray();
            arrivals = distances.ToArray();
            error = null;
            return true;
        }

        public void Skip()
        {
            if (!IsPlaying || phase == Phase.Returning) return;
            phase = Phase.Returning;
            phaseSeconds = 0f;
            overviewCamera.gameObject.SetActive(false);
        }

        private void FinishOverview(bool showTutorial)
        {
            if (!IsPlaying) return;
            phase = Phase.Idle;
            if (overviewCamera != null)
            {
                overviewCamera.gameObject.SetActive(false);
                Destroy(overviewCamera.gameObject);
                overviewCamera = null;
            }
            if (lookAt != null) Destroy(lookAt.gameObject);
            foreach (var state in canvasStates)
            {
                if (state.Key != null) state.Key.enabled = state.Value;
            }
            canvasStates.Clear();
            if (brain != null)
            {
                if (!showTutorial)
                {
                    brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
                    brain.ManualUpdate();
                    brain.ActiveBlend = null;
                }
                brain.m_DefaultBlend = savedBlend;
                brain.m_CustomBlends = savedCustomBlends;
                brain.m_IgnoreTimeScale = savedIgnoreTimeScale;
            }
            if (gameTime != null) gameTime.SetQuestOverviewPaused(false);
            if (input != null) input.SetQuestOverviewBlocked(false);
            if (showTutorial) screens?.TryShowCombatTutorialAfterQuestAccepted();
        }

        public void Cancel()
        {
            pending = null;
            FinishOverview(false);
        }

        private void OnDisable() => Cancel();
    }
}
