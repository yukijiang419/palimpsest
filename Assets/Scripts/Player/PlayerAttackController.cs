using System;
using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    public enum SwordMove
    {
        Cleave,
        ReturnCut,
        Finisher,
        Rush,
        RapidCut,
        PrecisionCut
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerAttackController : MonoBehaviour
    {
        private enum AttackPhase
        {
            Idle,
            WaitingForAnimation,
            WaitingForImpact,
            ImpactRequested
        }

        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerAim aim;
        [SerializeField] private PlayerMoveController motor;
        private GameplayConfig.AttackSettings attackConfig;
        private PlayerStamina stamina;
        private AttackPhase phase;
        private SwordMove? bufferedMove;
        private float bufferExpires;
        private float comboExpires;
        private float startedAt;
        private float impactedAt;
        private int nextComboStep;
        private int pulseCount;
        private bool attackBlocked;
        private bool animationEnded;
        private Vector3 attackDirection;
        public event Action AttackRequested;
        public event Action<Vector3> AttackImpactRequested;
        public SwordMove CurrentMove { get; private set; }
        public Vector3 RushStart { get; private set; }
        public Vector3 RushEnd { get; private set; }
        public bool HasPendingAttack => phase != AttackPhase.Idle;
        public bool IsMovementRestricted => HasPendingAttack;
        public float MovementSpeedMultiplier => HasPendingAttack ? attackConfig.AttackMoveSpeedMultiplier : 1f;
        public bool CanCancelIntoSlide => phase == AttackPhase.ImpactRequested;
        public float AttackImpactNormalizedTime => attackConfig.AttackImpactNormalizedTime;
        public int AnimationIndex => CurrentMove == SwordMove.ReturnCut ? 1
            : CurrentMove == SwordMove.Cleave || CurrentMove == SwordMove.Rush ? 0 : 2;
        public GameplayConfig.SwordMoveSettings MoveSettings => attackConfig.Moves[(int)CurrentMove];
        public float AnimationSpeedMultiplier => MoveSettings.AnimationSpeedMultiplier;
        public float DamageMultiplier => MoveSettings.DamageMultiplier;
        public bool IsRangedCut => CurrentMove == SwordMove.PrecisionCut;
        public float RangeMultiplier => MoveSettings.RangeMultiplier;
        public float ArcMultiplier => MoveSettings.ArcMultiplier;
        public string MoveLabel => HasPendingAttack ? CurrentMove switch
        {
            SwordMove.ReturnCut => "II • RETURN CUT",
            SwordMove.Finisher => "III • CRESCENT FINISH",
            SwordMove.Rush => "RUSH SLASH",
            SwordMove.RapidCut => "RAPID CUTS",
            SwordMove.PrecisionCut => "JUDGMENT CUT",
            _ => "I • DRAW SLASH"
        } : "LMB ×3 combo   ·   F rush   ·   LMB ×2 → F rapid cuts   ·   Q judgment cut";

        private void Start()
        {
            attackConfig = ConfigSystem.Instance.Config.Attack;
            if (motor == null)
            {
                motor = GetComponent<PlayerMoveController>();
            }

            stamina = GetComponent<PlayerStamina>();
        }

        private void Update()
        {
            if (attackBlocked || inputReader.IsGameplayBlocked || (motor != null && motor.IsPushing))
            {
                CancelAttack();
                return;
            }

            if (Time.timeScale <= 0f)
            {
                return;
            }

            if (inputReader.PrecisionPressed)
            {
                QueueAttack(SwordMove.PrecisionCut);
            }
            else if (inputReader.RushPressed)
            {
                QueueAttack(nextComboStep == 2 && Time.time <= comboExpires ? SwordMove.RapidCut : SwordMove.Rush);
            }
            else if (inputReader.AttackPressed)
            {
                QueueAttack(Time.time <= comboExpires ? (SwordMove)nextComboStep : SwordMove.Cleave);
            }

            while (phase == AttackPhase.ImpactRequested && CurrentMove == SwordMove.RapidCut
                && pulseCount < MoveSettings.PulseCount
                && Time.time >= impactedAt + pulseCount * MoveSettings.PulseInterval)
            {
                pulseCount++;
                AttackImpactRequested?.Invoke(attackDirection);
                if (pulseCount == MoveSettings.PulseCount && animationEnded)
                {
                    FinishSwing();
                }
            }

            if (bufferedMove.HasValue && Time.time > bufferExpires)
            {
                bufferedMove = null;
            }

            if (bufferedMove.HasValue && (!HasPendingAttack || CanChain))
            {
                SwordMove move = bufferedMove.Value;
                bufferedMove = null;
                TryAttack(move);
            }

            // A missing/disabled animation must never leave gameplay locked.
            if (phase != AttackPhase.Idle && Time.time - startedAt > attackConfig.AnimationTimeout)
            {
                CancelAttack();
            }
        }

        private bool CanChain => phase == AttackPhase.ImpactRequested && Time.time - impactedAt >= MoveSettings.ChainDelay;

        private void QueueAttack(SwordMove move)
        {
            if (TryAttack(move))
            {
                return;
            }

            if (!HasPendingAttack)
            {
                return;
            }

            bufferedMove = move;
            bufferExpires = Time.time + attackConfig.AttackBufferDuration;
        }

        private bool TryAttack(SwordMove move)
        {
            if (attackBlocked || inputReader.IsGameplayBlocked || (HasPendingAttack && !CanChain)
                || (motor != null && (motor.IsSliding || motor.IsPushing)))
            {
                return false;
            }

            Vector3 direction = aim.AimDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            float cost = attackConfig.Moves[(int)move].StaminaCost;
            if (cost > 0f && (stamina == null || !stamina.TrySpend(cost)))
            {
                return false;
            }

            bufferedMove = null;
            animationEnded = false;
            CurrentMove = move;
            attackDirection = direction.normalized;
            startedAt = Time.time;
            nextComboStep = move == SwordMove.Cleave ? 1 : move == SwordMove.ReturnCut ? 2 : 0;
            comboExpires = Time.time + attackConfig.ComboInputWindow;
            phase = AttackPhase.WaitingForAnimation;
            AttackRequested?.Invoke();
            return true;
        }

        public void NotifyAttackAnimationStarted()
        {
            if (phase == AttackPhase.WaitingForAnimation)
            {
                phase = AttackPhase.WaitingForImpact;
            }
        }

        public bool TryRequestAttackImpact()
        {
            if (phase != AttackPhase.WaitingForImpact)
            {
                return false;
            }

            phase = AttackPhase.ImpactRequested;
            impactedAt = Time.time;
            pulseCount = 1;
            if (CurrentMove == SwordMove.Rush)
            {
                RushStart = transform.position;
                motor?.ApplyCombatStep(attackDirection, MoveSettings.StepDistance);
                RushEnd = transform.position;
            }

            AttackImpactRequested?.Invoke(attackDirection);
            return true;
        }

        public void NotifyAttackAnimationEnded()
        {
            if (phase == AttackPhase.Idle)
            {
                return;
            }

            animationEnded = true;
            if (CurrentMove == SwordMove.RapidCut && phase == AttackPhase.ImpactRequested && pulseCount < MoveSettings.PulseCount)
            {
                return;
            }

            FinishSwing();
        }

        private void FinishSwing()
        {
            phase = AttackPhase.Idle;
            comboExpires = Time.time + attackConfig.ComboResetDelay;
        }

        public void SetAttackBlocked(bool value)
        {
            attackBlocked = value;
            if (value)
            {
                CancelAttack();
                motor?.StopMoving();
            }
        }

        public void CancelAttack()
        {
            phase = AttackPhase.Idle;
            bufferedMove = null;
            nextComboStep = 0;
            comboExpires = float.NegativeInfinity;
        }

        public bool TryCancelIntoSlide()
        {
            if (!CanCancelIntoSlide)
            {
                return false;
            }

            CancelAttack();
            return true;
        }

        private void OnDisable() => CancelAttack();
    }
}
