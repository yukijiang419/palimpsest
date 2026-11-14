using System;
using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Player;
using UnityEngine;

namespace Caffeine.Gameplay.Maze
{
    [DisallowMultipleComponent]
    // handle maze targeting, sessions, and effects
    public sealed class MazeSystem : MonoBehaviour
    {
        private const int TokensPerOutcome = 5;

        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerAim playerAim;
        [SerializeField] private PlayerAttackController playerAttackController;
        [SerializeField] private HealthShield playerHealthShield;
        [SerializeField] private EnemySystem enemySystem;
        [SerializeField] private DamageSystem damageSystem;
        [SerializeField] private GameTimeController timeController;

        private GameplayConfig.MazeSettings mazeSettings;
        private GameplayConfig.DamageSettings damageSettings;
        private Transform playerTransform;
        private MazeBoardSession session;
        private EnemyController target;
        private bool waitForMazeRelease;
        private PlayerTargetLock targetLock;

        public event Action SessionChanged;
        public event Action BoardChanged;
        public event Action<MazeResolution> OutcomeResolved;
        public event Action<MazeEndReason> SessionEnded;
        public int LastCancellationFrame { get; private set; } = -1;

        public bool IsSessionOpen => session != null;
        public MazeBoardSession CurrentSession => session;
        public EnemyController CurrentTarget => target;
        public float BonusRecovery { get; private set; }
        public float BonusControlDuration { get; private set; }

        public void AddRecoveryBonus(float amount)
        {
            if (amount > 0f)
            {
                BonusRecovery += amount;
            }
        }

        public void AddControlDurationBonus(float amount)
        {
            if (amount > 0f)
            {
                BonusControlDuration += amount;
            }
        }

        private void Start()
        {
            mazeSettings = ConfigSystem.Instance.Config.Maze;
            damageSettings = ConfigSystem.Instance.Config.Damage;
            playerTransform = inputReader.transform;
            targetLock = inputReader.GetComponent<PlayerTargetLock>();
        }

        private void Update()
        {
            if (session == null)
            {
                UpdateClosedState();
                return;
            }

            // cancel without applying effects when the session is interrupted
            if (inputReader.CancelPressed)
            {
                CancelSession(MazeEndReason.Cancelled);
                return;
            }
            if (!IsTargetValid() || IsOutsideBreakRange())
            {
                CleanupSession(true, !IsTargetValid() ? MazeEndReason.TargetLost : MazeEndReason.OutOfRange);
                return;
            }

            // releasing the maze button applies the current path effects
            if (inputReader.MazeReleased || !inputReader.MazeHeld)
            {
                ResolveAndCleanup();
            }
        }

        private void OnDisable()
        {
            CleanupSession(true);
        }

        // notify the UI only when the path changes
        public bool RequestMove(MoveDirection direction)
        {
            if (session == null || !session.TryMove(direction))
            {
                return false;
            }

            BoardChanged?.Invoke();
            return true;
        }

        public void CancelSession(MazeEndReason? reason = null)
        {
            if (session != null && reason == MazeEndReason.Cancelled)
            {
                LastCancellationFrame = Time.frameCount;
            }

            CleanupSession(true, reason);
        }

        private void UpdateClosedState()
        {
            // wait for release so a cancelled session does not restart while held
            if (waitForMazeRelease)
            {
                if (!inputReader.MazeHeld)
                {
                    waitForMazeRelease = false;
                }

                return;
            }

            if (!inputReader.MazeHeld)
            {
                return;
            }

            EnemyController selectedTarget = SelectNearestTarget();
            if (selectedTarget)
            {
                BeginSession(selectedTarget);
            }
        }

