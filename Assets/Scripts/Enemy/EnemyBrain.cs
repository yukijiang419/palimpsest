namespace Caffeine.Gameplay.Enemy
{
    public enum EnemyDecision
    {
        Disabled,
        Approach,
        Retreat,
        Strafe,
        Attack
    }

    public static class EnemyBrain
    {
        // choose the enemy action, state machine
        public static EnemyDecision Evaluate(
            float targetDistance,
            float preferredRangeStart,
            float preferredRangeEnd,
            bool cooldownReady,
            bool isControlled,
            bool canStrafe)
        {
            if (isControlled)
            {
                // stop the controlled enemy
                return EnemyDecision.Disabled;
            }

            if (targetDistance > preferredRangeEnd)
            {
                // find the player
                return EnemyDecision.Approach;
            }

            if (targetDistance < preferredRangeStart)
            {
                // back away from the player
                return EnemyDecision.Retreat;
            }

            // Ranged locomotion continues while the controller handles its firing cooldown.
            if (canStrafe)
            {
                return EnemyDecision.Strafe;
            }

            if (cooldownReady)
            {
                // attack the player
                return EnemyDecision.Attack;
            }

            return EnemyDecision.Disabled;
        }
    }
}
