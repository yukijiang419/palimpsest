using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Puzzles;
using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class PlayerMoveController : MonoBehaviour
    {
        private enum MovementState
        {
            Normal,
            Dashing,
            Sprinting
        }

        [SerializeField] private CharacterController controller;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerStamina stamina;
        [SerializeField] private PlayerAttackController attackController;

        private GameplayConfig.PlayerSettings playerConfig;
        private MovementState movementState;
        private Vector3 dashDirection;
        private Vector2 dashAnimationInput;
        private Vector3 planarVelocity;
        private float dashTimeRemaining;
        private float verticalSpeed;
        private int traversalSceneHandle = int.MinValue;
        private bool groundedPuzzleScene;

        public bool IsGroundedPuzzle
        {
            get
            {
                var scene = gameObject.scene;
                if (traversalSceneHandle != scene.handle)
                {
                    groundedPuzzleScene = TimelinePuzzleArea.RequiresGroundedTraversal(scene);
                    traversalSceneHandle = scene.handle;
                }
                return groundedPuzzleScene;
            }
        }
        private bool shiftSessionActive;
        private bool requiresShiftRelease;
        private Vector3 pushAxis;
        private float pushSpeed;

        public float CurrentSpeed { get; private set; }
        public Vector2 LocalMoveInput { get; private set; }
        public float AnimationSpeed => playerConfig == null ? 0f : CurrentSpeed / playerConfig.MoveSpeed;
        public bool IsGrounded => controller != null && controller.isGrounded && verticalSpeed <= 0f;
        public float VerticalSpeed => verticalSpeed;
        public bool IsSliding => movementState == MovementState.Dashing;
        public float SlideProgress => playerConfig == null || playerConfig.DashDuration <= 0f
            ? 0f : 1f - dashTimeRemaining / playerConfig.DashDuration;
        public Vector2 SlideLocalInput => dashAnimationInput;
        public bool IsPushing => pushAxis != Vector3.zero;
        public Vector3 PushAxis => pushAxis;
        // Signed so pulling a block back plays the push animation in reverse.
        public float PushAnimationSpeed => pushSpeed > 0f ? Vector3.Dot(planarVelocity, pushAxis) / pushSpeed : 0f;
        public event System.Action MovementStopped;

        private void Awake()
        {
            if (attackController == null)
            {
                attackController = GetComponent<PlayerAttackController>();
            }
        }

        private void Start()
        {
            playerConfig = ConfigSystem.Instance.Config.Player;
        }

        private void Update()
        {
            if (inputReader.IsGameplayBlocked)
            {
                StopMoving();
                MoveWithGravity(Vector3.zero);
                return;
            }
            if (Time.timeScale <= 0f) return;
            Vector2 moveInput = inputReader.MoveInput;

            if (IsPushing)
            {
                movementState = MovementState.Normal;
                MoveToward(pushAxis * moveInput.y, pushSpeed);
                return;
            }

            if (!inputReader.DashSprintHeld)
            {
                shiftSessionActive = false;
                requiresShiftRelease = false;
                if (movementState == MovementState.Sprinting)
                {
                    movementState = MovementState.Normal;
                }
            }

            if (movementState == MovementState.Dashing)
            {
                LocalMoveInput = Vector2.zero;
                UpdateDash();
                return;
            }

            if (attackController != null && attackController.IsMovementRestricted
                && !attackController.CanCancelIntoSlide)
            {
                if (inputReader.DashSprintPressed)
                {
                    requiresShiftRelease = true;
                    shiftSessionActive = false;
                }
                MoveDuringAttack(moveInput);
                return;
            }

            if (attackController != null && attackController.IsMovementRestricted
                && !inputReader.DashSprintPressed)
            {
                MoveDuringAttack(moveInput);
                return;
            }

            if (requiresShiftRelease)
            {
                if (attackController != null && attackController.IsMovementRestricted)
                {
                    MoveDuringAttack(moveInput);
                }
                else
                {
                    MoveNormally(moveInput);
                }

                return;
            }

            if (inputReader.DashSprintPressed)
            {
                if (TryStartDash(moveInput))
                {
                    UpdateDash();
                    return;
                }

                requiresShiftRelease = true;
                shiftSessionActive = false;
                if (attackController != null && attackController.IsMovementRestricted)
                {
                    MoveDuringAttack(moveInput);
                }
                else
                {
                    MoveNormally(moveInput);
                }

                return;
            }

            if (attackController != null && attackController.IsMovementRestricted)
            {
                MoveDuringAttack(moveInput);
                return;
            }

            if (shiftSessionActive
                && inputReader.DashSprintHeld
                && moveInput.sqrMagnitude > 0f
                && stamina.CurrentStamina > 0f)
            {
                UpdateSprint(moveInput);
                return;
            }

            MoveNormally(moveInput);
        }

        private bool TryStartDash(Vector2 moveInput)
        {
            if (inputReader.IsGameplayBlocked || IsSliding)
            {
                return false;
            }

            if (attackController != null && attackController.HasPendingAttack
                            && !attackController.CanCancelIntoSlide)
            {
                return false;
            }

            if (!stamina.TrySpend(playerConfig.DashStaminaCost))
            {
                return false;
            }

            Vector3 direction = ResolveWorldDirection(moveInput, transform.forward, transform.right);
            if (direction == Vector3.zero)
            {
                direction = transform.forward;
                direction.y = 0f;
            }

            dashDirection = direction.normalized;
            dashAnimationInput = moveInput == Vector2.zero ? Vector2.up : moveInput.normalized;
            LocalMoveInput = dashAnimationInput;
            dashTimeRemaining = playerConfig.DashDuration;
            movementState = MovementState.Dashing;
            attackController?.TryCancelIntoSlide();
            shiftSessionActive = true;
            return true;
        }

        private void UpdateDash()
        {
            float duration = playerConfig.DashDuration;
            float stepTime = Time.deltaTime < dashTimeRemaining ? Time.deltaTime : dashTimeRemaining;
            float startProgress = 1f - dashTimeRemaining / duration;
            dashTimeRemaining -= stepTime;
            float endProgress = 1f - dashTimeRemaining / duration;

            // ease out: speed falls from 1.5x to 0.5x the average, so the dash hands off into a run
            float distance = playerConfig.DashDistance
                * (DashTravel(endProgress) - DashTravel(startProgress));
            MoveWithGravity(dashDirection * distance);
            CurrentSpeed = 0f;
            planarVelocity = dashDirection
                * (playerConfig.DashDistance / duration * (1.5f - endProgress));

            if (dashTimeRemaining <= 0f)
            {
                movementState = MovementState.Normal;
            }
        }

        private static float DashTravel(float progress) => 1.5f * progress - 0.5f * progress * progress;

        private void UpdateSprint(Vector2 moveInput)
        {
            movementState = MovementState.Sprinting;
            Move(moveInput, playerConfig.SprintSpeed);

            if (!stamina.Drain(playerConfig.SprintStaminaPerSecond * Time.deltaTime))
            {
                movementState = MovementState.Normal;
                shiftSessionActive = false;
                requiresShiftRelease = true;
            }
        }

        private void MoveNormally(Vector2 moveInput)
        {
            movementState = MovementState.Normal;
            Move(moveInput, playerConfig.MoveSpeed);
        }

        private void MoveDuringAttack(Vector2 moveInput)
        {
            movementState = MovementState.Normal;
            Move(moveInput, playerConfig.MoveSpeed * attackController.MovementSpeedMultiplier);
        }

        // Puzzle blocks: while gripping, the player only walks slowly along the push axis.
        public void BeginPush(Vector3 axis, float speed)
        {
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.0001f) return;
            pushAxis = axis.normalized;
            pushSpeed = speed;
            movementState = MovementState.Normal;
            dashTimeRemaining = 0f;
            shiftSessionActive = false;
            requiresShiftRelease = true;
        }

        public void EndPush() => pushAxis = Vector3.zero;

        private void Move(Vector2 moveInput, float speed) =>
            MoveToward(ResolveWorldDirection(moveInput, transform.forward, transform.right), speed);

        private void MoveToward(Vector3 direction, float speed)
        {
            float deltaTime = Time.deltaTime;
            Vector3 targetVelocity = direction * speed;
            float rate = targetVelocity == Vector3.zero ? playerConfig.Deceleration : playerConfig.Acceleration;
            if (!IsGrounded) rate *= playerConfig.AirControl;
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity, rate * deltaTime);

            CollisionFlags collisions = MoveWithGravity(planarVelocity * deltaTime);
            if ((collisions & CollisionFlags.Sides) != 0 && deltaTime > 0f)
            {
                // keep only the sliding part so walls do not store speed
                Vector3 actual = controller.velocity;
                actual.y = 0f;
                if (actual.sqrMagnitude < planarVelocity.sqrMagnitude)
                {
                    planarVelocity = actual;
                }
            }

            CurrentSpeed = planarVelocity.magnitude;
            Vector3 local = transform.InverseTransformDirection(planarVelocity);
            float walkRatio = Mathf.Min(CurrentSpeed / playerConfig.MoveSpeed, 1f);
            LocalMoveInput = CurrentSpeed > 0.001f
                ? new Vector2(local.x, local.z).normalized * walkRatio : Vector2.zero;
        }

        private CollisionFlags MoveWithGravity(Vector3 horizontalMovement)
        {
            float deltaTime = Time.deltaTime;
            if (controller.isGrounded && verticalSpeed < 0f)
            {
                verticalSpeed = -2f;
            }
            else
            {
                verticalSpeed = Mathf.Max(verticalSpeed + Physics.gravity.y * deltaTime, -50f);
            }

            CollisionFlags collisions = controller.Move(
                horizontalMovement + Vector3.up * (verticalSpeed * deltaTime));
            if ((collisions & CollisionFlags.Below) != 0 && verticalSpeed < 0f)
            {
                verticalSpeed = -2f;
            }

            return collisions;
        }

        // CharacterController sweeps the rush against walls instead of teleporting through them.
        public void ApplyCombatStep(Vector3 direction, float distance)
        {
            if (!isActiveAndEnabled || !controller.enabled || inputReader.IsGameplayBlocked || IsGroundedPuzzle || IsPushing) return;
            direction.y = 0f;
            controller.Move(direction.normalized * distance);
            planarVelocity = Vector3.zero;
        }

        public void StopMoving()
        {
            movementState = MovementState.Normal;
            dashTimeRemaining = 0f;
            shiftSessionActive = false;
            requiresShiftRelease = false;
            planarVelocity = Vector3.zero;
            CurrentSpeed = 0f;
            LocalMoveInput = Vector2.zero;
            MovementStopped?.Invoke();
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            StopMoving();
            verticalSpeed = 0f;
            bool wasEnabled = controller.enabled;
            if (wasEnabled) controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (wasEnabled) controller.enabled = true;
        }

        private void OnDisable()
        {
            EndPush();
            StopMoving();
        }

        private static Vector3 ResolveWorldDirection(Vector2 input, Vector3 forward, Vector3 right)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            return forward * input.y + right * input.x;
        }
    }
}
