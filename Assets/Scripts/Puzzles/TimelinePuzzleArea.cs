using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.UI;
using UnityEngine;

namespace Caffeine.Gameplay.Puzzles
{
    public enum TimelineEra { Past, Present, Future }
    public enum PuzzleCondition { Always, BeforeFirst, AfterFirst, BeforeSecond, AfterSecond, West, East }
    public enum PuzzleAction { PushFirst = 1, PushSecond = 2, AlignWest = 3, AlignEast = 4, Finish = 5 }
    public enum TimelinePuzzleKind { Unassigned, CourtyardWallGap, LibraryStaircase, MainIntersectionBridge }

    public sealed class TimelinePuzzleArea : MonoBehaviour
    {
        public int puzzleIndex; // 1, 2, or 3
        public TimelinePuzzleKind puzzleKind;
        public string title;
        public string stationRecord;
        public Vector3 arrivalPosition;
        public TimelinePuzzlePiece[] pieces;
        public TimelinePuzzlePoint[] points;
        public Transform firstObject;
        public Vector3 firstTargetLocalPosition;
        public GameObject firstGoal;
        public Transform secondObject;
        public Vector3 secondTargetLocalPosition;
        public GameObject secondGoal;
        public Transform support;
        public BoxCollider finishLanding;
        public Light eraLight;
        public Vector2 halfExtents = new(15, 15);
        public float interactionHeight = 5f;

        private PlayerInputReader input;
        private HealthShield health;
        private CharacterController character;
        private MazeSystem maze;
        private float pulseUntil;
        private bool claiming;
        private PuzzleHintPresenter hints;
        private Vector3 supportStartPosition;
        private Quaternion supportStartRotation;

        public TimelineEra Era { get; private set; } = TimelineEra.Past;
        public bool FirstChanged { get; private set; }
        public bool SecondChanged { get; private set; }
        public bool WestAligned { get; private set; }
        public bool EastAligned { get; private set; }
        public bool ReachedIsland { get; private set; }
        public bool Completed { get; private set; }
        public bool IsShifting => GetComponent<PuzzleFeedback>()?.IsShifting == true;

        public static bool RequiresGroundedTraversal(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<TimelinePuzzleArea>(true) != null) return true;
            return false;
        }

        private void Awake()
        {
            if (support == null) return;
            supportStartPosition = support.localPosition;
            supportStartRotation = support.localRotation;
        }

        private void Start()
        {
            BindPlayer();
            maze = FindFirstObjectByType<MazeSystem>();
            hints = PuzzleHintPresenter.Create(this);
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
        }

        private void BindPlayer()
        {
            input = FindFirstObjectByType<PlayerInputReader>();
            if (input == null) return;
            character = input.GetComponent<CharacterController>();
            health = input.GetComponent<HealthShield>();
            if (input.GetComponent<PuzzleBlockPusher>() == null)
                input.gameObject.AddComponent<PuzzleBlockPusher>();
        }

        private void Update()
        {
            if (input == null) BindPlayer();
            if (input == null || input.IsGameplayBlocked || health == null || health.IsDead || Time.timeScale == 0f
                || maze != null && maze.IsSessionOpen) return;
            Vector3 local = transform.InverseTransformPoint(input.transform.position);
            if (local.y < -6f && Mathf.Abs(local.x) <= halfExtents.x + 12f
                && Mathf.Abs(local.z) <= halfExtents.y + 12f)
            {
                health.ApplyResolvedDamage(new DamageResult(0f, health.CurrentHealth + 1f));
                return;
            }
            if (!ContainsPosition(input.transform.position)) return;
            if (IsShifting) return;
            UpdateBlockGoals();
            RecordIslandArrival(input.transform.position);
            if (Era == TimelineEra.Past && input.ResetPuzzlePressed)
            {
                ResetUnsolvedBlock();
                hints?.Flash("Block returned to its start");
                return;
            }
            if (input.TimelineNextPressed || input.TimelinePreviousPressed)
            {
                bool reverse = input.TimelinePreviousPressed;
                input.GetComponent<PuzzleBlockPusher>()?.ReleaseGrip();
                bool shifted = TryShiftNext(reverse, input.transform.position, character, out string message);
                hints?.Flash(shifted ? "Shifted to the " + message : message);
                return;
            }
            TimelinePuzzlePoint nearest = NearestPoint(input.transform.position);
            if (nearest != null && input.TryConsumeInteract())
            {
                if (nearest.action == PuzzleAction.Finish)
                {
                    if (IsSolved && Era == TimelineEra.Present)
                    {
                        ClaimRecord();
                    }
                }
                else hints?.Flash(Apply(nearest));
            }
        }

