using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Enemy;

namespace Caffeine.Gameplay.Maze
{
    public enum MazeEndReason { Resolved, NoEffect, Cancelled, TargetLost, OutOfRange }

    // capture effects already applied by combat; presentation must not apply them again
    public readonly struct MazeOutcome
    {
        public readonly OutcomeType Type;
        public readonly int Stage;
        public readonly float HealthRestored, HealthLoss, ShieldLoss;
        public readonly float HealthBefore, HealthAfter, MaxHealth;
        public readonly bool ShieldBroken;
        public readonly float ReductionEndsAt, ControlEndsAt, VulnerabilityEndsAt;

        public MazeOutcome(OutcomeType type, int stage, float healthRestored = 0,
            float healthLoss = 0, float shieldLoss = 0, bool shieldBroken = false,
            float healthBefore = 0, float healthAfter = 0, float maxHealth = 0,
            float reductionEndsAt = 0, float controlEndsAt = 0, float vulnerabilityEndsAt = 0)
        {
            Type = type;
            Stage = stage;
            HealthRestored = healthRestored;
            HealthLoss = healthLoss;
            ShieldLoss = shieldLoss;
            ShieldBroken = shieldBroken;
            HealthBefore = healthBefore;
            HealthAfter = healthAfter;
            MaxHealth = maxHealth;
            ReductionEndsAt = reductionEndsAt;
            ControlEndsAt = controlEndsAt;
            VulnerabilityEndsAt = vulnerabilityEndsAt;
        }
    }

    public sealed class MazeResolution
    {
        public EnemyController Target { get; }
        public HealthShield Player { get; }
        public IReadOnlyList<MazeOutcome> Effects { get; }

        public MazeResolution(EnemyController target, HealthShield player, params MazeOutcome[] effects)
        {
            Target = target;
            Player = player;
            Effects = System.Array.AsReadOnly((MazeOutcome[])effects.Clone());
        }
    }
}
