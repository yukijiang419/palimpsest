using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Quests;
using Caffeine.Gameplay.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.Puzzles
{
    // Run-only progress shared by the campus and the three self-contained puzzle scenes.
    public static class PuzzleRunState
    {
        // Index 4 is the Boss arena: entered like a puzzle once puzzle 3 is done, but it has no reward or return trip.
        public const int BossIndex = 4;
        public static bool BossUnlocked { get; private set; }
        public static bool BossDefeated { get; private set; }

        internal static bool AuthorizeBoss()
        {
            if (!AllPuzzlesSolved || !IsUnlocked(1) || !IsUnlocked(2) || !IsUnlocked(3) || BossUnlocked) return false;
            BossUnlocked = true;
            return true;
        }

        internal static bool CompleteBoss()
        {
            if (BossDefeated) return false;
            BossDefeated = true;
            return true;
        }
        private static Vector3 returnPosition;
        private static Quaternion returnRotation = Quaternion.identity;
        private static bool hasReturnPosition;
        private static int jumpIndex;
        private static float health;
        private static float shield;
        private static int coins;
        private static bool hasVitals;
        // Shop upgrades live on the player, which is rebuilt by every scene load
        private static float bonusMaxHealth;
        private static float bonusMaxStamina;
        private static float bonusRecovery;
        private static float bonusControlDuration;
        private static float[] weaponDamageBonus;
        private static string arrivalMessage;

        public static bool HasPendingArrival { get; private set; }
        public static void CancelPendingArrival() => HasPendingArrival = false;
        public static bool HasTravelled { get; private set; }
        public static int PreferredSlot { get; private set; } = 1;
        private static bool puzzleTutorialShown;
        public static bool CombatTutorialShown { get; private set; }
        public static void CompleteCombatTutorial() => CombatTutorialShown = true;

        // The puzzle guide opens automatically on the first puzzle of each run only.
        public static bool TryShowPuzzleTutorial()
        {
            if (puzzleTutorialShown) return false;
            puzzleTutorialShown = true;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetRun()
        {
            QuestProgress.ResetRun();
            BossUnlocked = BossDefeated = false;
            Caffeine.Gameplay.Enemy.CampusEncounterDirector.ResetRun();
            HasPendingArrival = false;
            HasTravelled = false;
            hasVitals = false;
            health = shield = 0f;
            coins = 0;
            bonusMaxHealth = bonusMaxStamina = 0f;
            bonusRecovery = bonusControlDuration = 0f;
            weaponDamageBonus = null;
            PreferredSlot = 1;
            puzzleTutorialShown = false;
            CombatTutorialShown = false;
            arrivalMessage = null;
            returnPosition = Vector3.zero;
            returnRotation = Quaternion.identity;
            hasReturnPosition = false;
            jumpIndex = 0;
        }

        private static QuestStage Stage(int index)
        {
            if (index > 0 && index < BossIndex && ConfigSystem.Instance != null
                && ConfigSystem.Instance.Config.Quests.TryGetPuzzleDefinition(index, out var definition))
                return QuestProgress.ForRun(definition.QuestId).Stage;
            return QuestStage.Available;
        }

        public static bool IsUnlocked(int index) => Stage(index) == QuestStage.Completed;
        public static bool HasWeaponReward(int index) => IsUnlocked(index);
        public static bool IsSolved(int index) => Stage(index) is QuestStage.ReturnToMerchant or QuestStage.Completed;
        public static bool CanEnter(int index)
        {
            if (index == BossIndex) return BossUnlocked && !BossDefeated;
            if (index <= 0 || index >= BossIndex || IsSolved(index) || ConfigSystem.Instance == null)
                return false;
            if (!ConfigSystem.Instance.Config.Quests.TryGetPuzzleDefinition(index, out var definition)) return false;
            var progress = QuestProgress.ForRun(definition.QuestId);
            return (index == 1 || IsUnlocked(index - 1)) && progress.Stage == QuestStage.Travelling;
        }

        public static bool AllPuzzlesSolved => IsSolved(1) && IsSolved(2) && IsSolved(3);

        // e.g. "1 · Courtyard Wall Gap\n3 · Main Intersection Bridge"
        public static string UnsolvedPuzzles()
        {
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 1; i < BossIndex; i++)
                if (!IsSolved(i) && ConfigSystem.Instance.Config.Quests.TryGetPuzzleDefinition(i, out var definition))
                    lines.Add(i + " · " + definition.Puzzle.displayName);
            return string.Join("\n", lines);
        }
        public static WeaponData RewardWeapon(int index) => ConfigSystem.Instance != null
            && ConfigSystem.Instance.Config.Quests.TryGetPuzzleDefinition(index, out var definition)
            ? definition.Puzzle.rewardWeapon : null;

        public static string RewardName(int index) => RewardWeapon(index)?.weaponName ?? "";

        public static void Enter(int index, PlayerInputReader player)
        {
            if (!CanEnter(index) || player == null || HasPendingArrival) return;
            Capture(player);
            SaveReturnPosition(player);
            jumpIndex = 0;
            arrivalMessage = null;
            HasTravelled = HasPendingArrival = true;
            var quests = ConfigSystem.Instance.Config.Quests;
            Caffeine.Gameplay.Persistence.SaveGameService.Instance.StartCoroutine(Caffeine.Gameplay.Persistence.SaveGameService.Instance.Travel(index == BossIndex ? quests.BossSceneName
                : quests.TryGetPuzzleDefinition(index, out var definition) ? definition.Puzzle.sceneName
                : throw new System.InvalidOperationException("Puzzle scene configuration is missing.")));
        }

        public static bool TryJumpTo(int index, PlayerInputReader player)
        {
            if (index < 1 || index > BossIndex || player == null || HasPendingArrival
                || ConfigSystem.Instance == null) return false;
            var quests = ConfigSystem.Instance.Config.Quests;
            MerchantQuestDefinition definition = null;
            if (index != BossIndex && !quests.TryGetPuzzleDefinition(index, out definition)) return false;
            string sceneName = index == BossIndex ? quests.BossSceneName : definition.Puzzle.sceneName;
            if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName)) return false;
            var prerequisites = new MerchantQuestDefinition[index - 1];
            for (int i = 1; i < index; i++)
                if (!quests.TryGetPuzzleDefinition(i, out prerequisites[i - 1])) return false;

            Capture(player);
            SaveReturnPosition(player);
            // Selecting a later level bypasses both the earlier tasks and their weapon gates.
            // No coin rewards are issued by navigation.
            foreach (var prerequisite in prerequisites)
            {
                var progress = QuestProgress.ForRun(prerequisite.QuestId);
                progress.Accept();
                progress.Arrive();
                progress.Complete();
            }
            if (definition != null) QuestProgress.ForRun(definition.QuestId).Accept();
            if (index == BossIndex) AuthorizeBoss();
            jumpIndex = index;
            arrivalMessage = null;
            HasTravelled = HasPendingArrival = true;
            Caffeine.Gameplay.Persistence.SaveGameService.Instance.StartCoroutine(Caffeine.Gameplay.Persistence.SaveGameService.Instance.Travel(sceneName));
            return true;
        }

        public static bool TryGetSkippablePuzzle(out int index)
        {
            index = 0;
            if (HasPendingArrival || ConfigSystem.Instance == null) return false;
            var quests = ConfigSystem.Instance.Config.Quests;
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != "StartScene")
            {
                for (int i = 1; i < BossIndex; i++)
                    if (quests.TryGetPuzzleDefinition(i, out var definition)
                        && definition.Puzzle.sceneName == sceneName && Stage(i) == QuestStage.Travelling)
                    {
                        index = i;
                        return true;
                    }
                return false;
            }

            // Match the merchant: collect pending rewards before moving on to another task.
            for (int i = 1; i < BossIndex; i++)
                if (Stage(i) == QuestStage.ReturnToMerchant) return false;
            for (int i = 1; i < BossIndex; i++)
            {
                if (Stage(i) == QuestStage.Completed) continue;
                if (Stage(i) != QuestStage.Travelling) return false;
                index = i;
                return true;
            }
            return false;
        }

        public static bool TrySkipCurrent(PlayerInputReader player)
        {
            if (player == null || player.GetComponent<HealthShield>()?.IsDead == true
                || !TryGetSkippablePuzzle(out int index)) return false;
            if (SceneManager.GetActiveScene().name != "StartScene")
            {
                if (!hasReturnPosition) return false;
                return Finish(index, player, true);
            }

            var merchant = Object.FindFirstObjectByType<Merchant>();
            if (merchant == null || merchant.PuzzleReturnPoint == null) return false;
            var quests = ConfigSystem.Instance.Config.Quests;
            if (!quests.TryGetPuzzleDefinition(index, out var definition)
                || !QuestProgress.ForRun(definition.QuestId).Arrive()) return false;
            SaveReturnPosition(player);
            jumpIndex = 0;
            PlacePlayerAtReturnPosition(player);
            return true;
        }

        private static void SaveReturnPosition(PlayerInputReader player)
        {
            if (SceneManager.GetActiveScene().name != "StartScene") return;
            var merchant = Object.FindFirstObjectByType<Merchant>();
            var returnPoint = merchant != null && merchant.PuzzleReturnPoint != null
                ? merchant.PuzzleReturnPoint : player.transform;
            returnPosition = returnPoint.position;
            returnRotation = returnPoint.rotation;
            hasReturnPosition = true;
        }

        public static bool Finish(int index, PlayerInputReader player) => Finish(index, player, false);

        private static bool Finish(int index, PlayerInputReader player, bool skipped)
        {
            if (index <= 0 || index >= BossIndex || player == null || HasPendingArrival) return false;
            if (!HasTravelled || ConfigSystem.Instance == null) return false;
            if (!ConfigSystem.Instance.Config.Quests.TryGetPuzzleDefinition(index, out var definition)) return false;
            var progress = QuestProgress.ForRun(definition.QuestId);
            if (IsSolved(index) && jumpIndex != index) return false;
            if (!Application.CanStreamedLevelBeLoaded("StartScene")) return false;
            if (!IsSolved(index) && !progress.Arrive()) return false;
            Capture(player);
            arrivalMessage = progress.Stage == QuestStage.ReturnToMerchant
                ? (skipped ? "Level skipped" : "Record recovered")
                    + " — press E to speak to the merchant and collect " + RewardName(index)
                : "Puzzle replay complete";
            jumpIndex = 0;
            HasTravelled = HasPendingArrival = true;
            Caffeine.Gameplay.Persistence.SaveGameService.Instance.StartCoroutine(Caffeine.Gameplay.Persistence.SaveGameService.Instance.Travel("StartScene"));
            return true;
        }

        public static bool ClaimReward(QuestProgress progress, int index)
        {
            if (index <= 0 || index >= BossIndex || !IsSolved(index) || IsUnlocked(index)
                || !ConfigSystem.Instance.Config.Quests.TryGetPuzzleDefinition(index, out var definition)
                || progress != QuestProgress.ForRun(definition.QuestId) || !progress.Complete()) return false;
            PreferredSlot = definition.Puzzle.rewardWeapon.slot;
            return true;
        }

        private static void Capture(PlayerInputReader player)
        {
            var vitals = player.GetComponent<HealthShield>();
            hasVitals = vitals != null && vitals.IsInitialized;
            if (hasVitals) { health = vitals.CurrentHealth; shield = vitals.CurrentShield; }
            bonusMaxHealth = hasVitals ? vitals.BonusMaxHealth : 0f;
            coins = player.GetComponent<PlayerWallet>().Coins;
            var stamina = player.GetComponent<PlayerStamina>();
            bonusMaxStamina = stamina != null ? stamina.BonusMaxStamina : 0f;
            var maze = Object.FindFirstObjectByType<MazeSystem>();
            bonusRecovery = maze != null ? maze.BonusRecovery : 0f;
            bonusControlDuration = maze != null ? maze.BonusControlDuration : 0f;
            var weapons = player.GetComponentInChildren<WeaponManager>();
            if (weapons != null && weapons.Current != null) PreferredSlot = weapons.Current.slot;
            weaponDamageBonus = null;
            if (weapons != null)
            {
                weaponDamageBonus = new float[weapons.WeaponCount];
                for (int i = 0; i < weaponDamageBonus.Length; i++) weaponDamageBonus[i] = weapons.GetDamageBonus(i);
            }
        }

        public static void PlaceReturningPlayer(PlayerInputReader player)
        {
            if (!HasPendingArrival || !hasReturnPosition || SceneManager.GetActiveScene().name != "StartScene") return;
            PlacePlayerAtReturnPosition(player);
        }

        private static void PlacePlayerAtReturnPosition(PlayerInputReader player)
        {
            var controller = player.GetComponent<CharacterController>();
            bool controllerEnabled = controller != null && controller.enabled;
            if (controllerEnabled) controller.enabled = false;
            player.transform.SetPositionAndRotation(returnPosition, returnRotation);
            if (controllerEnabled) controller.enabled = true;
        }

        public static string RestorePlayer(PlayerInputReader player)
        {
            if (!HasPendingArrival) return null;
            var vitals = player.GetComponent<HealthShield>();
            if (hasVitals && vitals != null)
            {
                // Raise the cap first so the saved health is not clamped to the base maximum
                vitals.IncreaseMaxHealth(bonusMaxHealth);
                vitals.RestoreForSceneTravel(health, shield);
            }
            player.GetComponent<PlayerStamina>()?.IncreaseMaxStamina(bonusMaxStamina);
            var maze = Object.FindFirstObjectByType<MazeSystem>();
            if (maze != null)
            {
                maze.AddRecoveryBonus(bonusRecovery);
                maze.AddControlDurationBonus(bonusControlDuration);
            }
            var weapons = player.GetComponentInChildren<WeaponManager>();
            if (weapons != null && weaponDamageBonus != null)
                for (int i = 0; i < weaponDamageBonus.Length && i < weapons.WeaponCount; i++)
                    if (weaponDamageBonus[i] > 0f) weapons.AddDamageBonus(i, weaponDamageBonus[i]);
            player.GetComponent<PlayerWallet>().RestoreCoins(coins);
            HasPendingArrival = false;
            string message = arrivalMessage;
            arrivalMessage = null;
            return message;
        }
        [System.Serializable] public sealed class SaveState { public bool BossUnlocked; public bool BossDefeated; public Vector3 returnPosition; public Quaternion returnRotation; public bool hasReturnPosition; public bool HasTravelled; public int PreferredSlot; public bool puzzleTutorialShown; public bool CombatTutorialShown; public int jumpIndex; }
        public static SaveState CaptureState() => new SaveState { BossUnlocked = BossUnlocked, BossDefeated = BossDefeated, returnPosition = returnPosition, returnRotation = returnRotation, hasReturnPosition = hasReturnPosition, HasTravelled = HasTravelled, PreferredSlot = PreferredSlot, puzzleTutorialShown = puzzleTutorialShown, CombatTutorialShown = CombatTutorialShown, jumpIndex = jumpIndex };
        public static void RestoreState(SaveState s)
        {
            if (s == null) return;

            BossUnlocked = s.BossUnlocked; BossDefeated = s.BossDefeated; returnPosition = s.returnPosition; returnRotation = s.returnRotation; hasReturnPosition = s.hasReturnPosition; HasTravelled = s.HasTravelled; PreferredSlot = s.PreferredSlot; puzzleTutorialShown = s.puzzleTutorialShown; CombatTutorialShown = s.CombatTutorialShown; jumpIndex = s.jumpIndex;
            HasPendingArrival = false;
        }

    }
}
