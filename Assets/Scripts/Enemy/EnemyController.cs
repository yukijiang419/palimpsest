using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.AI;

namespace Caffeine.Gameplay.Enemy
{
    public enum EnemyType
    {
        Ranged,
        Melee,
        // Spawn slot for the microscope; the prefab itself fights as a ranged enemy.
        Scanner
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(HealthShield), typeof(EnemyHitFeedback))]

    // UE-like controller for an enemy. Handles movement, attack, and death.

    public sealed class EnemyController : MonoBehaviour
    {
        [SerializeField] private CharacterController controller;
        [field: SerializeField] public HealthShield HealthShield { get; private set; }
        [SerializeField] private Transform muzzle;
        [SerializeField] private EnemyType type;
        [SerializeField] private Animator animator;
        [SerializeField] private int encounterGroup;
        [SerializeField, Min(1), Tooltip("Melee attack animations the Animator picks from through AttackVariant.")]
        private int attackVariants = 1;
        [SerializeField, Tooltip("Ranged enemies also ram a player who gets within the configured ram range.")]
        private bool closeRangeRam;

        private enum AwarenessState { Guarding, Fighting, Returning }

        private static readonly int MoveDecisionId = Animator.StringToHash("MoveDe");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int IsAttackingId = Animator.StringToHash("IsAttacking");
        private static readonly int IsStaggeredId = Animator.StringToHash("IsStaggered");
        private static readonly int AttackVariantId = Animator.StringToHash("AttackVariant");
        private static readonly int ShootId = Animator.StringToHash("Shoot");
        private static readonly int MeleeAttackStateId = Animator.StringToHash("HumanM@Attack1H01_L");
        private const string AttackStateTag = "Attack";
        private const float GroundedVerticalVelocity = -2f;

        private EnemySystem enemySystem;
        private Transform playerTransform;
        private HealthShield playerHealthShield;
        private ProjectileSystem projectileSystem;
        private GameplayConfig.EnemySettings enemyConfig;
        private MazeSystem mazeSystem;
        private DamageSystem damageSystem;
        private EnemyHitFeedback hitFeedback;
        private EnemyDeathEffect deathEffect;
        private EnemyRagdoll ragdoll;
        private EnemyScanBeam scanBeam;
        private NavMeshAgent navigation;
        private AwarenessState awareness;
        private Vector3 homePosition;
        private float outOfRangeSince = -1f;
        private float nextPathAt;
        private int strafeSign;
        private float controlExpiresAt;
        private float hitStunExpiresAt;
        private float mazeCooldownExpiresAt;
        private float mazeCooldownDuration;
        private float nextAttackAt;
        private bool isRemoved;
        private bool meleeAttackPending;
        private bool meleeAttackStateEntered;
        private bool meleeImpactApplied;
        private float verticalVelocity;
        private Vector3 lastHitDirection;
        private bool hasShootAnimation;

        public MazeSystem MazeSystem => mazeSystem;
        public Animator VisualAnimator => animator;
        public int EncounterGroup => encounterGroup;
        public void ConfigureEncounterGroup(int group) => encounterGroup = group;
        private bool hasEncounterHome;
        public void ConfigureEncounterHome(Vector3 position)
        {
            homePosition = position;
            hasEncounterHome = true;
        }
        public bool IsControlled => controlExpiresAt > Time.time;
        public bool IsHitStunned => hitStunExpiresAt > Time.time;
        public bool IsMazeOnCooldown => mazeCooldownExpiresAt > Time.time;
        public float MazeCooldownProgress => mazeCooldownDuration > 0f
            ? Mathf.Clamp01(1f - (mazeCooldownExpiresAt - Time.time) / mazeCooldownDuration)
            : 1f;
        public bool IsAlive => HealthShield.IsInitialized && !HealthShield.IsDead && !isRemoved;
        public bool IsFighting => IsAlive && awareness == AwarenessState.Fighting;

        private void Awake()
        {
            hitFeedback = GetComponent<EnemyHitFeedback>();
            navigation = GetComponent<NavMeshAgent>();
            deathEffect = GetComponent<EnemyDeathEffect>();
            if (deathEffect == null)
            {
                deathEffect = gameObject.AddComponent<EnemyDeathEffect>();
            }
            scanBeam = GetComponent<EnemyScanBeam>();
            ragdoll = GetComponent<EnemyRagdoll>();
            if (ragdoll == null)
            {
                ragdoll = gameObject.AddComponent<EnemyRagdoll>();
            }
        }

        private void OnEnable()
        {
            HealthShield.Died += HandleDied;
        }

        private void Start()
        {
            enemyConfig = ConfigSystem.Instance.Config.Enemy;
            hasShootAnimation = HasParameter(ShootId);
            if (!hasEncounterHome) homePosition = transform.position;
            if (navigation == null)
            {
                Debug.LogError("Placed enemy requires a NavMeshAgent.", this);
                return;
            }

            navigation.updatePosition = false;
            navigation.updateRotation = false;
            navigation.speed = enemyConfig.MoveSpeed;
            navigation.stoppingDistance = 0.1f;
        }

        private void OnDisable()
        {
            HealthShield.Died -= HandleDied;
        }

        private void Update()
        {
            if (Caffeine.Gameplay.Persistence.SaveGameService.Restoring) return;
            if (isRemoved)
            {
                return;
            }

            if (HealthShield.IsDead)
            {
                HandleDied(HealthShield);
                return;
            }

            ApplyGravity();

            if (IsHitStunned)
            {
                StopNavigation();
                animator.SetInteger(MoveDecisionId, 0);
                return;
            }
            animator.SetBool(IsStaggeredId, false);

            if (playerTransform == null || enemyConfig == null)
            {
                return;
            }

            Vector3 toPlayer = DirectionToPlayer();
            if (awareness == AwarenessState.Guarding)
            {
                animator.SetInteger(MoveDecisionId, 0);
                if (!playerHealthShield.IsDead && toPlayer.sqrMagnitude <= enemyConfig.AggroRange * enemyConfig.AggroRange)
                {
                    Alert();
                }
                return;
            }

            if (playerHealthShield.IsDead && awareness == AwarenessState.Fighting)
            {
                BeginReturn();
            }
            if (awareness == AwarenessState.Returning)
            {
                UpdateReturn();
                return;
            }
            if (ShouldReturn(toPlayer))
            {
                BeginReturn();
                UpdateReturn();
                return;
            }

            if (type == EnemyType.Ranged && !IsControlled && !IsHitStunned
                && (scanBeam == null || !scanBeam.IsBusy || scanBeam.IsTracking))
            {
                FacePlayer();
            }

            if (meleeAttackPending && UpdateMeleeAttack())
            {
                StopNavigation();
                return;
            }
            // A scan roots the enemy until its beam has fired; crowd control breaks it off.
            if (scanBeam != null && scanBeam.IsBusy)
            {
                if (IsControlled)
                {
                    scanBeam.Cancel();
                }
                else
                {
                    StopNavigation();
                    animator.SetInteger(MoveDecisionId, 0);
                    return;
                }
            }
            if (ShouldRam(toPlayer))
            {
                StopNavigation();
                animator.SetInteger(MoveDecisionId, 0);
                PerformAttack(toPlayer);
                return;
            }
            float startRange = type == EnemyType.Ranged
                ? enemyConfig.PreferredRangeStart
                : enemyConfig.MeleeRangeStart;
            float endRange = type == EnemyType.Ranged
                ? enemyConfig.PreferredRangeEnd
                : enemyConfig.MeleeRangeEnd;

            EnemyDecision decision = EnemyBrain.Evaluate(
                toPlayer.magnitude,
                startRange,
                endRange,
                Time.time >= nextAttackAt,
                IsControlled || IsHitStunned,
                type == EnemyType.Ranged);

            // Firing does not replace the ranged enemy's movement decision.
            if (type == EnemyType.Ranged && decision != EnemyDecision.Disabled
                && Time.time >= nextAttackAt && toPlayer.sqrMagnitude <= endRange * endRange)
            {
                if (HasClearShot())
                {
                    PerformAttack(toPlayer);
                }
                else if (decision == EnemyDecision.Strafe)
                {
                    decision = EnemyDecision.Approach;
                }
            }
            animator.SetInteger(MoveDecisionId, ToMoveDecision(decision));

            if (decision == EnemyDecision.Attack)
            {
                StopNavigation();
                PerformAttack(toPlayer);
                return;
            }

            if (decision == EnemyDecision.Disabled || toPlayer == Vector3.zero)
            {
                StopNavigation();
                return;
            }

            Vector3 direction = toPlayer.normalized;
            if (decision == EnemyDecision.Retreat)
            {
                direction = -direction;
            }
            else if (decision == EnemyDecision.Strafe)
            {
                direction = Vector3.Cross(Vector3.up, direction) * strafeSign;
            }

            Vector3 destination = decision == EnemyDecision.Approach
                ? playerTransform.position
                : transform.position + direction * 4f;
            MoveTo(destination);
            if (type == EnemyType.Melee)
            {
                transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            }
        }

        private void LateUpdate()
        {
            if (type == EnemyType.Ranged && IsFighting && !IsControlled && !IsHitStunned
                && !playerHealthShield.IsDead && (scanBeam == null || !scanBeam.IsBusy || scanBeam.IsTracking))
            {
                // Navigation and the player can both move after the direction was sampled in Update.
                FacePlayer();
            }
        }

        private void FacePlayer()
        {
            Vector3 direction = DirectionToPlayer();
            if (direction.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }

        internal void Register(
            EnemySystem owner,
            int assignedStrafeSign,
            Transform requiredPlayerTransform,
            ProjectileSystem requiredProjectileSystem,
            MazeSystem requiredMazeSystem,
            DamageSystem requiredDamageSystem)
        {
            enemySystem = owner;
            strafeSign = assignedStrafeSign;
            playerTransform = requiredPlayerTransform;
            playerHealthShield = requiredPlayerTransform.GetComponent<HealthShield>();
            projectileSystem = requiredProjectileSystem;
            mazeSystem = requiredMazeSystem;
            damageSystem = requiredDamageSystem;
        }

        public void Alert()
        {
            if (enemySystem == null || !IsAlive)
            {
                return;
            }
            if (encounterGroup > 0)
            {
                enemySystem.AlertGroup(encounterGroup);
            }
            else
            {
                ForceAggro();
            }
        }

        internal void ForceAggro()
        {
            if (!IsAlive || playerHealthShield.IsDead)
            {
                return;
            }
            if (awareness != AwarenessState.Fighting)
            {
                var tutorial = FindFirstObjectByType<Caffeine.Gameplay.UI.TutorialPresenter>();
                if (tutorial != null && !tutorial.TryBeginCombat()) return;
            }
            if (awareness != AwarenessState.Fighting)
            {
                CombatAudioController.Play(CombatSound.EnemyAlert, transform.position + Vector3.up);
            }

            awareness = AwarenessState.Fighting;
            outOfRangeSince = -1f;
            nextPathAt = 0f;
        }

        private bool ShouldReturn(Vector3 toPlayer)
        {
            Vector3 fromHome = transform.position - homePosition;
            fromHome.y = 0f;
            if (fromHome.sqrMagnitude > enemyConfig.LeashRange * enemyConfig.LeashRange)
            {
                return true;
            }

            if (toPlayer.sqrMagnitude <= enemyConfig.DisengageRange * enemyConfig.DisengageRange)
            {
                outOfRangeSince = -1f;
                return false;
            }
            if (outOfRangeSince < 0f)
            {
                outOfRangeSince = Time.time;
            }
            return Time.time - outOfRangeSince >= enemyConfig.DisengageDelay;
        }

        private void BeginReturn()
        {
            awareness = AwarenessState.Returning;
            outOfRangeSince = -1f;
            nextPathAt = 0f;
            CancelMeleeAttack();
        }

        private void UpdateReturn()
        {
            Vector3 toHome = homePosition - transform.position;
            toHome.y = 0f;
            if (toHome.sqrMagnitude <= 1.44f)
            {
                StopNavigation();
                awareness = AwarenessState.Guarding;
                animator.SetInteger(MoveDecisionId, 0);
                return;
            }
            if (IsControlled || IsHitStunned)
            {
                StopNavigation();
                animator.SetInteger(MoveDecisionId, 0);
                return;
            }
            animator.SetInteger(MoveDecisionId, 1);
            MoveTo(homePosition);
            transform.rotation = Quaternion.LookRotation(toHome.normalized, Vector3.up);
        }

        private void MoveTo(Vector3 destination)
        {
            if (navigation == null || !navigation.isOnNavMesh)
            {
                animator.SetInteger(MoveDecisionId, 0);
                return;
            }
            navigation.isStopped = false;
            if (Time.time >= nextPathAt)
            {
                navigation.SetDestination(destination);
                nextPathAt = Time.time + 0.2f;
            }
            controller.Move(navigation.desiredVelocity * Time.deltaTime);
            navigation.nextPosition = transform.position;
        }

        private void StopNavigation()
        {
            if (navigation != null && navigation.isOnNavMesh)
            {
                navigation.isStopped = true;
                navigation.nextPosition = transform.position;
            }
        }

        private bool HasClearShot()
        {
            Vector3 origin = muzzle.position;
            Vector3 ray = ShotDirectionToPlayer();
            RaycastHit[] hits = Physics.RaycastAll(origin,
                ray.normalized,
                ray.magnitude,
                ~0,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }
                return hit.collider.GetComponentInParent<HealthShield>() == playerHealthShield;
            }
            return false;
        }

        public float ControlExpiresAt => controlExpiresAt;

        public void ApplyControl(float duration)
        {
            if (isRemoved || HealthShield.IsDead)
            {
                return;
            }

            controlExpiresAt = duration > 0f ? Time.time + duration : 0f;
        }

        public void ApplyHitStun(float duration)
        {
            if (isRemoved || HealthShield.IsDead || HealthShield.CurrentShield > 0f || duration <= 0f)
            {
                return;
            }

            float expiresAt = Time.time + duration;
            if (expiresAt > hitStunExpiresAt)
            {
                hitStunExpiresAt = expiresAt;
            }
            CancelMeleeAttack();
            StopNavigation();
            animator.SetInteger(MoveDecisionId, 0);
            animator.SetBool(IsStaggeredId, true);
            string reaction = Vector3.Dot(transform.right, lastHitDirection) < 0f
                ? "Base Layer.HitReactLeft" : "Base Layer.HitReactRight";
            animator.CrossFadeInFixedTime(reaction, 0.05f, 0, 0f);
        }

        public void RecordHitDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude > 0.0001f)
            {
                lastHitDirection = worldDirection.normalized;
            }
        }

        public void PlayHitFeedback(float duration, Vector3 hitDirection)
        {
            if (!IsAlive)
            {
                return;
            }

            GameplayConfig.AttackSettings attackSettings = ConfigSystem.Instance.Config.Attack;
            if (HealthShield.CurrentShield > 0f)
            {
                hitFeedback.Play(duration, hitDirection, 0f, 0f, 0f);
                return;
            }

            hitFeedback.Play(duration, hitDirection, attackSettings.HitRecoilDistance,
                attackSettings.HitRecoilDegrees, attackSettings.HitRecoilDuration);
        }

        public void BeginMazeCooldown(float duration)
        {
            mazeCooldownDuration = Mathf.Max(0f, duration);
            mazeCooldownExpiresAt = mazeCooldownDuration > 0f ? Time.time + mazeCooldownDuration : 0f;
        }

        private void PerformAttack(Vector3 toPlayer)
        {
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            if (type == EnemyType.Melee || ShouldRam(toPlayer))
            {
                CombatAudioController.Play(CombatSound.EnemyAttack, transform.position + Vector3.up);
                meleeAttackPending = true;
                meleeAttackStateEntered = false;
                meleeImpactApplied = false;
                if (attackVariants > 1)
                {
                    animator.SetInteger(AttackVariantId, Random.Range(0, attackVariants));
                }
                animator.SetBool(IsAttackingId, true);
                animator.SetTrigger(AttackId);
            }
            else if (scanBeam != null)
            {
                StopNavigation();
                scanBeam.Begin(muzzle, playerTransform, playerHealthShield, damageSystem, animator);
                nextAttackAt = Time.time + scanBeam.Cooldown;
                return;
            }
            else
            {
                projectileSystem.SpawnEnemyShot(muzzle.position, ShotDirectionToPlayer().normalized);
                if (hasShootAnimation)
                {
                    animator.SetTrigger(ShootId);
                }
            }

            nextAttackAt = Time.time + enemyConfig.AttackInterval;
        }

        private bool UpdateMeleeAttack()
        {
            if (IsControlled)
            {
                CancelMeleeAttack();
                return false;
            }

            // While blending, the current state still reports the previous clip. When attacks chain
            // back-to-back that is the finished swing (normalizedTime >= 1), which would apply damage again.
            if (animator.IsInTransition(0))
            {
                if (IsMeleeAttackState(animator.GetNextAnimatorStateInfo(0)))
                {
                    meleeAttackStateEntered = true;
                }

                return true;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (IsMeleeAttackState(state))
            {
                meleeAttackStateEntered = true;
                if (!meleeImpactApplied && state.normalizedTime >= enemyConfig.MeleeImpactNormalizedTime)
                {
                    meleeImpactApplied = true;
                    ApplyMeleeDamage();
                }

                if (state.normalizedTime >= 1f)
                {
                    CancelMeleeAttack();
                    return false;
                }

                return true;
            }

            if (meleeAttackStateEntered)
            {
                CancelMeleeAttack();
                return false;
            }

            return true;
        }

        private void ApplyMeleeDamage()
        {
            CombatAudioController.Play(CombatSound.Swing, transform.position + Vector3.up);
            if (playerHealthShield.IsDead)
            {
                return;
            }

            Vector3 offset = DirectionToPlayer();
            bool ram = type == EnemyType.Ranged;
            float range = ram ? enemyConfig.RangedRamRange : enemyConfig.MeleeRangeEnd;
            if (offset.sqrMagnitude > range * range)
            {
                return;
            }

            damageSystem.Resolve(
                playerHealthShield,
                new DamageRequest(DamageSource.NormalAttack, Faction.Enemy,
                    ram ? enemyConfig.RangedRamDamage : enemyConfig.MeleeDamage));
        }

        private void CancelMeleeAttack()
        {
            if (scanBeam != null)
            {
                scanBeam.Cancel();
            }

            meleeAttackPending = false;
            meleeAttackStateEntered = false;
            meleeImpactApplied = false;
            if (type == EnemyType.Melee || closeRangeRam)
            {
                animator.ResetTrigger(AttackId);
                animator.SetBool(IsAttackingId, false);
            }
        }

        private bool ShouldRam(Vector3 toPlayer)
        {
            return type == EnemyType.Ranged && closeRangeRam && !IsControlled && !IsHitStunned
                && Time.time >= nextAttackAt
                && toPlayer.sqrMagnitude <= enemyConfig.RangedRamRange * enemyConfig.RangedRamRange;
        }

        // Generated rigs tag their attack states; the humanoid controller still uses its clip name.
        private static bool IsMeleeAttackState(AnimatorStateInfo state)
        {
            return state.shortNameHash == MeleeAttackStateId || state.IsTag(AttackStateTag);
        }

        private bool HasParameter(int id)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == id)
                {
                    return true;
                }
            }

            return false;
        }

        // CharacterController has no built-in gravity, so keep the enemy pressed onto the ground.
        private void ApplyGravity()
        {
            verticalVelocity += Physics.gravity.y * Time.deltaTime;
            CollisionFlags flags = controller.Move(Vector3.up * (verticalVelocity * Time.deltaTime));
            if ((flags & CollisionFlags.Below) != 0)
            {
                verticalVelocity = GroundedVerticalVelocity;
            }
        }

        private Vector3 DirectionToPlayer()
        {
            Vector3 direction = playerTransform.position - transform.position;
            direction.y = 0f;
            return direction;
        }

        private Vector3 ShotDirectionToPlayer()
        {
            return playerTransform.position + Vector3.up - muzzle.position;
        }

        private static int ToMoveDecision(EnemyDecision decision)
        {
            return decision switch
            {
                EnemyDecision.Approach => 1,
                EnemyDecision.Retreat => 2,
                EnemyDecision.Strafe => 3,
                _ => 0
            };
        }

        private void HandleDied(HealthShield source)
        {
            if (isRemoved)
            {
                return;
            }

            isRemoved = true;
            enemySystem.DropCoins(transform.position, ConfigSystem.Instance.Config.Enemy.CoinReward);
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            hitFeedback.Stop();
            CancelMeleeAttack();
            if (navigation != null)
            {
                navigation.enabled = false;
            }
            controller.enabled = false;
            animator.SetInteger(MoveDecisionId, 0);
            ragdoll.Play(animator, lastHitDirection);
            deathEffect.Play(() =>
            {
                Destroy(gameObject);
            });
        }

        private void OnDestroy()
        {
            if (enemySystem != null)
            {
                enemySystem.Unregister(this);
            }
        }
        [System.Serializable] public sealed class SaveState { public int awareness; public Vector3 homePosition; public float outOfRangeSince; public float controlExpiresAt; public float hitStunExpiresAt; public float mazeCooldownExpiresAt; public float mazeCooldownDuration; public float nextAttackAt; public bool hasEncounterHome; }
        public SaveState CaptureState() => new SaveState { awareness = (int)awareness, homePosition = homePosition, outOfRangeSince = outOfRangeSince < 0f ? -1f : Time.time - outOfRangeSince, controlExpiresAt = Mathf.Max(0f, controlExpiresAt - Time.time), hitStunExpiresAt = Mathf.Max(0f, hitStunExpiresAt - Time.time), mazeCooldownExpiresAt = Mathf.Max(0f, mazeCooldownExpiresAt - Time.time), mazeCooldownDuration = mazeCooldownDuration, nextAttackAt = Mathf.Max(0f, nextAttackAt - Time.time), hasEncounterHome = hasEncounterHome };
        public void RestoreState(SaveState s)
        {
            if (s == null) return;
            CancelMeleeAttack();
            awareness = (AwarenessState)s.awareness; homePosition = s.homePosition; outOfRangeSince = s.outOfRangeSince < 0f ? -1f : Time.time - s.outOfRangeSince; controlExpiresAt = Time.time + s.controlExpiresAt; hitStunExpiresAt = Time.time + s.hitStunExpiresAt; mazeCooldownExpiresAt = Time.time + s.mazeCooldownExpiresAt; mazeCooldownDuration = s.mazeCooldownDuration; nextAttackAt = Time.time + s.nextAttackAt; hasEncounterHome = s.hasEncounterHome;
            nextPathAt = 0f; verticalVelocity = 0f;
        }

    }
}
