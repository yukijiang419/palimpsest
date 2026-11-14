using System;

namespace Caffeine.Gameplay.Maze
{
    // integer coordinates used to compare and locate board cells
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(GridPosition left, GridPosition right) => left.Equals(right);
        public static bool operator !=(GridPosition left, GridPosition right) => !left.Equals(right);
    }

    public enum MoveDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    // board cells include empty space, the start, obstacles, and effect tokens
    public enum CellKind
    {
        Empty,
        Start,
        Obstacle,
        Recovery,
        Damage,
        Control
    }

    public enum OutcomeType
    {
        Recovery,
        Damage,
        Control
    }

    // counts of each effect token on the current path
    public readonly struct OutcomeProgress
    {
        public OutcomeProgress(int recovery, int damage, int control)
        {
            Recovery = recovery;
            Damage = damage;
            Control = control;
        }

        public int Recovery { get; }
        public int Damage { get; }
        public int Control { get; }

        public int GetCount(OutcomeType outcome)
        {
            if (outcome == OutcomeType.Recovery)
            {
                return Recovery;
            }

            return outcome == OutcomeType.Damage ? Damage : Control;
        }

        // stage zero has no effect; stages one and two use the configured thresholds
        public int GetStage(OutcomeType outcome, int stageOneThreshold, int stageTwoThreshold)
        {
            int count = GetCount(outcome);
            return count >= stageTwoThreshold ? 2 : count >= stageOneThreshold ? 1 : 0;
        }

        // return updated counts; a negative amount removes a path contribution
        public OutcomeProgress Add(OutcomeType outcome, int amount)
        {
            if (outcome == OutcomeType.Recovery)
            {
                return new OutcomeProgress(Recovery + amount, Damage, Control);
            }

            if (outcome == OutcomeType.Damage)
            {
                return new OutcomeProgress(Recovery, Damage + amount, Control);
            }

            return new OutcomeProgress(Recovery, Damage, Control + amount);
        }
    }

    // store the board layout separately from the player path
    public sealed class MazeBoard
    {
        private readonly CellKind[] cells;

        public MazeBoard(int width, int height, CellKind[] sourceCells, GridPosition startPosition)
        {
            Width = width;
            Height = height;
            cells = sourceCells;
            StartPosition = startPosition;
        }

        public int Width { get; }
        public int Height { get; }
        public GridPosition StartPosition { get; }

        public bool IsInBounds(GridPosition position)
        {
            return position.X >= 0 && position.X < Width
                && position.Y >= 0 && position.Y < Height;
        }

        // convert board coordinates to the flat cell array index
        public CellKind GetCell(GridPosition position)
        {
            return cells[position.Y * Width + position.X];
        }
    }

    internal static class MazeRules
    {
        // empty, start, and obstacle cells do not give an outcome
        internal static bool TryGetOutcome(CellKind cell, out OutcomeType outcome)
        {
            if (cell == CellKind.Recovery)
            {
                outcome = OutcomeType.Recovery;
                return true;
            }

            if (cell == CellKind.Damage)
            {
                outcome = OutcomeType.Damage;
                return true;
            }

            if (cell == CellKind.Control)
            {
                outcome = OutcomeType.Control;
                return true;
            }

            outcome = default;
            return false;
        }

        // use a triangle for recovery, a diamond for damage, and a pentagon for control
        internal static int GetSideCount(OutcomeType outcome)
        {
            if (outcome == OutcomeType.Recovery)
            {
                return 3;
            }

            return outcome == OutcomeType.Damage ? 4 : 5;
        }
    }
}
