using UnityEngine;

namespace Caffeine.Gameplay.Puzzles
{
    public sealed class TimelinePuzzlePiece : MonoBehaviour
    {
        public TimelineEra era;
        public PuzzleCondition condition;

        public bool ShouldShow(TimelineEra selected, bool first, bool second, bool west, bool east)
        {
            if (era != selected) return false;
            return condition switch
            {
                PuzzleCondition.BeforeFirst => !first,
                PuzzleCondition.AfterFirst => first,
                PuzzleCondition.BeforeSecond => !second,
                PuzzleCondition.AfterSecond => second,
                PuzzleCondition.West => west && !east,
                PuzzleCondition.East => east,
                _ => true
            };
        }

    }
}