        public bool ClaimRecord()
        {
            if (IsShifting || claiming || Completed || !IsSolved || Era != TimelineEra.Present || health == null || health.IsDead)
                return false;
            var screens = FindFirstObjectByType<TutorialPresenter>();
            if (screens == null || !screens.ShowArchiveRecord(puzzleIndex, FinishClaim)) return false;
            claiming = true;
            return true;
        }

        private void FinishClaim()
        {
            if (!claiming || Completed || health.IsDead) return;
            claiming = false;
            Complete();
            if (!PuzzleRunState.Finish(puzzleIndex, input))
                Debug.LogWarning($"{title}: record recovered outside campus travel; return to the merchant for the reward.");
        }

        public TimelinePuzzlePoint NearestPoint(Vector3 position)
        {
            TimelinePuzzlePoint best = null;
            float distance = float.PositiveInfinity;
            foreach (var point in points)
            {
                if (!point.gameObject.activeInHierarchy) continue;
                float value = Vector3.Distance(position, point.transform.position);
                if (value <= point.radius && value < distance)
                {
                    best = point;
                    distance = value;
                }
            }
            return best;
        }

        public void UpdateBlockGoals()
        {
            if (Era != TimelineEra.Past || Completed) return;
            bool wasSolved = IsSolved;
            bool firstSeated = false;
            bool secondSeated = false;
            bool changed = false;
            if (!FirstChanged && IsAtGoal(firstObject, firstTargetLocalPosition))
            {
                firstObject.GetComponent<PuzzleMovableBlock>()?.LockAt(firstTargetLocalPosition);
                FirstChanged = true;
                firstSeated = true;
                changed = true;
            }
            if (FirstChanged && !SecondChanged && secondObject != null
                && IsAtGoal(secondObject, secondTargetLocalPosition))
            {
                secondObject.GetComponent<PuzzleMovableBlock>()?.LockAt(secondTargetLocalPosition);
                SecondChanged = true;
                secondSeated = true;
                changed = true;
            }
            if (changed)
            {
                Refresh();
                Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            }
            if (changed) pulseUntil = Time.unscaledTime + .45f;
            if (firstSeated) firstGoal.GetComponent<PuzzleGoalVisual>().PlaySeat();
            if (secondSeated) secondGoal.GetComponent<PuzzleGoalVisual>().PlaySeat();
            if (!wasSolved && IsSolved)
                GetComponent<PuzzleFeedback>().Repaired(secondSeated ? secondObject.position : firstObject.position);
        }

