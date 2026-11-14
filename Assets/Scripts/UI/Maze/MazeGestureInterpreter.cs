using System;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using UnityEngine;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    // turn mouse movement into grid steps
    public sealed class MazeGestureInterpreter : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;

        private float thresholdPixels;
        private Vector2 remainder;
        private bool active;

        public event Action<MoveDirection> DirectionRequested;

        private void Start()
        {
            thresholdPixels = ConfigSystem.Instance.Config.Maze.GestureThreshold;
        }

        private void Update()
        {
            if (!active)
            {
                return;
            }

            // keep leftover movement; a large gesture can request several steps
            remainder += inputReader.PointerDelta;
            while (TryConsumeDirection(out MoveDirection direction))
            {
                DirectionRequested?.Invoke(direction);
            }
        }

        public void SetActive(bool value)
        {
            active = value;
            // discard movement from the previous session
            remainder = Vector2.zero;
        }

        private bool TryConsumeDirection(out MoveDirection direction)
        {
            // consume one threshold on the larger axis, choosing horizontal on a tie
            if (Mathf.Abs(remainder.x) >= Mathf.Abs(remainder.y)
                && Mathf.Abs(remainder.x) >= thresholdPixels)
            {
                float sign = Mathf.Sign(remainder.x);
                remainder.x -= sign * thresholdPixels;
                direction = sign > 0f ? MoveDirection.Right : MoveDirection.Left;
                return true;
            }

            if (Mathf.Abs(remainder.y) >= thresholdPixels)
            {
                float sign = Mathf.Sign(remainder.y);
                remainder.y -= sign * thresholdPixels;
                direction = sign > 0f ? MoveDirection.Up : MoveDirection.Down;
                return true;
            }

            direction = default;
            return false;
        }
    }
}
