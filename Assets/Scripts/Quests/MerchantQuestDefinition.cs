using System;
using UnityEngine;

namespace Caffeine.Gameplay.Quests
{
    [CreateAssetMenu(menuName = "Caffeine/Merchant Quest")]
    public sealed class MerchantQuestDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class PuzzleStep
        {
            [Range(1, 3)] public int puzzleIndex;
            public string title;
            public string displayName;
            public string sceneName;
            public WeaponData rewardWeapon;
            [TextArea(3, 8)] public string archiveRecord;
        }

        [field: SerializeField] public string QuestId { get; private set; }
        public string speaker = "Merchant";
        public string title = "Scout the courtyard";
        public string destinationId = "c1-entrance";
        [SerializeReference] public PuzzleStep Puzzle;
        public bool HasPuzzle => Puzzle != null;
        [Min(0)] public int coinReward = 25;
        [Min(0.5f)] public float arrivalRadius = 3f;
        [TextArea(2, 6)] public string[] offer;
        [TextArea(2, 6)] public string[] inProgress;
        [TextArea(2, 6)] public string[] readyToTurnIn;
        [TextArea(2, 6)] public string[] completed;

        public string ArchiveRecord(int index)
        {
            return Puzzle != null && Puzzle.puzzleIndex == index ? Puzzle.archiveRecord : null;
        }

        public bool TryValidate(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(QuestId) || !HasPuzzle && string.IsNullOrWhiteSpace(destinationId))
            {
                error = $"Quest {name} needs a quest ID and destination ID.";
            }
            else if (coinReward < 0 || !HasPuzzle && arrivalRadius <= 0)
            {
                error = $"Quest {QuestId} needs a non-negative reward and positive arrival radius.";
            }
            else if (!HasLines(offer) || !HasLines(inProgress) || !HasLines(readyToTurnIn) || !HasLines(completed))
            {
                error = $"Quest {QuestId} needs dialogue lines for every stage.";
            }

            if (error == null && HasPuzzle)
            {
                if (Puzzle.puzzleIndex < 1 || Puzzle.puzzleIndex > 3
                    || string.IsNullOrWhiteSpace(Puzzle.title) || string.IsNullOrWhiteSpace(Puzzle.sceneName)
                    || Puzzle.rewardWeapon == null || Puzzle.rewardWeapon.slot < 1 || Puzzle.rewardWeapon.slot > 2)
                {
                    error = $"Quest {QuestId} needs a named puzzle from 1 to 3, a scene and a reward weapon.";
                }
            }
            return error == null;
        }

        private static bool HasLines(string[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return false;
            }

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return false;
                }
            }

            return true;
        }

    }
}
