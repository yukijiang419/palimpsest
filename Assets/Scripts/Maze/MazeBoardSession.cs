using System.Collections.Generic;
using Caffeine.Gameplay.Config;

namespace Caffeine.Gameplay.Maze
{
    // track the current path and its collected outcomes
    public sealed class MazeBoardSession
    {
        private readonly List<GridPosition> path = new();
        private readonly int stageOneThreshold;
        private readonly int stageTwoThreshold;
        private OutcomeProgress progress;

        public MazeBoardSession(MazeBoard board, GameplayConfig.MazeSettings settings)
        {
            Board = board;
            stageOneThreshold = settings.StageOneThreshold;
            stageTwoThreshold = settings.StageTwoThreshold;
            path.Add(board.StartPosition);
        }

        public MazeBoard Board { get; }
        public IReadOnlyList<GridPosition> Path => path;
        public GridPosition Cursor => path[^1];
        public OutcomeProgress Progress => progress;
        public int RecoveryStage => progress.GetStage(OutcomeType.Recovery, stageOneThreshold, stageTwoThreshold);
        public int DamageStage => progress.GetStage(OutcomeType.Damage, stageOneThreshold, stageTwoThreshold);
        public int ControlStage => progress.GetStage(OutcomeType.Control, stageOneThreshold, stageTwoThreshold);

        public bool HasVisited(GridPosition position)
        {
            return path.Contains(position);
        }

        public bool TryMove(MoveDirection direction)
        {
            GridPosition next = Offset(Cursor, direction);
            if (!Board.IsInBounds(next))
            {
                return false;
            }

            // moving back one cell undoes the last step
            if (path.Count > 1 && next == path[^2])
            {
                RemoveTail();
                return true;
            }

            // block obstacles and revisiting the rest of the path
            if (Board.GetCell(next) == CellKind.Obstacle || HasVisited(next))
            {
                return false;
            }

            path.Add(next);
            AddContribution(Board.GetCell(next), 1);
            return true;
        }

        // remove the last cell and subtract its contribution
        private void RemoveTail()
        {
            GridPosition tail = Cursor;
            AddContribution(Board.GetCell(tail), -1);
            path.RemoveAt(path.Count - 1);
        }

        // only recovery, damage, and control cells affect progress
        private void AddContribution(CellKind cell, int amount)
        {
            if (MazeRules.TryGetOutcome(cell, out OutcomeType outcome))
            {
                progress = progress.Add(outcome, amount);
            }
        }

        // board coordinates increase to the right and upward
        private static GridPosition Offset(GridPosition position, MoveDirection direction)
        {
            if (direction == MoveDirection.Up)
            {
                return new GridPosition(position.X, position.Y + 1);
            }

            if (direction == MoveDirection.Down)
            {
                return new GridPosition(position.X, position.Y - 1);
            }

            if (direction == MoveDirection.Left)
            {
                return new GridPosition(position.X - 1, position.Y);
            }

            return new GridPosition(position.X + 1, position.Y);
        }
    }
}
