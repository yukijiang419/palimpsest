using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Player;
using UnityEngine;

namespace Caffeine.Gameplay.Puzzles
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PuzzleBlockPusher : MonoBehaviour
    {
        private GameplayConfig.PuzzlePushSettings Settings => ConfigSystem.Instance.Config.PuzzlePush;

        private PuzzleMovableBlock heldBlock;
        private Rigidbody heldBody;
        private Vector3 heldOffset;
        private Vector3 pushAxis;
        private float halfDepth;
        private float reach;
        private Collider heldCollider;
        private CharacterController body;
        private PlayerMoveController motor;
        private PlayerAttackController attacks;
        private WeaponManager weapons;
        private PlayerInputReader input;
        private Transform leftHand;
        private Transform rightHand;
        private bool aligning;
        private bool weaponStowed;
        public bool IsGripping => heldBlock != null;

        private void Awake()
        {
            motor = GetComponent<PlayerMoveController>();
            body = GetComponent<CharacterController>();
            attacks = GetComponent<PlayerAttackController>();
            input = GetComponent<PlayerInputReader>();
            weapons = GetComponentInChildren<WeaponManager>();
            var animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }

        private void Update()
        {
            UpdateGrip();
            // Put the weapon away whenever a block is within reach so the blade never cuts into it.
            bool stow = heldBlock != null || Time.timeScale > 0f && FindGrippable() != null;
            if (stow != weaponStowed)
            {
                weaponStowed = stow;
                weapons?.SetModelHidden(stow);
            }
        }

        private void UpdateGrip()
        {
            if (Time.timeScale == 0f || input.IsGameplayBlocked || !input.GripHeld)
            {
                ReleaseGrip();
                return;
            }

            if (heldBlock != null)
            {
                if (!heldBlock.isActiveAndEnabled || !heldBlock.CanPush || Vector3.Distance(transform.position, heldBlock.transform.position) > Settings.ReleaseDistance)
                {
                    ReleaseGrip();
                }
                else if (aligning)
                {
                    StepToBlockFace();
                }

                return;
            }

            if (input.GripPressed)
            {
                TryGripNearest();
            }
        }

        // Runs after the Animator has posed the hands: never let a palm sink past the block face.
        private void LateUpdate()
        {
            if (heldBlock == null)
            {
                return;
            }

            float handReach = Mathf.Max(HandReach(leftHand), HandReach(rightHand)) + Settings.PalmThickness;
            if (handReach <= reach)
            {
                return;
            }

            reach = handReach;
            heldOffset = pushAxis * (halfDepth + reach);
        // After the initial approach, let the block follow the hands. Re-aligning
        // the body here would undo the motor's push/pull movement every frame.
        }

        private float HandReach(Transform hand)
        {
            if (hand == null)
            {
                return 0f;
            }

            Vector3 offset = hand.position - transform.position;
            offset.y = 0f;
            return Vector3.Dot(offset, pushAxis);
        }

        // Ease into the pushing spot: centred on the face, at arm's length.
        private void StepToBlockFace()
        {
            Vector3 stand = heldBody.position - heldOffset;
            Vector3 delta = stand - transform.position;
            delta.y = 0f;
            if (delta.magnitude < .02f)
            {
                aligning = false;
                motor?.BeginPush(pushAxis, Settings.PushWalkSpeed);
                return;
            }

            body.Move(Vector3.ClampMagnitude(delta, Settings.AlignSpeed * Time.deltaTime));
        }

        public PuzzleMovableBlock FindGrippable()
        {
            Vector3 centre = transform.position + Vector3.up * Settings.SearchHeight;
            float bestDistance = float.PositiveInfinity;
            PuzzleMovableBlock best = null;
            foreach (var collider in Physics.OverlapSphere(centre, Settings.SearchRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var block = collider.GetComponent<PuzzleMovableBlock>();
                if (block == null || !block.CanPush)
                {
                    continue;
                }

                Vector3 towards = block.transform.position - transform.position;
                towards.y = 0f;
                if (Vector3.Dot(transform.forward, towards.normalized) < Settings.FacingThreshold)
                {
                    continue;
                }

                float distance = Vector3.Distance(centre, collider.ClosestPoint(centre));
                if (distance > Settings.GripDistance || distance >= bestDistance)
                {
                    continue;
                }

                best = block;
                bestDistance = distance;
            }

            return best;
        }

        public bool TryGripNearest()
        {
            if (heldBlock != null || attacks != null && attacks.HasPendingAttack)
            {
                return false;
            }

            var best = FindGrippable();
            if (best == null)
            {
                return false;
            }

            heldBlock = best;
            heldBody = best.GetComponent<Rigidbody>();
            Vector3 towards = heldBody.position - transform.position;
            towards.y = 0f;
            pushAxis = PushAxis(best.transform, towards);
            // The player would otherwise stand against the block and never move it.
            heldCollider = best.GetComponent<Collider>();
            if (heldCollider != null)
            {
                Physics.IgnoreCollision(body, heldCollider, true);
            }

            Vector3 extents = heldCollider != null ? heldCollider.bounds.extents : Vector3.one * .5f;
            halfDepth = Mathf.Abs(pushAxis.x) * extents.x + Mathf.Abs(pushAxis.z) * extents.z;
            // Never closer than the capsule itself, so the body cannot overlap the block either.
            reach = Mathf.Max(Settings.MinReach, body.radius + Settings.BodyClearance);
            heldOffset = pushAxis * (halfDepth + reach);
            aligning = true;
            motor?.BeginPush(pushAxis, 0f);
            return true;
        }

        // Push along whichever side of the block the player is facing.
        private static Vector3 PushAxis(Transform block, Vector3 towards)
        {
            Vector3 right = Vector3.ProjectOnPlane(block.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(block.forward, Vector3.up).normalized;
            float x = Vector3.Dot(towards, right);
            float z = Vector3.Dot(towards, forward);
            return Mathf.Abs(x) > Mathf.Abs(z) ? right * Mathf.Sign(x) : forward * Mathf.Sign(z);
        }

        private void FixedUpdate()
        {
            if (heldBlock == null || !heldBlock.CanPush || heldBody == null || aligning)
            {
                return;
            }

            Vector3 target = transform.position + heldOffset;
            target.y = heldBody.position.y;
            Vector3 error = target - heldBody.position;
            if (error.magnitude > Settings.StuckDistance)
            {
                ReleaseGrip();
                return;
            }

            heldBody.linearVelocity = Vector3.ClampMagnitude(error * Settings.FollowGain, Settings.GripSpeed);
        }

        public void ReleaseGrip()
        {
            if (heldBody != null && !heldBody.isKinematic)
            {
                heldBody.linearVelocity = Vector3.zero;
            }

            if (heldCollider != null)
            {
                Physics.IgnoreCollision(body, heldCollider, false);
            }

            heldCollider = null;
            if (heldBlock != null)
            {
                motor?.EndPush();
            }

            aligning = false;
            heldBlock = null;
            heldBody = null;
        }

        private void OnDisable() => ReleaseGrip();
        public void ResetForRespawn()
        {
            ReleaseGrip();
            weaponStowed = false;
            weapons?.SetModelHidden(false);
        }
    }
}
