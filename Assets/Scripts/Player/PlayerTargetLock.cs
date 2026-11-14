using System;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    public sealed class PlayerTargetLock : MonoBehaviour
    {
        private PlayerInputReader inputReader;
        private HealthShield playerHealth;
        private EnemySystem enemySystem;
        private GameTimeController timeController;
        private GameplayConfig.CameraSettings settings;
        private UnityEngine.Camera viewCamera;
        private Transform lockedTransform;
        private EnemyController lockedEnemy;
        private BossController lockedBoss;
        private Collider targetCollider;
        private bool hasLock;
        private float occludedSeconds;
        private readonly RaycastBuffer sightHits = new();

        public event Action LockChanged;
        public Transform LockedTransform => lockedTransform;
        public EnemyController LockedEnemy => lockedEnemy;
        public bool IsLocked => hasLock && lockedTransform != null;
        public bool IsTargetVisible { get; private set; }
        public bool IsTrackingPaused => !Application.isFocused || inputReader.IsGameplayBlocked
            || (timeController != null && timeController.IsPaused);
        public Vector3 TargetPoint => lockedTransform != null
            ? ResolveTargetPoint(lockedTransform, targetCollider) : transform.position;

        // Camera framing and hacking link share the same enemy body center.
        public static Vector3 GetTargetPoint(Transform target) => ResolveTargetPoint(target, GetTargetBody(target));

        private static Collider GetTargetBody(Transform target)
        {
            var controller = target.GetComponent<CharacterController>();
            return controller != null ? controller : target.GetComponent<Collider>();
        }

        private static Vector3 ResolveTargetPoint(Transform target, Collider body) => body != null && body.enabled
            ? body.bounds.center : target.position + Vector3.up;

        private void Awake()
        {
            inputReader = GetComponent<PlayerInputReader>();
            playerHealth = GetComponent<HealthShield>();
        }

        private void OnEnable() => playerHealth.Died += HandlePlayerDied;

        private void Start()
        {
            settings = ConfigSystem.Instance.Config.Camera;
            enemySystem = FindFirstObjectByType<EnemySystem>();
            timeController = FindFirstObjectByType<GameTimeController>();
            viewCamera = UnityEngine.Camera.main;
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            if (hasLock && (!IsTargetAlive() || playerHealth.IsDead
                            || PlanarDistanceSquared(lockedTransform.position) > settings.LockBreakRange * settings.LockBreakRange))
            {
                ClearLock();
            }

            if (IsTrackingPaused)
            {
                return;
            }

            if (inputReader.LockTargetPressed)
            {
                if (IsLocked)
                {
                    ClearLock();
                }
                else if (!playerHealth.IsDead)
                {
                    AcquireTarget();
                }
            }
            if (!IsLocked)
            {
                return;
            }

            IsTargetVisible = HasLineOfSight(TargetPoint, lockedTransform);
            occludedSeconds = IsTargetVisible ? 0f : occludedSeconds + Time.unscaledDeltaTime;
            if (!IsTargetVisible && occludedSeconds >= settings.LockOcclusionGrace)
            {
                ClearLock();
            }
        }

        private bool IsTargetAlive() => lockedTransform != null &&
            ((lockedEnemy != null && lockedEnemy.isActiveAndEnabled && lockedEnemy.IsAlive)
            || (lockedBoss != null && lockedBoss.isActiveAndEnabled && lockedBoss.IsAlive));

        private void AcquireTarget()
        {
            if (viewCamera == null)
            {
                return;
            }

            Transform best = null;
            EnemyController bestEnemy = null;
            BossController bestBoss = null;
            Collider bestCollider = null;
            float bestScreenDistance = float.PositiveInfinity;
            float bestWorldDistance = float.PositiveInfinity;
            if (enemySystem != null)
            {
                foreach (EnemyController enemy in enemySystem.OrderedTargets)
                {
                    if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAlive)
                    {
                        continue;
                    }

                    Consider(enemy.transform, enemy.GetComponent<CharacterController>(), enemy, null);
                }
            }

            BossController boss = BossController.Current;
            if (boss != null && boss.isActiveAndEnabled && boss.IsAlive)
            {
                Consider(boss.transform, GetTargetBody(boss.transform), null, boss);
            }

            if (best == null)
            {
                return;
            }

            lockedTransform = best;
            lockedEnemy = bestEnemy;
            lockedBoss = bestBoss;
            targetCollider = bestCollider;
            hasLock = true;
            IsTargetVisible = true;
            occludedSeconds = 0f;
            LockChanged?.Invoke();

            void Consider(Transform candidate, Collider body, EnemyController enemy, BossController candidateBoss)
            {
                float distance = PlanarDistanceSquared(candidate.position);
                if (distance > settings.LockStartRange * settings.LockStartRange)
                {
                    return;
                }

                Vector3 point = ResolveTargetPoint(candidate, body);
                Vector3 screen = viewCamera.WorldToViewportPoint(point);
                if (screen.z <= 0f || screen.x < 0f || screen.x > 1f || screen.y < 0f || screen.y > 1f
                    || !HasLineOfSight(point, candidate))
                {
                    return;
                }

                Vector2 centerOffset = new Vector2((screen.x - 0.5f) * viewCamera.aspect, screen.y - 0.5f);
                float screenDistance = centerOffset.sqrMagnitude;
                if (screenDistance > bestScreenDistance
                    || (Mathf.Approximately(screenDistance, bestScreenDistance) && distance >= bestWorldDistance))
                {
                    return;
                }

                best = candidate;
                bestEnemy = enemy;
                bestBoss = candidateBoss;
                bestCollider = body;
                bestScreenDistance = screenDistance;
                bestWorldDistance = distance;
            }
        }

        private float PlanarDistanceSquared(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }

        private bool HasLineOfSight(Vector3 point, Transform target)
        {
            if (viewCamera == null)
            {
                return false;
            }

            Vector3 start = viewCamera.transform.position;
            Vector3 delta = point - start;
            if (delta.sqrMagnitude < 0.001f)
            {
                return true;
            }

            int count = sightHits.Cast(start, delta.normalized, delta.magnitude,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = sightHits[index];
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(target))
                {
                    continue;
                }

                return false;
            }
            return true;
        }

        public void ClearLock()
        {
            bool changed = hasLock;
            hasLock = false;
            lockedTransform = null;
            lockedEnemy = null;
            lockedBoss = null;
            targetCollider = null;
            IsTargetVisible = false;
            occludedSeconds = 0f;
            if (changed)
            {
                LockChanged?.Invoke();
            }
        }

        private void HandlePlayerDied(HealthShield source) => ClearLock();

        private void OnDisable()
        {
            playerHealth.Died -= HandlePlayerDied;
            ClearLock();
        }
    }
}