        public bool ContainsPosition(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.z) <= halfExtents.y
                && local.y >= -2f && local.y <= interactionHeight;
        }

        public void ResetUnsolvedBlock()
        {
            if (Era != TimelineEra.Past || Completed) return;
            Transform block = null;
            if (!FirstChanged) block = firstObject;
            else if (!SecondChanged) block = secondObject;
            if (block != null)
            {
                input?.GetComponent<PuzzleBlockPusher>()?.ReleaseGrip();
                block.GetComponent<PuzzleMovableBlock>()?.ResetToStart();
            }
        }

        public void ResetForRetry()
        {
            GetComponent<PuzzleFeedback>()?.CancelShift();
            input?.GetComponent<PuzzleBlockPusher>()?.ReleaseGrip();
            Era = TimelineEra.Past;
            FirstChanged = SecondChanged = WestAligned = EastAligned = ReachedIsland = Completed = false;
            claiming = false;
            pulseUntil = 0f;
            if (firstObject != null) firstObject.GetComponent<PuzzleMovableBlock>()?.ResetToStart();
            if (secondObject != null) secondObject.GetComponent<PuzzleMovableBlock>()?.ResetToStart();
            if (support != null)
            {
                support.localPosition = supportStartPosition;
                support.localRotation = supportStartRotation;
            }
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            Physics.SyncTransforms();
            hints?.Flash("Puzzle restarted");
        }

        public void RecordIslandArrival(Vector3 playerPosition)
        {
            if (puzzleKind != TimelinePuzzleKind.MainIntersectionBridge || Era != TimelineEra.Future
                || !WestAligned || EastAligned || ReachedIsland) return;
            Vector3 local = transform.InverseTransformPoint(playerPosition);
            if (Mathf.Abs(local.x) > 4.5f || local.z < -.8f || local.z > 1.9f
                || local.y < -1f || local.y > 2f) return;
            ReachedIsland = true;
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
        }

        private static bool IsAtGoal(Transform block, Vector3 target)
        {
            if (block == null) return false;
            Vector3 offset = block.localPosition - target;
            return offset.x * offset.x + offset.z * offset.z < .5f * .5f
                && Mathf.Abs(offset.y) < .25f;
        }

        public void Refresh()
        {
            if (eraLight != null)
            {
                eraLight.color = Era switch
                {
                    TimelineEra.Past => new Color(1f, .57f, .26f),
                    TimelineEra.Present => new Color(.35f, .67f, 1f),
                    _ => new Color(.68f, .43f, 1f)
                };
                eraLight.intensity = 1f;
            }
            if (firstObject != null) firstObject.gameObject.SetActive(Era == TimelineEra.Past);
            if (secondObject != null) secondObject.gameObject.SetActive(Era == TimelineEra.Past);
            if (firstGoal != null)
            {
                firstGoal.SetActive(Era == TimelineEra.Past);
                firstGoal.GetComponent<PuzzleGoalVisual>()?.SetSolved(FirstChanged);
            }
            if (secondGoal != null)
            {
                secondGoal.SetActive(Era == TimelineEra.Past && FirstChanged);
                secondGoal.GetComponent<PuzzleGoalVisual>()?.SetSolved(SecondChanged);
            }
            foreach (var piece in pieces)
                piece.gameObject.SetActive(piece.ShouldShow(Era, FirstChanged, SecondChanged, WestAligned, EastAligned));
            foreach (var point in points)
            {
                bool visible = point.fromEra == Era && !Completed;
                if (point.action == PuzzleAction.PushFirst) visible &= !FirstChanged;
                if (point.action == PuzzleAction.PushSecond) visible &= FirstChanged && !SecondChanged;
                if (point.action == PuzzleAction.AlignWest) visible &= !WestAligned && !EastAligned;
                if (point.action == PuzzleAction.AlignEast) visible &= ReachedIsland && FirstChanged && !EastAligned;
                if (point.action == PuzzleAction.Finish) visible &= IsSolved;
                visible &= point.condition switch
                {
                    PuzzleCondition.BeforeFirst => !FirstChanged,
                    PuzzleCondition.AfterFirst => FirstChanged,
                    PuzzleCondition.BeforeSecond => !SecondChanged,
                    PuzzleCondition.AfterSecond => SecondChanged,
                    PuzzleCondition.West => WestAligned && !EastAligned,
                    PuzzleCondition.East => EastAligned,
                    _ => true
                };
                point.gameObject.SetActive(visible);
            }
        }

        public bool IsSolved => puzzleKind switch
        {
            TimelinePuzzleKind.CourtyardWallGap => FirstChanged && SecondChanged,
            TimelinePuzzleKind.LibraryStaircase => FirstChanged && SecondChanged,
            TimelinePuzzleKind.MainIntersectionBridge => WestAligned && ReachedIsland && FirstChanged && EastAligned,
            _ => false
        };

        public bool IsOnFinishLanding(Vector3 position, CharacterController player)
        {
            if (finishLanding == null || !finishLanding.enabled
                || !finishLanding.gameObject.activeInHierarchy || player == null) return false;
            Capsule(position, player, out Vector3 bottom, out _, out float radius);
            Bounds bounds = finishLanding.bounds;
            const float edgeClearance = .02f;
            return bottom.x - radius >= bounds.min.x + edgeClearance
                && bottom.x + radius <= bounds.max.x - edgeClearance
                && bottom.z - radius >= bounds.min.z + edgeClearance
                && bottom.z + radius <= bounds.max.z - edgeClearance
                && Mathf.Abs(bottom.y - radius - bounds.max.y) <= .2f;
        }

        public bool TryShift(TimelineEra destination, Vector3 position, CharacterController player, out string message)
        {
            if (IsShifting) { message = "Timeline scan in progress"; return false; }
            if (destination == Era) { message = "Already in this timeline"; return false; }
            if (player == null) { message = "No player controller"; return false; }

            var feedback = GetComponent<PuzzleFeedback>();
            feedback?.PrepareShift();

            // A shift keeps the player's world position. Preview the destination so every
            // era-specific collider and floor is checked, including movable blocks and bridges.
            Physics.SyncTransforms();
            Capsule(position, player, out Vector3 bottom, out Vector3 top, out float radius);
            var existing = new HashSet<Collider>(Physics.OverlapCapsule(bottom, top, radius,
                ~0, QueryTriggerInteraction.Ignore));
            TimelineEra previous = Era;
            Era = destination;
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            Physics.SyncTransforms();

            bool occupied = false;
            foreach (var collider in Physics.OverlapCapsule(bottom, top, radius,
                         ~0, QueryTriggerInteraction.Ignore))
            {
                if (existing.Contains(collider) || collider.transform.IsChildOf(player.transform)) continue;
                if (!Physics.ComputePenetration(player, position, player.transform.rotation,
                        collider, collider.transform.position, collider.transform.rotation,
                        out _, out float depth) || depth <= .08f) continue;
                occupied = true;
                break;
            }

            bool supported = HasNearbyFloor(position, player);
            if (occupied || !supported)
            {
                feedback?.CancelShift();
                Era = previous;
                Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
                Physics.SyncTransforms();
                message = occupied ? "Shift cancelled — destination occupied"
                    : "Shift cancelled — no floor in destination";
                return false;
            }
            message = destination.ToString();
            pulseUntil = 0f;
            feedback?.Shifted(position, destination, player);
            return true;
        }

        private void LateUpdate()
        {
            if (IsShifting) return;
            if (eraLight != null)
                eraLight.intensity = 1f + Mathf.Clamp01((pulseUntil - Time.unscaledTime) / .45f) * .8f;
        }

        public bool TryShiftNext(bool reverse, Vector3 position, CharacterController player, out string message)
        {
            if (IsShifting) { message = "Timeline scan in progress"; return false; }
            TimelineEra next = Era;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                next = StepEra(next, reverse);
                if (TryShift(next, position, player, out message)) return true;
            }
            message = "No safe timeline at this location";
            return false;
        }

        private static TimelineEra StepEra(TimelineEra era, bool reverse)
        {
            if (reverse)
                return era switch
                {
                    TimelineEra.Past => TimelineEra.Present,
                    TimelineEra.Present => TimelineEra.Future,
                    _ => TimelineEra.Past
                };
            return era switch
            {
                TimelineEra.Past => TimelineEra.Future,
                TimelineEra.Future => TimelineEra.Present,
                _ => TimelineEra.Past
            };
        }

        private static void Capsule(Vector3 position, CharacterController player,
            out Vector3 bottom, out Vector3 top, out float radius)
        {
            Vector3 scale = player.transform.lossyScale;
            radius = player.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = Mathf.Max(radius, player.height * Mathf.Abs(scale.y) * .5f);
            Vector3 centre = position + player.transform.rotation * Vector3.Scale(player.center, scale);
            bottom = centre - Vector3.up * (halfHeight - radius);
            top = centre + Vector3.up * (halfHeight - radius);
        }

        private static bool HasNearbyFloor(Vector3 position, CharacterController player)
        {
            // A small drop is safe while stepping down. An absent upper walkway or
            // bridge is not: shifting there would strand the player in empty space.
            const float rise = .6f;
            const float maxDrop = 1.75f;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * rise,
                         Vector3.down, rise + maxDrop, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(player.transform)) continue;
                if (hit.normal.y < .55f || hit.point.y > position.y + .25f) continue;
                return true;
            }
            return false;
        }

        public string Apply(TimelinePuzzlePoint point)
        {
            if (Era != TimelineEra.Past) return "Make this change in the Past";
            bool wasSolved = IsSolved;
            switch (point.action)
            {
                case PuzzleAction.PushFirst:
                    return "Push the block onto its socket";
                case PuzzleAction.PushSecond:
                    return "Push the brace onto its socket";
                case PuzzleAction.AlignWest:
                    WestAligned = true;
                    if (support != null) support.localRotation = Quaternion.Euler(0, -45, 0);
                    break;
                case PuzzleAction.AlignEast:
                    if (!ReachedIsland) return "Cross the first bridge to the island";
                    if (!FirstChanged) return "Balance the turntable before rotating it";
                    EastAligned = true;
                    if (support != null) support.localRotation = Quaternion.Euler(0, 45, 0);
                    break;
            }
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            if (!wasSolved && IsSolved) GetComponent<PuzzleFeedback>().Repaired(support.position);
            return point.action == PuzzleAction.AlignEast ? "Support aligned EAST — the first bridge is gone"
                : point.action == PuzzleAction.AlignWest ? "Support aligned WEST — the first bridge is ready"
                : "Past changed — inspect the Future";
        }

        public void Complete()
        {
            if (Era != TimelineEra.Present || !IsSolved) return;
            Completed = true;
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
        }
        [System.Serializable] public sealed class SaveState
        {
            public int index; public TimelineEra era; public bool first, second, west, east, island, completed;
            public Vector3 firstPosition, secondPosition, supportPosition; public Quaternion supportRotation;
        }
        public SaveState CaptureState() => new SaveState { index = puzzleIndex, era = Era, first = FirstChanged,
            second = SecondChanged, west = WestAligned, east = EastAligned, island = ReachedIsland, completed = Completed,
            firstPosition = firstObject != null ? firstObject.localPosition : default,
            secondPosition = secondObject != null ? secondObject.localPosition : default,
            supportPosition = support != null ? support.localPosition : default,
            supportRotation = support != null ? support.localRotation : Quaternion.identity };
        public void RestoreState(SaveState s)
        {
            ResetForRetry();
            Era = s.era; FirstChanged = s.first; SecondChanged = s.second; WestAligned = s.west;
            EastAligned = s.east; ReachedIsland = s.island; Completed = s.completed;
            if (firstObject != null) { firstObject.localPosition = s.firstPosition; if (s.first) firstObject.GetComponent<PuzzleMovableBlock>().LockAt(firstTargetLocalPosition); }
            if (secondObject != null) { secondObject.localPosition = s.secondPosition; if (s.second) secondObject.GetComponent<PuzzleMovableBlock>().LockAt(secondTargetLocalPosition); }
            if (support != null) { support.localPosition = s.supportPosition; support.localRotation = s.supportRotation; }
            Refresh();
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave(); Physics.SyncTransforms();
        }

    }
}
