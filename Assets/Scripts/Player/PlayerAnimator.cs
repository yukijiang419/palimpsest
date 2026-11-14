using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerAttackController attackController;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveYId = Animator.StringToHash("MoveY");
        private static readonly int SlideStateId = Animator.StringToHash("Base Layer.Slide");
        private static readonly int UpperEmptyStateId = Animator.StringToHash("UpperBody.Empty");
        private static readonly int LocomotionStateId = Animator.StringToHash("Base Layer.Locomotion");
        private static readonly int IdleStateId = Animator.StringToHash("Base Layer.HumanM@Idle01");
        private static readonly int PushStateId = Animator.StringToHash("Base Layer.Push");
        private static readonly int PushSpeedId = Animator.StringToHash("PushSpeed");
        private const float PushBlendDuration = 0.2f;

        // Each combat move owns a state on both the base and upper-body layers.
        private static readonly int[] AttackStateIds =
        {
            Animator.StringToHash("Attack1"), Animator.StringToHash("Attack2"),
            Animator.StringToHash("Attack3"), Animator.StringToHash("Rush"),
            Animator.StringToHash("RapidCut"), Animator.StringToHash("PrecisionCut"),
        };
        private static readonly int[] BaseAttackStateIds =
        {
            Animator.StringToHash("Base Layer.Attack1"), Animator.StringToHash("Base Layer.Attack2"),
            Animator.StringToHash("Base Layer.Attack3"), Animator.StringToHash("Base Layer.Rush"),
            Animator.StringToHash("Base Layer.RapidCut"), Animator.StringToHash("Base Layer.PrecisionCut"),
        };
        private static readonly int[] UpperAttackStateIds =
        {
            Animator.StringToHash("UpperBody.Attack1"), Animator.StringToHash("UpperBody.Attack2"),
            Animator.StringToHash("UpperBody.Attack3"), Animator.StringToHash("UpperBody.Rush"),
            Animator.StringToHash("UpperBody.RapidCut"), Animator.StringToHash("UpperBody.PrecisionCut"),
        };

        // the swing plays on the upper body layer; the base layer only joins in while standing still
        private const int BaseLayer = 0;
        private const int UpperLayer = 1;
        private const float MovingAnimationSpeed = 0.1f;
        private const float AttackFadeDuration = 0.055f;
        private const float HeadBlendDuration = 0.15f;
        private const float NeckShare = 0.5f;
        private const float MaxHeadCorrection = 60f;

        private static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
        private Config.GameplayConfig.AttackSettings attackSettings;
        private float baseAttackSpeed = 1f;
        private float attackCurveNormalization = 1f;
        private float clipLengthNormalization = 1f;
        private bool attackSpeedOverridden;
        private bool attackClipCalibrated;

        private PlayerMoveController motor;
        private WeaponManager weaponManager;
        private bool trackingAttack;
        private bool attackStateEntered;
        private bool upperBodyCleared;
        private int comboStep = -1;
        private int activeMoveIndex;

        private bool wasSliding;
        private bool wasPushing;
        private float pushPoseWeight;
        private float pushYaw;
        private float slidePoseWeight;
        private Vector2 slidePoseDirection;
        private Vector3 visualBasePosition;
        private Quaternion visualBaseRotation;
        private bool visualInitialized;
        private Config.GameplayConfig.PlayerSettings playerSettings;

        private Transform headBone;
        private Transform neckBone;
        private Transform spineBone;
        private Transform upperChestBone;
        private Vector3 headLocalForward = Vector3.forward;
        private Vector3 headLocalUp = Vector3.up;
        private float headStraightenWeight;

        private int CurrentAttackStateId => AttackStateIds[activeMoveIndex];

        private void Awake() => motor = GetComponent<PlayerMoveController>();

        private void Start()
        {
            motor = GetComponent<PlayerMoveController>();
            weaponManager = GetComponentInChildren<WeaponManager>();
            playerSettings = Config.ConfigSystem.Instance.Config.Player;
            attackSettings = Config.ConfigSystem.Instance.Config.Attack;
            visualBasePosition = animator.transform.localPosition;
            visualBaseRotation = animator.transform.localRotation;
            visualInitialized = true;
            CaptureHeadAxes();
        }

        private void OnEnable()
        {
            attackController.AttackRequested += HandleAttack;
            motor.MovementStopped += ResetSlidePose;
        }

        private void OnDisable()
        {
            ResetAttackSpeed();
            attackController.AttackRequested -= HandleAttack;
            motor.MovementStopped -= ResetSlidePose;
            if (trackingAttack)
            {
                attackController.NotifyAttackAnimationEnded();
            }

            trackingAttack = false;
            attackStateEntered = false;
            wasSliding = false;
            slidePoseWeight = 0f;
            headStraightenWeight = 0f;
            if (animator != null && visualInitialized)
            {
                animator.transform.localPosition = visualBasePosition;
                animator.transform.localRotation = visualBaseRotation;
            }
        }

        private void Update()
        {
            if (motor == null || !motor.isActiveAndEnabled)
            {
                ResetAttackSpeed();
                if (!upperBodyCleared)
                {
                    // death leaves the base layer to its own trigger
                    ClearUpperBody(0.1f);
                    trackingAttack = false;
                    attackStateEntered = false;
                }

                return;
            }

            upperBodyCleared = false;
            if (!attackController.HasPendingAttack || motor.IsSliding)
            {
                ResetAttackSpeed();
            }

            // motion is already eased by the motor, so light damping only hides frame jitter
            animator.SetFloat(SpeedId, motor.AnimationSpeed, 0.06f, Time.deltaTime);
            animator.SetFloat(MoveXId, motor.LocalMoveInput.x, 0.05f, Time.deltaTime);
            animator.SetFloat(MoveYId, motor.LocalMoveInput.y, 0.05f, Time.deltaTime);

            if (motor.IsPushing)
            {
                if (!wasPushing)
                {
                    wasPushing = true;
                    ClearUpperBody(PushBlendDuration);
                    animator.CrossFadeInFixedTime(PushStateId, PushBlendDuration, BaseLayer, 0f);
                }
                // Standing still holds the pose instead of walking in place.
                animator.SetFloat(PushSpeedId, motor.PushAnimationSpeed, 0.08f, Time.deltaTime);
                trackingAttack = false;
                attackStateEntered = false;
                return;
            }

            if (wasPushing)
            {
                wasPushing = false;
                ReturnLegsToLocomotion(PushBlendDuration);
            }

            if (motor.IsSliding)
            {
                if (!wasSliding)
                {
                    wasSliding = true;
                    slidePoseDirection = motor.SlideLocalInput;
                    ClearUpperBody(0.04f);
                    animator.CrossFadeInFixedTime(SlideStateId, 0.04f, BaseLayer, 0f);
                }
                trackingAttack = false;
                attackStateEntered = false;
                return;
            }

            if (wasSliding)
            {
                wasSliding = false;
                animator.CrossFadeInFixedTime(motor.CurrentSpeed > 0.1f ? "Base Layer.Locomotion"
                    : "Base Layer.HumanM@Idle01", playerSettings.SlideRecoveryDuration, 0, 0f);
            }

            if (!trackingAttack)
            {
                return;
            }

            if (!attackController.HasPendingAttack)
            {
                ClearUpperBody(0.1f);
                if (IsBaseLayerAttacking())
                {
                    ReturnLegsToLocomotion(0.1f);
                }

                trackingAttack = false;
                attackStateEntered = false;
                return;
            }

            // started moving mid-swing: hand the legs back to locomotion
            if (motor.AnimationSpeed > MovingAnimationSpeed && !animator.IsInTransition(BaseLayer)
                && IsAttackState(animator.GetCurrentAnimatorStateInfo(BaseLayer).shortNameHash))
            {
                ReturnLegsToLocomotion(0.15f);
            }

            if (animator.IsInTransition(UpperLayer))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(UpperLayer);
                if (next.shortNameHash == CurrentAttackStateId)
                {
                    if (!attackClipCalibrated)
                    {
                        CalibrateAttackClip(animator.GetNextAnimatorClipInfo(UpperLayer));
                    }
                    attackStateEntered = true;
                    attackController.NotifyAttackAnimationStarted();
                    ApplyAttackSpeed(next.normalizedTime);
                }
                else if (attackStateEntered)
                {
                    EndSwing();
                }
                return;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(UpperLayer);
            if (state.shortNameHash == CurrentAttackStateId)
            {
                if (!attackClipCalibrated)
                {
                    CalibrateAttackClip(animator.GetCurrentAnimatorClipInfo(UpperLayer));
                }
                attackStateEntered = true;
                attackController.NotifyAttackAnimationStarted();
                ApplyAttackSpeed(state.normalizedTime);
                if (state.normalizedTime >= ImpactNormalizedTime())
                {
                    attackController.TryRequestAttackImpact();
                }

                return;
            }

            if (attackStateEntered)
            {
                EndSwing();
            }
        }

        public void ResetForRespawn()
        {
            ResetAttackSpeed();
            trackingAttack = attackStateEntered = wasSliding = wasPushing = false;
            comboStep = -1;
            slidePoseWeight = pushPoseWeight = headStraightenWeight = 0f;
            animator.transform.localPosition = visualBasePosition;
            animator.transform.localRotation = visualBaseRotation;
            animator.ResetTrigger("Attack");
            animator.ResetTrigger("Death");
            animator.SetFloat(SpeedId, 0f);
            animator.SetFloat(MoveXId, 0f);
            animator.SetFloat(MoveYId, 0f);
            animator.SetFloat(PushSpeedId, 0f);
            animator.Play(IdleStateId, BaseLayer, 0f);
            animator.Play(UpperEmptyStateId, UpperLayer, 0f);
            upperBodyCleared = true;
            animator.Update(0f);
        }

        private void LateUpdate()
        {
            if (motor == null || playerSettings == null)
            {
                return;
            }

            if (!motor.isActiveAndEnabled)
            {
                slidePoseWeight = 0f;
                wasSliding = false;
            }
            float target = motor.isActiveAndEnabled && motor.IsSliding ? 1f : 0f;
            float duration = target > slidePoseWeight ? 0.04f : playerSettings.SlideRecoveryDuration;
            slidePoseWeight = Mathf.MoveTowards(slidePoseWeight, target, Time.deltaTime / duration);
            Vector3 lean = new(slidePoseDirection.y * playerSettings.SlideLeanDegrees, 0f,
                -slidePoseDirection.x * playerSettings.SlideLeanDegrees);
            animator.transform.localPosition = visualBasePosition
                + Vector3.down * (playerSettings.SlideLowering * slidePoseWeight);
            // The root follows the camera, so turn only the model to face the block being pushed.
            bool pushing = motor.isActiveAndEnabled && motor.IsPushing;
            if (pushing)
                pushYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), motor.PushAxis, Vector3.up);
            pushPoseWeight = Mathf.MoveTowards(pushPoseWeight, pushing ? 1f : 0f, Time.deltaTime / PushBlendDuration);
            animator.transform.localRotation = Quaternion.Euler(0f, pushYaw * pushPoseWeight, 0f)
                * visualBaseRotation * Quaternion.Euler(lean * slidePoseWeight);

            bool attacking = motor.isActiveAndEnabled && trackingAttack;
            headStraightenWeight = Mathf.MoveTowards(headStraightenWeight, attacking ? 1f : 0f,
                Time.deltaTime / HeadBlendDuration);
            if (attacking && attackStateEntered && Time.deltaTime > 0f)
            {
                AnimatorStateInfo cut = animator.IsInTransition(UpperLayer)
                    ? animator.GetNextAnimatorStateInfo(UpperLayer)
                    : animator.GetCurrentAnimatorStateInfo(UpperLayer);
                if (cut.shortNameHash == CurrentAttackStateId)
                    ApplyCutPose(Mathf.Clamp01(cut.normalizedTime), ImpactNormalizedTime(), attackController.CurrentMove);
            }
            if (headStraightenWeight > 0f)
            {
                StraightenHead(headStraightenWeight);
            }
        }

        private void CaptureHeadAxes()
        {
            if (!animator.isHuman)
            {
                return;
            }

            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
            upperChestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (headBone == null)
            {
                return;
            }

            // the rig still holds its bind pose here, where the face points along the model forward
            Quaternion inverse = Quaternion.Inverse(headBone.rotation);
            headLocalForward = inverse * animator.transform.forward;
            headLocalUp = inverse * animator.transform.up;
        }

        // Small additive body accents sit on the authored humanoid clips. The source
        // animation continues to own the arms, hands, footwork and sword grip.
        private void ApplyCutPose(float progress, float impact, SwordMove move)
        {
            if (spineBone == null || upperChestBone == null) return;
            float windup = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .14f))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impact - .12f, impact + .03f, progress)));
            float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impact - .07f, impact + .10f, progress))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.74f, .98f, progress)));
            float yaw = move switch
            {
                SwordMove.Cleave => -13f * windup + 18f * follow,
                SwordMove.ReturnCut => 15f * windup - 21f * follow,
                SwordMove.Finisher => -24f * windup + 29f * follow,
                SwordMove.Rush => -8f * windup + 14f * follow,
                SwordMove.RapidCut => 9f * windup - 16f * follow,
                SwordMove.PrecisionCut => -19f * windup + 23f * follow,
                _ => 0f
            };
            float lean = (move == SwordMove.Finisher ? 11f : 6f) * follow
                - 3f * windup;
            Vector3 up = animator.transform.up;
            spineBone.rotation = Quaternion.AngleAxis(yaw * .42f, up) * spineBone.rotation;
            upperChestBone.rotation = Quaternion.AngleAxis(lean, animator.transform.right)
                * Quaternion.AngleAxis(yaw * .58f, up) * upperChestBone.rotation;
        }

        // the combo clips turn the head toward a side target; keep the gaze on the character forward
        private void StraightenHead(float weight)
        {
            if (headBone == null)
            {
                return;
            }

            Quaternion correction = HeadCorrection();
            // a large error means the captured axes do not fit this pose, so leave the clip alone
            if (Quaternion.Angle(Quaternion.identity, correction) > MaxHeadCorrection)
            {
                return;
            }

            if (neckBone != null)
            {
                neckBone.rotation = Quaternion.Slerp(Quaternion.identity, correction, weight * NeckShare)
                    * neckBone.rotation;
                correction = HeadCorrection();
            }

            headBone.rotation = Quaternion.Slerp(Quaternion.identity, correction, weight) * headBone.rotation;
        }

        // removes turn and tilt but keeps the clip's nod
        private Quaternion HeadCorrection()
        {
            Transform root = animator.transform;
            Vector3 face = headBone.rotation * headLocalForward;
            Vector3 faceUp = headBone.rotation * headLocalUp;
            float pitch = Vector3.SignedAngle(Vector3.ProjectOnPlane(face, root.right), root.forward, root.right);
            Quaternion desired = Quaternion.AngleAxis(-pitch, root.right) * Quaternion.LookRotation(root.forward, root.up);
            return desired * Quaternion.Inverse(Quaternion.LookRotation(face, faceUp));
        }

        private void ResetSlidePose()
        {
            ResetAttackSpeed();
            slidePoseWeight = 0f;
            if (!visualInitialized)
            {
                return;
            }

            animator.transform.localPosition = visualBasePosition;
            animator.transform.localRotation = visualBaseRotation;
        }

        private float ImpactNormalizedTime()
        {
            var move = attackController.MoveSettings;
            if (move.ImpactNormalizedTime > 0f)
            {
                return move.ImpactNormalizedTime;
            }
            float[] times = weaponManager != null && weaponManager.Current != null
                && weaponManager.Current.comboImpactTimes is { Length: > 0 } weaponTimes
                ? weaponTimes : attackSettings?.ComboImpactNormalizedTimes;
            if (times != null && comboStep >= 0 && comboStep < times.Length)
            {
                return times[comboStep];
            }
            if (weaponManager != null && weaponManager.Current != null
                && weaponManager.Current.isTwoHanded && move.TwoHandImpactNormalizedTime > 0f)
            {
                return move.TwoHandImpactNormalizedTime;
            }
            return attackController.AttackImpactNormalizedTime;
        }

        private void CalibrateAttackClip(AnimatorClipInfo[] clips)
        {
            if (clips.Length == 0 || clips[0].clip == null)
            {
                return;
            }

            // Long retargeted finishers must complete before the gameplay watchdog, while
            // one-handed clips keep their original cadence.
            clipLengthNormalization = Mathf.Max(1f, clips[0].clip.length / 1.1f);
            attackClipCalibrated = true;
        }

        private float EvaluateAttackSpeed(float progress)
        {
            AnimationCurve curve = attackSettings?.AttackSpeedCurve;
            float baseSpeed = curve == null || curve.length == 0 ? 1f
                : Mathf.Clamp(curve.Evaluate(Mathf.Clamp01(progress)), 0.1f, 3f);
            float impact = ImpactNormalizedTime();
            float snap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impact - .12f, impact, progress))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impact + .04f, impact + .19f, progress)));
            float anticipation = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.05f, impact - .13f, progress));
            float accent = attackController.CurrentMove == SwordMove.Finisher ? .40f : .26f;
            return baseSpeed * (1f - .14f * anticipation + accent * snap);
        }

        private void ApplyAttackSpeed(float progress)
        {
            if (attackSpeedOverridden)
            {
                animator.SetFloat(AttackSpeedId,
                    baseAttackSpeed * attackController.AnimationSpeedMultiplier
                    * attackCurveNormalization * clipLengthNormalization
                    * EvaluateAttackSpeed(progress));
            }
        }

        private void ResetAttackSpeed()
        {
            if (!attackSpeedOverridden)
            {
                return;
            }

            if (animator != null)
            {
                animator.SetFloat(AttackSpeedId, baseAttackSpeed);
            }

            attackSpeedOverridden = false;
            clipLengthNormalization = 1f;
            attackClipCalibrated = false;
        }

        private void HandleAttack()
        {
            ResetAttackSpeed();
            comboStep = attackController.AnimationIndex;
            activeMoveIndex = (int)attackController.CurrentMove;
            baseAttackSpeed = Mathf.Max(0.01f, animator.GetFloat(AttackSpeedId));
            // integrate reciprocal speed to keep the attack duration close to its original value
            attackCurveNormalization = 0f;
            const int samples = 64;
            for (int i = 0; i < samples; i++)
            {
                attackCurveNormalization += 1f / EvaluateAttackSpeed((i + 0.5f) / samples);
            }

            attackCurveNormalization /= samples;
            attackSpeedOverridden = true;
            clipLengthNormalization = 1f;
            attackClipCalibrated = false;
            ApplyAttackSpeed(0f);

            // Gameplay owns combo selection so hit properties and animation always agree.
            trackingAttack = true;
            attackStateEntered = false;
            animator.CrossFadeInFixedTime(UpperAttackStateIds[activeMoveIndex], AttackFadeDuration, UpperLayer, 0f);
            if (motor.AnimationSpeed <= MovingAnimationSpeed)
            {
                // standing still keeps the full-body swing and its footwork
                animator.CrossFadeInFixedTime(BaseAttackStateIds[activeMoveIndex], AttackFadeDuration, BaseLayer, 0f);
            }
        }

        private void EndSwing()
        {
            ResetAttackSpeed();
            attackController.NotifyAttackAnimationEnded();
            trackingAttack = false;
            attackStateEntered = false;
        }

        private void ClearUpperBody(float fadeDuration)
        {
            animator.CrossFadeInFixedTime(UpperEmptyStateId, fadeDuration, UpperLayer, 0f);
            upperBodyCleared = true;
        }

        private static bool IsAttackState(int shortNameHash)
        {
            return System.Array.IndexOf(AttackStateIds, shortNameHash) >= 0;
        }

        private bool IsBaseLayerAttacking()
        {
            return IsAttackState(animator.GetCurrentAnimatorStateInfo(BaseLayer).shortNameHash)
                || (animator.IsInTransition(BaseLayer)
                    && IsAttackState(animator.GetNextAnimatorStateInfo(BaseLayer).shortNameHash));
        }

        private void ReturnLegsToLocomotion(float fadeDuration)
        {
            animator.CrossFadeInFixedTime(motor.AnimationSpeed > MovingAnimationSpeed
                ? LocomotionStateId : IdleStateId, fadeDuration, BaseLayer, 0f);
        }
    }
}
