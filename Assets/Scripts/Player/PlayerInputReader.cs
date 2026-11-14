using UnityEngine;
using UnityEngine.InputSystem;

namespace Caffeine.Gameplay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWallet))]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private const string GameplayMap = "Gameplay";

        [SerializeField] private PlayerInput playerInput;

        private InputActionMap gameplayActionMap;
        private InputAction moveAction;
        private InputAction pointerDeltaAction;
        private InputAction attackAction;
        private InputAction rushAction;
        private InputAction precisionAction;
        private InputAction dashSprintAction;
        private InputAction mazeAction;
        private InputAction cancelAction;
        private InputAction weaponSlot1Action;
        private InputAction weaponSlot2Action;
        private InputAction interactAction;
        private InputAction gripAction;
        private InputAction timelineNextAction;
        private InputAction timelinePreviousAction;
        private InputAction resetPuzzleAction;
        private InputAction weaponStatsAction;
        private InputAction lockTargetAction;
        private int interactionConsumedFrame = -1;
        private readonly TutorialInputGate tutorialInputGate = new TutorialInputGate();

        private bool shopInputBlocked;
        private bool tutorialInputBlocked;
        private bool boundaryTransitionBlocked;
        private bool questOverviewBlocked;
        public bool IsTimelineShiftBlocked { get; private set; }
        private PlayerMoveController motor;

        private bool CanReadInput => isActiveAndEnabled && tutorialInputGate.CanRead(Time.frameCount);
        private bool CanReadPuzzleInput => CanReadInput && !IsTimelineShiftBlocked;
        private bool UsesPuzzleControls => motor != null && motor.IsGroundedPuzzle;
        public bool IsGameplayBlocked => IsGameplayBlockedByInterface;
        public bool IsGameplayBlockedByInterface => !isActiveAndEnabled || tutorialInputBlocked || shopInputBlocked
            || boundaryTransitionBlocked || questOverviewBlocked;

        public Vector2 MoveInput => CanReadInput ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
        public Vector2 PointerDelta => CanReadInput ? pointerDeltaAction.ReadValue<Vector2>() : Vector2.zero;
        public bool GripPressed => CanReadPuzzleInput && (UsesPuzzleControls ? rushAction : gripAction).WasPressedThisFrame();
        public bool GripHeld => CanReadPuzzleInput && (UsesPuzzleControls ? rushAction : gripAction).IsPressed();
        public bool TimelineNextPressed => CanReadPuzzleInput && timelineNextAction.WasPressedThisFrame();
        public bool TimelinePreviousPressed => CanReadPuzzleInput && timelinePreviousAction.WasPressedThisFrame();
        public bool ResetPuzzlePressed => CanReadPuzzleInput && resetPuzzleAction.WasPressedThisFrame();
        public bool WeaponStatsPressed => CanReadInput && weaponStatsAction.WasPressedThisFrame();
        public bool LockTargetPressed => CanReadInput && lockTargetAction.WasPressedThisFrame();
        public bool TryConsumeInteract()
        {
            if (!CanReadPuzzleInput || !interactAction.WasPressedThisFrame() || interactionConsumedFrame == Time.frameCount)
                return false;
            interactionConsumedFrame = Time.frameCount;
            return true;
        }

        public bool RushPressed => CanReadInput && !UsesPuzzleControls && rushAction.WasPressedThisFrame();
        public bool PrecisionPressed => CanReadInput && !UsesPuzzleControls && precisionAction.WasPressedThisFrame();
        public bool AttackPressed => CanReadInput && !UsesPuzzleControls && attackAction.WasPressedThisFrame();
        public bool DashSprintPressed => CanReadInput && !UsesPuzzleControls && dashSprintAction.WasPressedThisFrame();
        public bool DashSprintHeld => CanReadInput && !UsesPuzzleControls && dashSprintAction.IsPressed();
        public bool MazeHeld => CanReadInput && !UsesPuzzleControls && mazeAction.IsPressed();
        public bool MazeReleased => CanReadInput && !UsesPuzzleControls && mazeAction.WasReleasedThisFrame();
        public bool CancelPressed => CanReadInput && cancelAction.WasPressedThisFrame();
        public bool WeaponSlot1Pressed => CanReadInput && weaponSlot1Action.WasPressedThisFrame();
        public bool WeaponSlot2Pressed => CanReadInput && weaponSlot2Action.WasPressedThisFrame();

        public void SetTutorialInputBlocked(bool blocked)
        {
            tutorialInputBlocked = blocked;
            UpdateInputGate();
        }

        public void SetShopInputBlocked(bool blocked)
        {
            shopInputBlocked = blocked;
            UpdateInputGate();
        }

        public void SetBoundaryTransitionBlocked(bool blocked)
        {
            boundaryTransitionBlocked = blocked;
            UpdateInputGate();
        }

        public void SetQuestOverviewBlocked(bool blocked)
        {
            questOverviewBlocked = blocked;
            UpdateInputGate();
        }

        public void SetTimelineShiftBlocked(bool blocked)
        {
            IsTimelineShiftBlocked = blocked;
        }

        // Only the explicit locks: callers may run before this component is enabled after a scene load,
        // and CanReadInput already checks isActiveAndEnabled, so it must not freeze the gate shut.
        private void UpdateInputGate() => tutorialInputGate.SetBlocked(
            tutorialInputBlocked || shopInputBlocked || boundaryTransitionBlocked || questOverviewBlocked, Time.frameCount);

        private void LateUpdate()
        {
            bool anyControlHeld = false;
            foreach (var action in gameplayActionMap.actions)
                if (action != pointerDeltaAction && action.IsPressed()) { anyControlHeld = true; break; }
            tutorialInputGate.AdvanceFrame(Time.frameCount, anyControlHeld);
        }

        private void OnEnable()
        {
            // find and start the input actions
            gameplayActionMap = playerInput.actions.FindActionMap(GameplayMap, false);
            moveAction = FindRequiredAction("Move");
            pointerDeltaAction = FindRequiredAction("PointerDelta");
            attackAction = FindRequiredAction("Attack");
            rushAction = FindRequiredAction("RushAttack");
            precisionAction = FindRequiredAction("PrecisionCut");
            dashSprintAction = FindRequiredAction("DashSprint");
            mazeAction = FindRequiredAction("Maze");
            cancelAction = FindRequiredAction("Cancel");
            weaponSlot1Action = FindRequiredAction("WeaponSlot1");
            weaponSlot2Action = FindRequiredAction("WeaponSlot2");
            interactAction = FindRequiredAction("Interact");
            motor = GetComponent<PlayerMoveController>();
            // Read scene context with input, after the puzzle hierarchy is ready; F never requests both actions.
            gripAction = FindRequiredAction("Grip");
            timelineNextAction = FindRequiredAction("TimelineNext");
            timelinePreviousAction = FindRequiredAction("TimelinePrevious");
            resetPuzzleAction = FindRequiredAction("ResetPuzzle");
            weaponStatsAction = FindRequiredAction("WeaponStats");
            lockTargetAction = FindRequiredAction("LockTarget");
            gameplayActionMap.Enable();
        }

        private void OnDisable()
        {
            gameplayActionMap.Disable();
        }

        private InputAction FindRequiredAction(string actionName)
        {
            // get one needed input action
            InputAction action = gameplayActionMap.FindAction(actionName, false);
            if (action == null) throw new System.InvalidOperationException($"Missing {GameplayMap}/{actionName} input action");
            return action;
        }

    }

    // Frame-based gate keeps closing clicks and held controls out of gameplay.
    internal sealed class TutorialInputGate
    {
        private bool blocked;
        private bool waitingForRelease;
        private int blockedThroughFrame = -1;

        public bool CanRead(int frame) => !blocked && !waitingForRelease && frame > blockedThroughFrame;

        public void SetBlocked(bool value, int frame)
        {
            blocked = value;
            waitingForRelease = !value;
            blockedThroughFrame = frame;
        }

        public void AdvanceFrame(int frame, bool anyControlHeld)
        {
            if (waitingForRelease && frame > blockedThroughFrame && !anyControlHeld)
            {
                waitingForRelease = false;
                // Button-up events and mouse delta on the release frame remain neutral.
                blockedThroughFrame = frame;
            }
        }
    }
}
