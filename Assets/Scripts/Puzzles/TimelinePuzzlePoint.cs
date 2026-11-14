using UnityEngine;

namespace Caffeine.Gameplay.Puzzles
{
    public sealed class TimelinePuzzlePoint : MonoBehaviour
    {
        public TimelineEra fromEra;
        public TimelineEra toEra;
        public PuzzleAction action;
        public PuzzleCondition condition;
        public string prompt;
        public float radius = 2.5f;
    }
}
