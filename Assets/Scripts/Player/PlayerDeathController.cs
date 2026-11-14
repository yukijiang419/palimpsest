using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.CameraSystem;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Puzzles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthShield), typeof(PlayerMoveController), typeof(PlayerInputReader))]
    public sealed class PlayerDeathController : MonoBehaviour
    {
        // Cache parameter hashes once instead of resolving strings when death occurs.
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int DeathId = Animator.StringToHash("Death");

        // An active Path Interaction must be closed before player input is disabled.
        [SerializeField] private MazeSystem mazeSystem;

        private HealthShield healthShield;
        private PlayerMoveController motor;
        private PlayerInputReader inputReader;
        private PlayerAttackController attackController;
        private Animator animator;
        private PlayerAnimatorDriver animatorDriver;
        private TimelinePuzzleArea puzzleArea;
        private ThirdPersonCameraController cameraController;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 deathPosition;
        private Quaternion deathRotation;

        private bool IsBossScene => SceneManager.GetActiveScene().name == ConfigSystem.Instance.Config.Quests.BossSceneName;
        public string RespawnButtonLabel => puzzleArea != null ? "Retry Puzzle"
            : IsBossScene ? "Respawn at Entrance" : "Respawn";

        // HealthShield raises Died once, but this also protects against duplicate
        // calls caused by another component invoking the handler during teardown.
        private bool isDead;

        private void Awake()
        {
            // All gameplay dependencies live on the player root. The Animator is
            // on the imported character model nested below that root.
            healthShield = GetComponent<HealthShield>();
            motor = GetComponent<PlayerMoveController>();
            inputReader = GetComponent<PlayerInputReader>();
            attackController = GetComponent<PlayerAttackController>();
            animator = GetComponentInChildren<Animator>();
            animatorDriver = GetComponent<PlayerAnimatorDriver>();
            puzzleArea = FindFirstObjectByType<TimelinePuzzleArea>();
            cameraController = FindFirstObjectByType<ThirdPersonCameraController>();
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        private void OnEnable()
        {
            // Subscribe only while this component is active to avoid stale callbacks.
            healthShield.Died += HandlePlayerDied;
        }

        private void OnDisable()
        {
            healthShield.Died -= HandlePlayerDied;
        }

        private void HandlePlayerDied(HealthShield source)
        {
            if (isDead)
            {
                return;
            }

            isDead = true;
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            deathPosition = transform.position;
            deathRotation = transform.rotation;

            // End interactive gameplay immediately so movement or attacks cannot
            // continue while the visual death animation is playing.
            mazeSystem.CancelSession();
            GetComponent<PuzzleBlockPusher>()?.ReleaseGrip();
            attackController.SetAttackBlocked(true);
            motor.StopMoving();
            motor.enabled = false;
            inputReader.enabled = false;

            // Death can interrupt locomotion or an attack and has no outgoing
            // transition, so the character remains in the final fallen pose.
            animator.ResetTrigger(AttackId);
            animator.SetTrigger(DeathId);
        }

        public bool TryRespawn()
        {
            if (!isDead || !healthShield.IsInitialized || !healthShield.IsDead) return false;

            Vector3 position = deathPosition;
            Quaternion rotation = deathRotation;
            if (puzzleArea != null)
            {
                puzzleArea.ResetForRetry();
                // Match the floor clearance used by the authored puzzle player spawn.
                position = puzzleArea.arrivalPosition + Vector3.up * .05f;
                rotation = initialRotation;
            }
            else if (IsBossScene)
            {
                position = initialPosition;
                rotation = initialRotation;
            }

            GetComponent<PuzzleBlockPusher>()?.ResetForRespawn();
            GetComponent<PlayerAim>()?.ReleaseMazeAimLock();
            motor.TeleportTo(position, rotation);
            animatorDriver.ResetForRespawn();
            cameraController.ResetAfterTeleport(rotation);
            healthShield.Revive(puzzleArea != null ? 0f : ConfigSystem.Instance.Config.Player.RespawnProtectionSeconds);
            GetComponent<PlayerStamina>().Refill();
            isDead = false;
            attackController.SetAttackBlocked(false);
            motor.enabled = true;
            inputReader.enabled = true;
            return true;
        }
        public void RestoreDeathState()
        {
            if (!healthShield.IsDead) return;
            HandlePlayerDied(healthShield);
        }

    }
}