        // Prefer the view lock, then find the nearest available enemy.
        private EnemyController SelectNearestTarget()
        {
            EnemyController preferred = targetLock != null ? targetLock.LockedEnemy : null;
            if (IsAvailableTarget(preferred, out _))
            {
                return preferred;
            }

            EnemyController bestTarget = null;
            float bestDistanceSquared = float.PositiveInfinity;

            foreach (EnemyController candidate in enemySystem.OrderedTargets)
            {
                if (!IsAvailableTarget(candidate, out float distanceSquared))
                {
                    continue;
                }

                if (distanceSquared < bestDistanceSquared)
                {
                    bestTarget = candidate;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return bestTarget;
        }

        private bool IsAvailableTarget(EnemyController candidate, out float distanceSquared)
        {
            distanceSquared = float.PositiveInfinity;
            if (candidate == null || !candidate.IsAlive || !candidate.isActiveAndEnabled
                || candidate.IsControlled || candidate.IsMazeOnCooldown)
            {
                return false;
            }

            Vector3 offset = candidate.transform.position - playerTransform.position;
            offset.y = 0f;
            distanceSquared = offset.sqrMagnitude;
            return distanceSquared <= mazeSettings.StartRange * mazeSettings.StartRange;
        }

        // lock aim, block normal attacks, and slow the game during the maze
        private void BeginSession(EnemyController selectedTarget)
        {
            target = selectedTarget;
            session = new MazeBoardSession(GenerateBoard(), mazeSettings);
            playerAttackController.SetAttackBlocked(true);
            playerAim.CaptureMazeAimLock();
            timeController.SetMazeActive(true);
            SessionChanged?.Invoke();
        }

        private MazeBoard GenerateBoard()
        {
            int boardSize = mazeSettings.BoardSize;
            var positions = new List<GridPosition>(boardSize * boardSize);
            for (int y = 0; y < boardSize; y++)
            {
                for (int x = 0; x < boardSize; x++)
                {
                    positions.Add(new GridPosition(x, y));
                }
            }

            // shuffle positions so each placed cell uses a different location
            for (int index = positions.Count - 1; index > 0; index--)
            {
                int swapIndex = UnityEngine.Random.Range(0, index + 1);
                (positions[index], positions[swapIndex]) = (positions[swapIndex], positions[index]);
            }

            // place the start, obstacles, and five tokens for each outcome
            var cells = new CellKind[positions.Count];
            int cursor = 0;
            SetCell(cells, positions[cursor++], CellKind.Start);
            Place(cells, positions, ref cursor, CellKind.Obstacle, mazeSettings.ObstacleCount);
            Place(cells, positions, ref cursor, CellKind.Recovery, TokensPerOutcome);
            Place(cells, positions, ref cursor, CellKind.Damage, TokensPerOutcome);
            Place(cells, positions, ref cursor, CellKind.Control, TokensPerOutcome);
            return new MazeBoard(boardSize, boardSize, cells, positions[0]);
        }

        private void Place(
            CellKind[] cells,
            IReadOnlyList<GridPosition> positions,
            ref int cursor,
            CellKind kind,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                SetCell(cells, positions[cursor++], kind);
            }
        }

        private void SetCell(CellKind[] cells, GridPosition position, CellKind kind)
        {
            cells[position.Y * mazeSettings.BoardSize + position.X] = kind;
        }

        private void ResolveAndCleanup()
        {
            // start the target cooldown only if an effect was applied
            bool applied = ApplyResolvedEffects();
            if (applied)
            {
                target.BeginMazeCooldown(mazeSettings.PersonalCooldown);
            }

            CleanupSession(false, applied ? MazeEndReason.Resolved : MazeEndReason.NoEffect);
        }

        private bool ApplyResolvedEffects()
        {
            var effects = new List<MazeOutcome>(3);
            // keep references before cleanup because the damage may kill the target
            EnemyController resolvedTarget = target;
            int recoveryStage = session.RecoveryStage;
            if (recoveryStage > 0)
            {
                float before = playerHealthShield.CurrentHealth;
                float healing = recoveryStage == 1
                    ? damageSettings.Recovery.StageOneValue : damageSettings.Recovery.StageTwoValue;
                playerHealthShield.Heal(healing * (1f + BonusRecovery));
                float restored = playerHealthShield.CurrentHealth - before;
                float reductionEnd = 0f;
                if (recoveryStage == 2 && !playerHealthShield.IsDead
                    && damageSettings.Recovery.StageTwoDuration > 0f)
                {
                    playerHealthShield.ApplyDamageReduction(damageSettings.Recovery.StageTwoReduction,
                        damageSettings.Recovery.StageTwoDuration);
                    if (playerHealthShield.DamageReduction > 0f)
                    {
                        reductionEnd = playerHealthShield.ReductionExpiresAt;
                    }
                }
                if (restored > 0f || reductionEnd > Time.time)
                {
                    effects.Add(new MazeOutcome(OutcomeType.Recovery, recoveryStage,
                                            healthRestored: restored, healthBefore: before, healthAfter: playerHealthShield.CurrentHealth,
                                            maxHealth: playerHealthShield.MaxHealth, reductionEndsAt: reductionEnd));
                }
            }

            int damageStage = session.DamageStage;
            if (damageStage > 0 && resolvedTarget.IsAlive)
            {
                HealthShield vitals = resolvedTarget.HealthShield;
                DamageResult result = damageSettings.PathDamage.BreakShieldOnMaze
                    ? damageSystem.BreakShield(vitals)
                    : damageSystem.Resolve(vitals, new DamageRequest(DamageSource.Path, Faction.Player,
                        damageStage == 1 ? damageSettings.PathDamage.StageOneValue : damageSettings.PathDamage.StageTwoValue));
                if (result.HasEffect)
                {
                    effects.Add(new MazeOutcome(OutcomeType.Damage, damageStage,
                                            healthLoss: result.HealthLoss, shieldLoss: result.ShieldLoss,
                                            shieldBroken: result.ShieldLoss > 0f && vitals.CurrentShield <= 0f));
                }
            }

            int controlStage = session.ControlStage;
            if (controlStage > 0 && resolvedTarget.IsAlive)
            {
                float duration = controlStage == 1
                    ? damageSettings.Control.StageOneDuration : damageSettings.Control.StageTwoDuration;
                duration *= 1f + BonusControlDuration;
                if (duration > 0f)
                {
                    resolvedTarget.ApplyControl(duration);
                    if (controlStage == 2)
                    {
                        resolvedTarget.HealthShield.ApplyVulnerability(
                                                    damageSettings.Control.StageTwoVulnerability, duration);
                    }

                    if (resolvedTarget.ControlExpiresAt > Time.time)
                    {
                        effects.Add(new MazeOutcome(OutcomeType.Control, controlStage,
                                                    controlEndsAt: resolvedTarget.ControlExpiresAt,
                                                    vulnerabilityEndsAt: controlStage == 2
                                                        ? resolvedTarget.HealthShield.VulnerabilityExpiresAt : 0f));
                    }
                }
            }

            if (effects.Count > 0)
            {
                OutcomeResolved?.Invoke(new MazeResolution(resolvedTarget, playerHealthShield, effects.ToArray()));
            }

            return effects.Count > 0;
        }

        private bool IsTargetValid()
        {
            return target && target.IsAlive && target.isActiveAndEnabled && !target.IsControlled;
        }

        private bool IsOutsideBreakRange()
        {
            Vector3 offset = target.transform.position - playerTransform.position;
            offset.y = 0f;
            float range = mazeSettings.BreakRange;
            return offset.sqrMagnitude > range * range;
        }

        private void CleanupSession(bool waitForRelease, MazeEndReason? reason = null)
        {
            if (session == null)
            {
                return;
            }

            // clear the session and restore aim, game speed, and normal attacks
            session = null;
            target = null;
            waitForMazeRelease = waitForRelease && inputReader.MazeHeld;
            playerAim.ReleaseMazeAimLock();
            timeController.SetMazeActive(false);
            playerAttackController.SetAttackBlocked(false);
            SessionChanged?.Invoke();
            if (reason.HasValue)
            {
                SessionEnded?.Invoke(reason.Value);
            }
        }
        [System.Serializable] public sealed class SaveState { public float BonusRecovery; public float BonusControlDuration; }
        public SaveState CaptureState() => new SaveState { BonusRecovery = BonusRecovery, BonusControlDuration = BonusControlDuration };
        public void RestoreState(SaveState s)
        {
            if (s == null) return;
            CancelSession();
            BonusRecovery = s.BonusRecovery; BonusControlDuration = s.BonusControlDuration;

        }

    }
}
