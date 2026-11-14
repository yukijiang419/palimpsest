using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Presentation;
using UnityEngine;

namespace Caffeine.Gameplay.Enemy
{
    // Track the player, freeze the aim, then fire along the scan line.
    [DisallowMultipleComponent]
    public sealed class EnemyScanBeam : MonoBehaviour
    {
        private static readonly int ScanningId = Animator.StringToHash("IsScanning");
        private static readonly int ShootId = Animator.StringToHash("Shoot");

        [SerializeField, Min(0.1f)] private float scanTime = 0.9f;
        [SerializeField, Min(0f), Tooltip("Seconds the aim is frozen before the beam fires.")]
        private float lockTime = 0.35f;
        [SerializeField, Min(0.05f)] private float fireTime = 0.25f;
        [SerializeField, Min(1f)] private float range = 16f;
        [SerializeField, Min(0f)] private float damage = 14f;
        [SerializeField, Min(0f), Tooltip("Seconds from one scan to the next.")]
        private float cooldown = 3.2f;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color scanColor = new(1f, 0.15f, 0.1f, 0.8f);
        [SerializeField] private Color lockColor = new(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color beamColor = new(0.35f, 0.95f, 1f, 1f);
        [SerializeField] private float scanWidth = 0.025f;
        [SerializeField] private float beamWidth = 0.22f;

        private enum Phase { Idle, Scanning, Locked, Firing }

        private LineRenderer line;
        private Transform muzzle;
        private Transform target;
        private HealthShield targetHealth;
        private DamageSystem damageSystem;
        private Animator animator;
        private Phase phase;
        private float phaseEndsAt;
        private Vector3 lockedDirection;
        private readonly RaycastBuffer beamHits = new();

        public bool IsBusy => phase != Phase.Idle;
        public bool IsTracking => phase == Phase.Scanning;
        public float Cooldown => cooldown;

        private void Awake()
        {
            line = new GameObject("Scan Beam").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.sharedMaterial = lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
        }

        internal void Begin(Transform requiredMuzzle, Transform requiredTarget, HealthShield requiredTargetHealth,
            DamageSystem requiredDamageSystem, Animator visualAnimator)
        {
            muzzle = requiredMuzzle;
            target = requiredTarget;
            targetHealth = requiredTargetHealth;
            damageSystem = requiredDamageSystem;
            animator = visualAnimator;
            Enter(Phase.Scanning, scanTime);
            SetScanning(true);
            CombatAudioController.Play(CombatSound.EnemyAlert, muzzle.position);
        }

        public void Cancel()
        {
            if (phase == Phase.Idle)
            {
                return;
            }

            phase = Phase.Idle;
            line.enabled = false;
            SetScanning(false);
        }

        private void Update()
        {
            if (phase == Phase.Idle)
            {
                return;
            }

            if (muzzle == null || target == null)
            {
                Cancel();
                return;
            }

            if (phase == Phase.Scanning)
            {
                lockedDirection = (target.position + Vector3.up - muzzle.position).normalized;
            }

            bool firing = phase == Phase.Firing;
            Vector3 end = Trace(out _);
            float flicker = phase == Phase.Scanning ? 0.6f + 0.4f * Mathf.PingPong(Time.time * 12f, 1f) : 1f;
            float width = firing ? beamWidth : phase == Phase.Locked ? scanWidth * 2.5f : scanWidth;
            Color color = firing ? beamColor : phase == Phase.Locked ? lockColor : scanColor;
            line.startWidth = width;
            line.endWidth = firing ? width * 0.6f : width;
            line.startColor = line.endColor = color * flicker;
            line.SetPosition(0, muzzle.position);
            line.SetPosition(1, end);
            line.enabled = true;

            if (Time.time < phaseEndsAt)
            {
                return;
            }

            switch (phase)
            {
                case Phase.Scanning:
                    Enter(Phase.Locked, lockTime);
                    break;
                case Phase.Locked:
                    Fire();
                    break;
                default:
                    Cancel();
                    break;
            }
        }

        private void Fire()
        {
            Enter(Phase.Firing, fireTime);
            SetScanning(false);
            if (animator != null && HasParameter(ShootId))
            {
                animator.SetTrigger(ShootId);
            }
            CombatAudioController.Play(CombatSound.EnemyShot, muzzle.position);

            Vector3 hitPosition = Trace(out HealthShield hit);
            if (hit != null && hit == targetHealth && !hit.IsDead)
            {
                damageSystem.Resolve(hit, new DamageRequest(DamageSource.NormalAttack, Faction.Enemy, damage), hitPosition);
            }
        }

        // The first thing the beam touches, ignoring the enemy itself and trigger volumes.
        private Vector3 Trace(out HealthShield hitHealth)
        {
            hitHealth = null;
            Vector3 origin = muzzle.position;
            int count = beamHits.Cast(origin, lockedDirection, range, ~0, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            RaycastHit nearest = default;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = beamHits[index];
                if (hit.transform.IsChildOf(transform) || hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                nearest = hit;
            }

            if (nearest.collider != null)
            {
                hitHealth = nearest.collider.GetComponentInParent<HealthShield>();
                return nearest.point;
            }

            return origin + lockedDirection * range;
        }

        private void Enter(Phase next, float duration)
        {
            phase = next;
            phaseEndsAt = Time.time + duration;
        }

        private void SetScanning(bool scanning)
        {
            if (animator != null && HasParameter(ScanningId))
            {
                animator.SetBool(ScanningId, scanning);
            }
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

        private void OnDisable() => Cancel();
    }
}
