using System;
using System.Collections.Generic;

namespace Caffeine.Gameplay.Quests
{
    public enum QuestStage { Available, Travelling, ReturnToMerchant, Completed }

    // keep run progress separate from the shared definition asset
    public sealed class QuestProgress
    {
        private static readonly Dictionary<string, QuestProgress> runProgress = new(StringComparer.Ordinal);

        public static QuestProgress ForRun(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                throw new ArgumentException("A quest ID is required.", nameof(questId));
            }

            if (!runProgress.TryGetValue(questId, out var progress))
            {
                runProgress.Add(questId, progress = new QuestProgress());
            }

            return progress;
        }

        public static void ResetRun() => runProgress.Clear();

        public QuestStage Stage { get; private set; }
        public bool OverviewShown { get; internal set; }

        public bool Accept()
        {
            if (Stage != QuestStage.Available)
            {
                return false;
            }

            Stage = QuestStage.Travelling;
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            return true;
        }

        public bool Arrive()
        {
            if (Stage != QuestStage.Travelling)
            {
                return false;
            }

            Stage = QuestStage.ReturnToMerchant;
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            return true;
        }

        public bool Complete()
        {
            if (Stage != QuestStage.ReturnToMerchant)
            {
                return false;
            }

            Stage = QuestStage.Completed;
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            return true;
        }
        [Serializable] public sealed class SaveState { public string id; public QuestStage stage; public bool overview; }
        public static SaveState[] CaptureState()
        {
            var states = new List<SaveState>();
            foreach (var pair in runProgress) states.Add(new SaveState { id = pair.Key, stage = pair.Value.Stage, overview = pair.Value.OverviewShown });
            return states.ToArray();
        }
        public static void RestoreState(SaveState[] states)
        {
            ResetRun();
            foreach (var s in states) { var p = ForRun(s.id); p.Stage = s.stage; p.OverviewShown = s.overview; }
        }

    }
}
