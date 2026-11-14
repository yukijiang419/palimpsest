using System;
using Caffeine.Gameplay.Quests;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.UI;
using UnityEngine;
using UnityEngine.Assertions;

namespace Caffeine.Gameplay.Config
{
    // Shared gameplay settings edited in one asset.
    [CreateAssetMenu(fileName = "GameplayConfig", menuName = "Caffeine/Gameplay Config")]
    public sealed class GameplayConfig : ScriptableObject
    {
        [field: SerializeField] public SimulationSettings Simulation { get; private set; } = new();
        [field: SerializeField] public PlayerSettings Player { get; private set; } = new();
        [field: SerializeField] public CameraSettings Camera { get; private set; } = new();
        [field: SerializeField] public AttackSettings Attack { get; private set; } = new();
        [field: SerializeField] public ProjectileSettings Projectile { get; private set; } = new();
        [field: SerializeField] public DamageSettings Damage { get; private set; } = new();
        [field: SerializeField] public EnemySettings Enemy { get; private set; } = new();
        [field: SerializeField] public BossSettings Boss { get; private set; } = new();
        [field: SerializeField] public PuzzlePushSettings PuzzlePush { get; private set; } = new();
        [field: SerializeField] public CoinSettings Coin { get; private set; } = new();
        [field: SerializeField] public FeedbackAudioSettings FeedbackAudio { get; private set; } = new();
        [field: SerializeField] public MazeSettings Maze { get; private set; } = new();
        [field: SerializeField] public UiSettings UI { get; private set; } = new();

        [field: SerializeField] public QuestSettings Quests { get; private set; } = new();
        [field: SerializeField] public ShopSettings Shop { get; private set; } = new();
        [field: SerializeField] public EncounterSettings Encounters { get; private set; } = new();
        [field: SerializeField] public BoundarySettings Boundary { get; private set; } = new();
        [field: SerializeField] public StorySettings Story { get; private set; } = new();
        [field: SerializeField] public TemporalHintSettings TemporalHints { get; private set; } = new();
        [field: SerializeField] public EffectSettings Effects { get; private set; } = new();

        private void OnValidate()
        {
            // Invalid settings should fail while editing, before play mode.
            Assert.IsNotNull(Simulation);
            Assert.IsNotNull(Player);
            Assert.IsNotNull(Camera);
            Assert.IsNotNull(Attack);
            Assert.IsTrue(Attack.Moves != null && Attack.Moves.Length == 6);
            foreach (var move in Attack.Moves)
            {
                Assert.IsNotNull(move);
                Assert.IsTrue(move.DamageMultiplier >= 0f && move.RangeMultiplier > 0f && move.ArcMultiplier > 0f);
                Assert.IsTrue(move.AnimationSpeedMultiplier > 0f && move.StaminaCost >= 0f);
                Assert.IsTrue(move.ImpactNormalizedTime >= 0f && move.ImpactNormalizedTime <= 1f);
                Assert.IsTrue(move.TwoHandImpactNormalizedTime >= 0f && move.TwoHandImpactNormalizedTime <= 1f);
                Assert.IsTrue(move.ChainDelay >= 0f && move.PulseCount > 0 && move.PulseInterval > 0f && move.StepDistance >= 0f);
            }
            Assert.IsTrue(Attack.AnimationTimeout > 0f && Attack.ComboInputWindow > 0f && Attack.ComboResetDelay >= 0f);
            Assert.IsNotNull(Boss);
            Assert.IsTrue(Boss.HitRadius > 0f && Boss.MeleeWindup > 0f && Boss.MinionKillsToBreakShield > 0);
            Assert.IsTrue(Boss.PaperCount > 0 && Boss.PaperRadius > 0f && Boss.PaperDamage >= 0f);
            Assert.IsTrue(Boss.AttackInterval > 0f && Boss.BrokenAttackInterval > 0f && Boss.AttackWindup >= 0f);
            Assert.IsNotNull(PuzzlePush);
            Assert.IsTrue(PuzzlePush.PushWalkSpeed > 0f && PuzzlePush.GripSpeed > 0f && PuzzlePush.FollowGain > 0f);
            Assert.IsTrue(PuzzlePush.StuckDistance > 0f && PuzzlePush.AlignSpeed > 0f && PuzzlePush.SearchRadius > 0f);

            Assert.IsNotNull(Projectile);
            Assert.IsNotNull(Damage);
            Assert.IsNotNull(Damage.Recovery);
            Assert.IsNotNull(Damage.PathDamage);
            Assert.IsNotNull(Damage.Control);
            Assert.IsNotNull(Damage.Normal);
            Assert.IsNotNull(Enemy);
            Assert.IsNotNull(Coin);
            Assert.IsNotNull(FeedbackAudio);
            Assert.IsTrue(FeedbackAudio.MusicCrossfadeSeconds > 0f && FeedbackAudio.MenuFadeSpeed > 0f);
            Assert.IsTrue(FeedbackAudio.MusicStateInterval > 0f && FeedbackAudio.CombatMusicHoldSeconds >= 0f);
            Assert.IsTrue(FeedbackAudio.CombatVolumes.Length == 10 && FeedbackAudio.CombatIntervals.Length == 10);
            foreach (float volume in FeedbackAudio.CombatVolumes)
            {
                Assert.IsTrue(volume >= 0f && volume <= 1f);
            }
            foreach (float interval in FeedbackAudio.CombatIntervals)
            {
                Assert.IsTrue(interval >= 0f);
            }

            Assert.IsNotNull(Maze);
            Assert.IsNotNull(UI);
            Assert.IsNotNull(Quests);
            Assert.IsNotNull(Shop);
            Assert.IsNotNull(Encounters);
            Assert.IsNotNull(Boundary);
            Assert.IsNotNull(Story);
            Assert.IsNotNull(Effects);
            Assert.IsTrue(Effects.ShiftSeconds > 0f && Effects.ShiftRadius > 0f && Effects.SurfaceWidth > 0f);
            Assert.IsTrue(Effects.ShieldHitSeconds > 0f && Effects.ShieldBreakSeconds > 0f);
            Assert.IsTrue(Effects.SeatSeconds > 0f && Effects.RepairSeconds > 0f);
            foreach (var upgrade in new[] { Shop.WeaponDamage, Shop.MaxHealth, Shop.MaxStamina, Shop.MazeRecovery, Shop.MazeControl })
                Assert.IsTrue(upgrade != null && upgrade.amountPerLevel > 0 && upgrade.basePrice >= 0
                    && upgrade.pricePerLevel >= 0 && upgrade.maxLevel >= 0);
            Assert.IsTrue(Encounters.SquadsPerRound != null && Encounters.SquadsPerRound.Length == 3);
            foreach (int squads in Encounters.SquadsPerRound) Assert.IsTrue(squads > 0);
            Assert.IsTrue(Encounters.Formation != null && Encounters.Formation.Length > 0);
            foreach (var member in Encounters.Formation) Assert.IsNotNull(member);
            Assert.IsTrue(Boundary.PlayableOutline != null && Boundary.PlayableOutline.Length >= 3);
            Assert.IsTrue(Boundary.ReturnOutsideMargin >= 0 && Boundary.BlockerHeight > 0
                && Boundary.BlockerThickness > 0 && Boundary.FadeOutSeconds > 0
                && Boundary.HoldSeconds >= 0 && Boundary.FadeInSeconds > 0);
            Assert.IsTrue(Story.BlendStart >= 0 && Story.BlendEnd > Story.BlendStart && Story.BlendEnd <= 1);
            Assert.IsTrue(Story.TypewriterCharactersPerSecond > 0);
            Assert.IsTrue(Story.FirstFadeInSeconds > 0 && Story.FadeInSeconds > 0
                && Story.FadeOutSeconds > 0 && Story.VideoPrepareTimeout > 0);
            Assert.IsTrue(UI.MinimapWorldRadius >= 1 && UI.MinimapRefreshRate >= 1 && UI.MinimapRefreshRate <= 60);
            Assert.IsTrue(UI.FullMapTextureSize >= 256 && UI.FullMapTextureSize <= 4096);
            Assert.IsTrue(UI.FullMapPadding >= 0f && UI.FullMapPadding < 0.5f);
            if (Quests != null && !Quests.TryValidate(out string questError))
            {
                Debug.LogError(questError, this);
            }

            Assert.IsTrue(Simulation.MazeTimeScale >= 0f);
            Assert.IsTrue(Player.MaxHealth > 0f && Player.MoveSpeed > 0f);
            Assert.IsTrue(Player.RespawnProtectionSeconds >= 0f);
            Assert.IsTrue(Player.SprintSpeed >= Player.MoveSpeed);
            Assert.IsTrue(Player.DashDistance >= 0f && Player.DashDuration > 0f);
            Assert.IsTrue(Player.SlideLeanDegrees >= 0f && Player.SlideLowering >= 0f
                && Player.SlideRecoveryDuration > 0f);
            Assert.IsTrue(Player.MaxStamina >= 0f);
            Assert.IsTrue(Player.DashStaminaCost >= 0f && Player.DashStaminaCost <= Player.MaxStamina);
            Assert.IsTrue(Player.SprintStaminaPerSecond >= 0f);
            Assert.IsTrue(Player.StaminaRecoveryDelay >= 0f);
            Assert.IsTrue(Player.StaminaRecoveryPerSecond >= 0f);
            Assert.IsTrue(Camera.FollowTargetHeight >= 0f);
            Assert.IsTrue(Camera.FieldOfView > 0f && Camera.FieldOfView < 180f);
            Assert.IsTrue(Camera.HorizontalSensitivity >= 0f && Camera.VerticalSensitivity >= 0f);
            Assert.IsTrue(Camera.TopOrbit.x >= 0f && Camera.TopOrbit.y > 0f);
            Assert.IsTrue(Camera.MiddleOrbit.x >= 0f && Camera.MiddleOrbit.y > 0f);
            Assert.IsTrue(Camera.BottomOrbit.x >= 0f && Camera.BottomOrbit.y > 0f);
            Assert.IsTrue(Camera.OcclusionDamping >= 0f);
            Assert.IsTrue(Camera.LockStartRange > 0f && Camera.LockBreakRange >= Camera.LockStartRange);
            Assert.IsTrue(Camera.LockOcclusionGrace >= 0f && Camera.LockSmoothTime > 0f);
            Assert.IsTrue(Attack.AttackImpactNormalizedTime > 0f && Attack.AttackImpactNormalizedTime <= 1f);
            Assert.IsTrue(Attack.HitStopDuration >= 0f);
            Assert.IsTrue(Attack.HitFlashDuration >= 0f);
            Assert.IsTrue(Attack.HitStunDuration >= 0f);
            Assert.IsTrue(Attack.AttackBufferDuration >= 0f);
            Assert.IsTrue(Attack.AttackMoveSpeedMultiplier >= 0f && Attack.AttackMoveSpeedMultiplier <= 1f);
            Assert.IsTrue(Attack.HitRecoilDistance >= 0f && Attack.HitRecoilDegrees >= 0f
                && Attack.HitRecoilDuration > 0f);
            Assert.IsTrue(Attack.MeleeRange >= 0f);
            Assert.IsTrue(Attack.RushHitHalfWidth > 0f);
            Assert.IsTrue(Attack.MeleeDamage >= 0f);
            Assert.IsTrue(Attack.MeleeArcDegrees > 0f && Attack.MeleeArcDegrees <= 360f);
            Assert.IsTrue(Projectile.Speed >= 0f);
            Assert.IsTrue(Projectile.Lifetime >= 0f);
            Assert.IsTrue(Damage.Recovery.StageOneValue >= 0f);
            Assert.IsTrue(Damage.Recovery.StageTwoValue >= 0f);
            Assert.IsTrue(Damage.Recovery.StageTwoReduction >= 0f && Damage.Recovery.StageTwoReduction <= 1f);
            Assert.IsTrue(Damage.Recovery.StageTwoDuration >= 0f);
            Assert.IsTrue(Damage.PathDamage.StageOneValue >= 0f);
            Assert.IsTrue(Damage.PathDamage.StageTwoValue >= 0f);
            Assert.IsTrue(Damage.Control.StageOneDuration >= 0f);
            Assert.IsTrue(Damage.Control.StageTwoDuration >= 0f);
            Assert.IsTrue(Damage.Control.StageTwoVulnerability >= 0f);
            Assert.IsTrue(Damage.Normal.MinimumDamage >= 0f);
            Assert.IsTrue(Damage.Normal.ShieldDamageReduction >= 0f && Damage.Normal.ShieldDamageReduction <= 1f);
            Assert.IsTrue(Enemy.RangedCount >= 0);
            Assert.IsTrue(Enemy.MeleeCount >= 0);
            Assert.IsTrue(Enemy.MaxHealth > 0f && Enemy.MaxShield >= 0f);
            Assert.IsTrue(Enemy.MoveSpeed >= 0f);
            Assert.IsTrue(Enemy.PreferredRangeStart >= 0f);
            Assert.IsTrue(Enemy.PreferredRangeEnd > Enemy.PreferredRangeStart);
            Assert.IsTrue(Enemy.AttackInterval >= 0f);
            Assert.IsTrue(Enemy.ProjectileDamage >= 0f);
            Assert.IsTrue(Enemy.MeleeRangeStart >= 0f);
            Assert.IsTrue(Enemy.MeleeRangeEnd > Enemy.MeleeRangeStart);
            Assert.IsTrue(Enemy.MeleeDamage >= 0f);
            Assert.IsTrue(Enemy.MeleeImpactNormalizedTime > 0f && Enemy.MeleeImpactNormalizedTime <= 1f);
            Assert.IsTrue(Coin.PrewarmCount >= 0);
            Assert.IsTrue(Coin.MaxCreatesPerFrame > 0);
            Assert.IsTrue(Coin.MaxSpawnsPerFrame > 0);
            Assert.IsTrue(Coin.MaxInactive > 0);
            Assert.IsTrue(Maze.BoardSize == 5);
            Assert.IsTrue(Maze.ObstacleCount == 4);
            Assert.IsTrue(Maze.StageOneThreshold >= 3);
            Assert.IsTrue(Maze.StageTwoThreshold > Maze.StageOneThreshold);
            Assert.IsTrue(Maze.StartRange >= 0f);
            Assert.IsTrue(Maze.BreakRange > Maze.StartRange);
            Assert.IsTrue(Maze.PersonalCooldown >= 0f);
            Assert.IsTrue(Maze.GestureThreshold > 0f);
            Assert.IsTrue(UI.AimLineWidth >= 0f && UI.AimLineLength >= 0f);
            Assert.IsTrue(UI.MazeLinkWidth >= 0f && UI.WorldLineHeight >= 0f);
            Assert.IsTrue(UI.TokenOutlineThickness >= 0f);
            Assert.IsTrue(UI.BoardCellSize >= 0f && UI.BoardSpacing >= 0f && UI.BoardPadding >= 0f);
            Assert.IsTrue(UI.EntangleWidth >= 0f && UI.EntangleRadius >= 0f && UI.EntangleHeight >= 0f);
            Assert.IsTrue(UI.EntangleTurns >= 0f && UI.EntangleSegments >= 8);
        }

        [Serializable]
        public sealed class UpgradeSettings
        {
            public float amountPerLevel;
            public int basePrice, pricePerLevel, maxLevel;
            public int PriceAt(int level) => basePrice + level * pricePerLevel;
        }

        [Serializable]
        public sealed class ShopSettings
        {
            public UpgradeSettings WeaponDamage = new() { amountPerLevel = 5, basePrice = 10, pricePerLevel = 10, maxLevel = 5 };
            public UpgradeSettings MaxHealth = new() { amountPerLevel = 20, basePrice = 15, pricePerLevel = 10, maxLevel = 5 };
            public UpgradeSettings MaxStamina = new() { amountPerLevel = 20, basePrice = 15, pricePerLevel = 10, maxLevel = 5 };
            public UpgradeSettings MazeRecovery = new() { amountPerLevel = .2f, basePrice = 20, pricePerLevel = 10, maxLevel = 5 };
            public UpgradeSettings MazeControl = new() { amountPerLevel = .2f, basePrice = 20, pricePerLevel = 10, maxLevel = 5 };
        }

        [Serializable]
        public sealed class EncounterMember
        {
            public Vector3 offset;
            public EnemyType type;
        }

        [Serializable]
        public sealed class EncounterSettings
        {
            public int[] SquadsPerRound = { 2, 3, 5 };
            public EncounterMember[] Formation =
            {
                new() { offset = new(-2.8f, 0, -2.5f), type = EnemyType.Melee },
                new() { offset = new(2.8f, 0, -2.5f), type = EnemyType.Melee },
                new() { offset = new(0, 0, -5f), type = EnemyType.Melee },
                new() { offset = new(0, 0, 3.5f), type = EnemyType.Ranged },
                new() { offset = new(3.5f, 0, 5f), type = EnemyType.Scanner }
            };
            public int TotalSquads => RoundStart(SquadsPerRound.Length + 1);
            public int EnemyCount => TotalSquads * Formation.Length;
            public int RoundStart(int round)
            {
                int start = 0;
                for (int i = 0; i < round - 1; i++) start += SquadsPerRound[i];
                return start;
            }
        }

        [Serializable]
        public sealed class BoundarySettings
        {
            // World X/Z perimeter; the north-west section follows the existing garden fence.
            public Vector2[] PlayableOutline =
            {
                new(-52, -16), new(218, -16), new(218, 210), new(156, 258),
                new(105, 263), new(85, 263), new(85, 243), new(50, 243),
                new(50, 250), new(26, 263), new(-14, 263), new(-14, 356), new(-54, 358),
                new(-65, 350), new(-84, 332), new(-84, 302), new(-78, 280),
                new(-44, 278), new(-39, 263), new(-23, 263), new(-62, 263),
                new(-62, 195), new(-37, 195), new(-37, 128), new(-52, 128)
            };
            public float ReturnOutsideMargin = 3f;
            public float BlockerHeight = 3f;
            public float BlockerThickness = .6f;
            public float FadeOutSeconds = .35f;
            public float HoldSeconds = .15f;
            public float FadeInSeconds = .5f;

            public bool TryGetPlayableBounds(out Bounds bounds)
            {
                bounds = default;
                if (PlayableOutline == null || PlayableOutline.Length < 3) return false;
                Vector2 first = PlayableOutline[0];
                bounds = new Bounds(new Vector3(first.x, 0, first.y), Vector3.zero);
                foreach (Vector2 point in PlayableOutline)
                    bounds.Encapsulate(new Vector3(point.x, 0, point.y));
                return bounds.size.x > 0 && bounds.size.z > 0;
            }

            public bool ContainsPosition(Vector3 position, float outsideMargin = 0f)
            {
                if (PlayableOutline == null || PlayableOutline.Length < 3) return false;
                Vector2 point = new(position.x, position.z);
                bool inside = false;
                float nearestSquared = float.PositiveInfinity;
                for (int i = 0, j = PlayableOutline.Length - 1; i < PlayableOutline.Length; j = i++)
                {
                    Vector2 a = PlayableOutline[j], b = PlayableOutline[i];
                    Vector2 edge = b - a;
                    float t = edge.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0;
                    nearestSquared = Mathf.Min(nearestSquared, (point - a - edge * t).sqrMagnitude);
                    if ((a.y > point.y) != (b.y > point.y)
                        && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                float margin = Mathf.Max(0, outsideMargin);
                return inside || nearestSquared <= margin * margin + .000001f;
            }
        }

        [Serializable]
        public sealed class EffectSettings
        {
            [field: SerializeField] public float ShiftSeconds { get; private set; } = .8f;
            [field: SerializeField] public float ShiftRadius { get; private set; } = 6f;
            [field: SerializeField] public float SurfaceWidth { get; private set; } = .65f;
            [field: SerializeField] public int ShiftParticles { get; private set; } = 20;
            [field: SerializeField] public Color PastColor { get; private set; } = new(1f, .57f, .26f);
            [field: SerializeField] public Color PresentColor { get; private set; } = new(.35f, .67f, 1f);
            [field: SerializeField] public Color FutureColor { get; private set; } = new(.68f, .43f, 1f);
            [field: SerializeField] public float ShieldHitSeconds { get; private set; } = .22f;
            [field: SerializeField] public float ShieldBreakSeconds { get; private set; } = .55f;
            [field: SerializeField] public int ShieldBreakParticles { get; private set; } = 24;
            [field: SerializeField] public Color ShieldColor { get; private set; } = new(.3f, .85f, 1f);
            [field: SerializeField] public float SeatSeconds { get; private set; } = .4f;
            [field: SerializeField] public int SeatParticles { get; private set; } = 16;
            [field: SerializeField] public float RepairSeconds { get; private set; } = .6f;
        }

        [Serializable]
        public sealed class TemporalHintSettings
        {
            [Min(.1f)] public float ApproachDistance = 5f;
            [Min(.1f)] public float PulseSeconds = .8f;
        }

        [Serializable]
        public sealed class StorySettings
        {
            public OpeningSequenceController openingSequence;
            public TutorialGuide tutorialGuide;
            public TutorialGuide puzzleTutorialGuide;
            public TutorialGuide combatTutorialGuide;
            [TextArea(3, 8)] public string ending;
            [Min(1f)] public float TypewriterCharactersPerSecond = 35f;
            public float BlendStart = .4f, BlendEnd = .82f;
            public float FirstFadeInSeconds = 1.2f, FadeInSeconds = .55f, FadeOutSeconds = .55f;
            public float VideoPrepareTimeout = 30f;
        }

        [Serializable]
        public sealed class QuestSettings
        {
            [field: SerializeField, Min(0.1f)] public float OverviewTransitionSeconds { get; private set; } = 0.8f;
            [field: SerializeField, Min(1f)] public float OverviewFlyoverSeconds { get; private set; } = 8f;
            [field: SerializeField, Min(0f)] public float OverviewStopSeconds { get; private set; } = 0.7f;
            [field: SerializeField, Min(2f)] public float OverviewHeight { get; private set; } = 18f;
            [field: SerializeField, Min(2f)] public float OverviewLookAhead { get; private set; } = 12f;
            [field: SerializeField] public string BossSceneName { get; private set; } = "BossArena";
            [field: SerializeField] public MerchantQuestDefinition[] Definitions { get; private set; } = Array.Empty<MerchantQuestDefinition>();
            [field: SerializeField, Min(0.1f)] public float ArrowHeight { get; private set; } = 2.5f;
            [field: SerializeField, Min(0)] public float ArrowFloatAmplitude { get; private set; } = 0.2f;
            [field: SerializeField, Min(0)] public float ArrowFloatSpeed { get; private set; } = 2f;
            [field: SerializeField, Min(0)] public float ArrowRotateSpeed { get; private set; } = 60f;

            public bool TryGetDefinition(string id, out MerchantQuestDefinition definition)
            {
                definition = null;
                if (string.IsNullOrWhiteSpace(id) || Definitions == null)
                {
                    return false;
                }

                foreach (var candidate in Definitions)
                {
                    if (candidate == null || candidate.QuestId != id)
                    {
                        continue;
                    }

                    if (definition != null)
                    {
                        definition = null;
                        return false;
                    }
                    definition = candidate;
                }
                return definition != null;
            }

            public bool TryGetPuzzleDefinition(int puzzleIndex, out MerchantQuestDefinition definition)
            {
                definition = null;
                if (Definitions == null) return false;
                foreach (var candidate in Definitions)
                {
                    if (candidate == null || !candidate.HasPuzzle || candidate.Puzzle.puzzleIndex != puzzleIndex) continue;
                    if (definition != null) { definition = null; return false; }
                    definition = candidate;
                }
                return definition != null;
            }

            public bool TryValidate(out string error)
            {
                error = null;
                if (Definitions == null || string.IsNullOrWhiteSpace(BossSceneName))
                {
                    error = "Quest definitions or Boss scene name are missing.";
                    return false;
                }
                var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                var puzzleIndices = new System.Collections.Generic.HashSet<int>();
                foreach (var definition in Definitions)
                {
                    if (definition == null)
                    {
                        error = "Quest definitions contains an empty entry.";
                        return false;
                    }
                    if (!definition.TryValidate(out error))
                    {
                        return false;
                    }

                    if (!ids.Add(definition.QuestId))
                    {
                        error = $"Duplicate quest ID: {definition.QuestId}";
                        return false;
                    }
                    if (definition.HasPuzzle && !puzzleIndices.Add(definition.Puzzle.puzzleIndex))
                    {
                        error = $"Duplicate puzzle task: {definition.Puzzle.puzzleIndex}";
                        return false;
                    }
                }
                if (ArrowHeight <= 0 || ArrowFloatAmplitude < 0 || ArrowFloatSpeed < 0 || ArrowRotateSpeed < 0)
                {
                    error = "Quest arrow height must be positive; floating amplitude and animation speeds cannot be negative.";
                    return false;
                }
                return true;
            }
        }

        [Serializable]
        public sealed class SimulationSettings
        {
            [field: SerializeField] public float MazeTimeScale { get; private set; } = 0.3f;
        }

        [Serializable]
        public sealed class PlayerSettings
        {
            [field: SerializeField] public float MaxHealth { get; private set; } = 100f;
            [field: SerializeField, Min(0f)] public float RespawnProtectionSeconds { get; private set; } = 3f;
            [field: SerializeField] public float MoveSpeed { get; private set; } = 5.5f;
            [field: SerializeField, Range(0f, 1f)] public float AirControl { get; private set; } = 0.75f;
            [field: SerializeField] public float SprintSpeed { get; private set; } = 8f;
            [field: SerializeField, Tooltip("Horizontal speed gained per second while moving.")]
            public float Acceleration { get; private set; } = 40f;
            [field: SerializeField, Tooltip("Horizontal speed lost per second after releasing input.")]
            public float Deceleration { get; private set; } = 30f;
            [field: SerializeField] public float DashDistance { get; private set; } = 3f;
            [field: SerializeField] public float DashDuration { get; private set; } = 0.2f;
            [field: SerializeField] public float SlideLeanDegrees { get; private set; } = 10f;
            [field: SerializeField] public float SlideLowering { get; private set; } = 0.08f;
            [field: SerializeField] public float SlideRecoveryDuration { get; private set; } = 0.06f;
            [field: SerializeField] public float MaxStamina { get; private set; } = 100f;
            [field: SerializeField] public float DashStaminaCost { get; private set; } = 20f;
            [field: SerializeField] public float SprintStaminaPerSecond { get; private set; } = 20f;
            [field: SerializeField] public float StaminaRecoveryDelay { get; private set; } = 0.5f;
            [field: SerializeField] public float StaminaRecoveryPerSecond { get; private set; } = 25f;
        }

        [Serializable]
        public sealed class CameraSettings
        {
            [field: SerializeField] public float FollowTargetHeight { get; private set; } = 0.5f;
            [field: SerializeField] public float FieldOfView { get; private set; } = 55f;
            [field: SerializeField] public float HorizontalSensitivity { get; private set; } = 0.12f;
            [field: SerializeField] public float VerticalSensitivity { get; private set; } = 0.002f;
            [field: SerializeField, Tooltip("Height and radius of the upper FreeLook orbit.")]
            public Vector2 TopOrbit { get; private set; } = new(3.5f, 4.5f);
            [field: SerializeField, Tooltip("Height and radius of the middle FreeLook orbit.")]
            public Vector2 MiddleOrbit { get; private set; } = new(1.4f, 4.2f);
            [field: SerializeField, Tooltip("Height and radius of the lower FreeLook orbit.")]
            public Vector2 BottomOrbit { get; private set; } = new(0.3f, 3.8f);
            [field: SerializeField] public float OcclusionDamping { get; private set; } = 0.35f;
            [field: SerializeField] public float LockStartRange { get; private set; } = 20f;
            [field: SerializeField] public float LockBreakRange { get; private set; } = 25f;
            [field: SerializeField] public float LockOcclusionGrace { get; private set; } = 0.75f;
            [field: SerializeField] public float LockSmoothTime { get; private set; } = 0.15f;
            [field: SerializeField] public float LockMaxTurnSpeed { get; private set; } = 240f;
            [field: SerializeField] public float LockDirectionMinimumDistance { get; private set; } = 0.5f;
            [field: SerializeField] public float LockGroupFramingSize { get; private set; } = 0.8f;
            [field: SerializeField] public float LockMaximumFieldOfView { get; private set; } = 65f;
        }

        [Serializable]
        public sealed class SwordMoveSettings
        {
            [field: SerializeField] public float DamageMultiplier { get; private set; } = 1f;
            [field: SerializeField] public float RangeMultiplier { get; private set; } = 1f;
            [field: SerializeField] public float ArcMultiplier { get; private set; } = 1f;
            [field: SerializeField] public float AnimationSpeedMultiplier { get; private set; } = 1.3f;
            [field: SerializeField] public float StaminaCost { get; private set; } = 0f;
            [field: SerializeField, Tooltip("Zero uses the weapon combo timing, then the shared attack timing.")]
            public float ImpactNormalizedTime { get; private set; } = 0f;
            [field: SerializeField, Tooltip("Fallback for two-handed combos without weapon-specific timing. Zero uses the shared attack timing.")]
            public float TwoHandImpactNormalizedTime { get; private set; } = 0f;
            [field: SerializeField] public float ChainDelay { get; private set; } = 0.14f;
            [field: SerializeField] public int PulseCount { get; private set; } = 1;
            [field: SerializeField] public float PulseInterval { get; private set; } = 0.075f;
            [field: SerializeField] public float StepDistance { get; private set; } = 0f;

            public SwordMoveSettings(float damage, float range = 1f, float arc = 1f,
                float speed = 1.3f, float stamina = 0f, float impact = 0f,
                float twoHandImpact = 0f, float chainDelay = 0.14f, int pulses = 1, float step = 0f)
            {
                DamageMultiplier = damage;
                RangeMultiplier = range;
                ArcMultiplier = arc;
                AnimationSpeedMultiplier = speed;
                StaminaCost = stamina;
                ImpactNormalizedTime = impact;
                TwoHandImpactNormalizedTime = twoHandImpact;
                ChainDelay = chainDelay;
                PulseCount = pulses;
                StepDistance = step;
            }
        }

        [Serializable]
        public sealed class AttackSettings
        {
            // Indexed by SwordMove: Cleave, ReturnCut, Finisher, Rush, RapidCut, PrecisionCut.
            [field: SerializeField] public SwordMoveSettings[] Moves { get; private set; } =
            {
                new(0.85f),
                new(1.05f, twoHandImpact: 0.55f),
                new(1.8f, range: 1.25f, arc: 1.8f, twoHandImpact: 0.62f),
                new(1.25f, range: 1.5f, arc: 0.55f, speed: 1.65f, stamina: 12f, impact: 0.3f, step: 3.5f),
                new(0.48f, speed: 1.8f, stamina: 18f, impact: 0.22f, chainDelay: 0.38f, pulses: 5),
                new(1f, stamina: 22f, impact: 0.5f)
            };
            [field: SerializeField] public float AnimationTimeout { get; private set; } = 2f;
            [field: SerializeField] public float ComboInputWindow { get; private set; } = 2f;

            [field: SerializeField, Tooltip("Damage multiplier for judgment cuts with a two-handed sword, including purchased attack power.")]
            public float TwoHandPrecisionDamageMultiplier { get; private set; } = 2f;
            [field: SerializeField] public float AttackImpactNormalizedTime { get; private set; } = 0.35f;
            [field: SerializeField, Tooltip("Impact point of each combo swing; missing entries fall back to AttackImpactNormalizedTime.")]
            public float[] ComboImpactNormalizedTimes { get; private set; } = new float[0];
            [field: SerializeField, Tooltip("Seconds after a swing ends during which the next attack continues the combo.")]
            public float ComboResetDelay { get; private set; } = 0.5f;
            [field: SerializeField] public float AttackBufferDuration { get; private set; } = 0.45f;
            [field: SerializeField, Range(0f, 1f)] public float AttackMoveSpeedMultiplier { get; private set; } = 0.45f;
            [field: SerializeField, Tooltip("Animation progress to attack playback multiplier; weapon speed is applied separately.")]
            public AnimationCurve AttackSpeedCurve { get; private set; } = new(
                new Keyframe(0f, 0.8f), new Keyframe(0.18f, 0.8f),
                new Keyframe(0.35f, 1.5f), new Keyframe(0.48f, 1.5f),
                new Keyframe(0.65f, 0.9f), new Keyframe(1f, 0.9f));
            [field: SerializeField] public float HitStopDuration { get; private set; } = 0.06f;
            [field: SerializeField] public float HitFlashDuration { get; private set; } = 0.1f;
            [field: SerializeField] public float HitStunDuration { get; private set; } = 0.32f;
            [field: SerializeField] public float HitRecoilDistance { get; private set; } = 0.14f;
            [field: SerializeField] public float HitRecoilDegrees { get; private set; } = 18f;
            [field: SerializeField] public float HitRecoilDuration { get; private set; } = 0.3f;
            [field: SerializeField] public float MeleeRange { get; private set; } = 2.5f;
            [field: SerializeField, Tooltip("Half-width of the rush slash's horizontal damage corridor along its actual movement.")]
            public float RushHitHalfWidth { get; private set; } = 0.6f;
            [field: SerializeField] public float MeleeDamage { get; private set; } = 25f;
            [field: SerializeField] public float MeleeArcDegrees { get; private set; } = 90f;
        }

        [Serializable]
        public sealed class BossSettings
        {
            [field: SerializeField] public float HitRadius { get; private set; } = 1.9f;
            [field: SerializeField] public float TurnSpeed { get; private set; } = 120f;
            [field: SerializeField] public float ActivationRange { get; private set; } = 30f;
            [field: SerializeField] public bool JumpIn { get; private set; } = true;
            [field: SerializeField] public float JumpInHeight { get; private set; } = 25f;
            [field: SerializeField] public float JumpInFallTime { get; private set; } = 0.8f;
            [field: SerializeField] public float IntroDuration { get; private set; } = 2.2f;
            [field: SerializeField] public float AttackInterval { get; private set; } = 7f;
            [field: SerializeField] public float AttackWindup { get; private set; } = 0.5f;
            [field: SerializeField] public int PaperCount { get; private set; } = 4;
            [field: SerializeField] public float PaperRadius { get; private set; } = 1.3f;
            [field: SerializeField] public float PaperSpread { get; private set; } = 10f;
            [field: SerializeField] public float PaperSpacing { get; private set; } = 3.5f;
            [field: SerializeField] public float WarningTime { get; private set; } = 1.5f;
            [field: SerializeField] public float DropInterval { get; private set; } = 0.15f;
            [field: SerializeField] public float PaperDamage { get; private set; } = 20f;
            [field: SerializeField] public float PaperDropHeight { get; private set; } = 12f;
            [field: SerializeField] public int BrokenExtraPaper { get; private set; } = 2;
            [field: SerializeField] public float BrokenAttackInterval { get; private set; } = 5f;
            [field: SerializeField] public float RingRadius { get; private set; } = 7f;
            [field: SerializeField] public float WallSegment { get; private set; } = 2.5f;
            [field: SerializeField] public float WallHeight { get; private set; } = 2f;
            [field: SerializeField] public float WallThickness { get; private set; } = 0.5f;
            [field: SerializeField] public float SprayTime { get; private set; } = 1.2f;
            [field: SerializeField] public float WallRiseTime { get; private set; } = 0.4f;
            [field: SerializeField] public float WallLifetime { get; private set; } = 9f;
            [field: SerializeField] public float WallDamage { get; private set; } = 12f;
            [field: SerializeField] public int RingVolleys { get; private set; } = 3;
            [field: SerializeField] public float RingVolleyInterval { get; private set; } = 2.6f;
            [field: SerializeField] public int RingVolleySize { get; private set; } = 5;
            [field: SerializeField] public float MeleeRange { get; private set; } = 2.5f;
            [field: SerializeField] public float SlamRadius { get; private set; } = 2.2f;
            [field: SerializeField] public float MeleeWindup { get; private set; } = 0.9f;
            [field: SerializeField] public float MeleeDamage { get; private set; } = 18f;
            [field: SerializeField] public float MeleeCooldown { get; private set; } = 3f;
            [field: SerializeField] public float FirstSummonDelay { get; private set; } = 2f;
            [field: SerializeField] public float NextWaveDelay { get; private set; } = 3f;
            [field: SerializeField] public int MinionsPerSummon { get; private set; } = 3;
            [field: SerializeField] public float SummonWindup { get; private set; } = 0.6f;
            [field: SerializeField] public int MinionKillsToBreakShield { get; private set; } = 6;
            [field: SerializeField] public float DissolveDelay { get; private set; } = 2.5f;
        }

        [Serializable]
        public sealed class PuzzlePushSettings
        {
            [field: SerializeField] public float PushWalkSpeed { get; private set; } = 1.4f;
            [field: SerializeField] public float GripSpeed { get; private set; } = 3f;
            [field: SerializeField] public float FollowGain { get; private set; } = 30f;
            [field: SerializeField] public float StuckDistance { get; private set; } = 0.15f;
            [field: SerializeField] public float MinReach { get; private set; } = 0.5f;
            [field: SerializeField] public float PalmThickness { get; private set; } = 0.04f;
            [field: SerializeField] public float AlignSpeed { get; private set; } = 3f;
            [field: SerializeField] public float ReleaseDistance { get; private set; } = 3.5f;
            [field: SerializeField] public float SearchHeight { get; private set; } = 0.8f;
            [field: SerializeField] public float SearchRadius { get; private set; } = 2.4f;
            [field: SerializeField] public float FacingThreshold { get; private set; } = 0.15f;
            [field: SerializeField] public float GripDistance { get; private set; } = 1.35f;
            [field: SerializeField] public float BodyClearance { get; private set; } = 0.05f;
        }

        [Serializable]
        public sealed class ProjectileSettings
        {
            [field: SerializeField] public float Speed { get; private set; } = 12f;
            [field: SerializeField] public float Lifetime { get; private set; } = 1.2f;
        }

        [Serializable]
        public sealed class DamageSettings
        {
            [field: SerializeField] public RecoverySettings Recovery { get; private set; } = new();
            [field: SerializeField] public PathDamageSettings PathDamage { get; private set; } = new();
            [field: SerializeField] public ControlSettings Control { get; private set; } = new();
            [field: SerializeField] public NormalSettings Normal { get; private set; } = new();

            [Serializable]
            public sealed class RecoverySettings
            {
                [field: SerializeField] public float StageOneValue { get; private set; } = 25f;
                [field: SerializeField] public float StageTwoValue { get; private set; } = 45f;
                [field: SerializeField] public float StageTwoReduction { get; private set; } = 0.2f;
                [field: SerializeField] public float StageTwoDuration { get; private set; } = 3f;
            }

            [Serializable]
            public sealed class PathDamageSettings
            {
                [field: SerializeField, Tooltip("When enabled, any resolved maze Damage stage removes the target's entire current shield without health overflow.")]
                public bool BreakShieldOnMaze { get; private set; } = true;
                [field: SerializeField] public float StageOneValue { get; private set; } = 25f;
                [field: SerializeField] public float StageTwoValue { get; private set; } = 40f;
            }

            [Serializable]
            public sealed class ControlSettings
            {
                [field: SerializeField] public float StageOneDuration { get; private set; } = 2f;
                [field: SerializeField] public float StageTwoDuration { get; private set; } = 3.5f;
                [field: SerializeField] public float StageTwoVulnerability { get; private set; } = 1.25f;
            }

            [Serializable]
            public sealed class NormalSettings
            {
                [field: SerializeField] public float MinimumDamage { get; private set; } = 1f;
                [field: SerializeField, Range(0f, 1f), Tooltip("Reduction to normal attack damage while the target has shield.")]
                public float ShieldDamageReduction { get; private set; } = 0.5f;
            }
        }

        [Serializable]
        public sealed class EnemySettings
        {
            [field: SerializeField] public int RangedCount { get; private set; } = 3;
            [field: SerializeField] public int MeleeCount { get; private set; } = 2;
            
            [field: SerializeField] public float MaxHealth { get; private set; } = 35f;
            [field: SerializeField] public float MaxShield { get; private set; } = 150f;
            [field: SerializeField] public float MoveSpeed { get; private set; } = 3.2f;
            [field: SerializeField] public float AggroRange { get; private set; } = 12f;
            [field: SerializeField] public float DisengageRange { get; private set; } = 18f;
            [field: SerializeField] public float DisengageDelay { get; private set; } = 3f;
            [field: SerializeField] public float LeashRange { get; private set; } = 24f;
            [field: SerializeField] public float PreferredRangeStart { get; private set; } = 6f;
            [field: SerializeField] public float PreferredRangeEnd { get; private set; } = 9f;
            [field: SerializeField] public float AttackInterval { get; private set; } = 0.9f;
            [field: SerializeField] public float ProjectileDamage { get; private set; } = 8f;
            [field: SerializeField] public float MeleeRangeStart { get; private set; } = 0.8f;
            [field: SerializeField] public float MeleeRangeEnd { get; private set; } = 1.4f;
            [field: SerializeField] public float MeleeDamage { get; private set; } = 12f;
            [field: SerializeField] public float MeleeImpactNormalizedTime { get; private set; } = 0.5f;
            [field: SerializeField, Tooltip("Ranged enemies with close-range ram enabled charge a player this close.")]
            public float RangedRamRange { get; private set; } = 2.2f;
            [field: SerializeField] public float RangedRamDamage { get; private set; } = 10f;
            [field: SerializeField] public int CoinReward { get; private set; } = 10;

        }

        [Serializable]
        public sealed class FeedbackAudioSettings
        {
            [field: SerializeField, Range(0f, 1f)] public float MusicVolume { get; private set; } = 0.3f;
            [field: SerializeField, Min(0.01f)] public float MusicCrossfadeSeconds { get; private set; } = 1.5f;
            [field: SerializeField, Range(0f, 1f)] public float MenuVolumeMultiplier { get; private set; } = 0.45f;
            [field: SerializeField] public float MenuFadeSpeed { get; private set; } = 2f;
            [field: SerializeField] public float MusicStateInterval { get; private set; } = 0.2f;
            [field: SerializeField] public float CombatMusicHoldSeconds { get; private set; } = 2f;
            [field: SerializeField] public float NavigationVolumeMultiplier { get; private set; } = 0.5f;
            [field: SerializeField] public float NavigationInterval { get; private set; } = 0.05f;
            [field: SerializeField] public float CoinInterval { get; private set; } = 0.06f;
            [field: SerializeField] public Vector2 CoinPitch { get; private set; } = new(0.97f, 1.03f);
            [field: SerializeField] public Vector2 CombatPitch { get; private set; } = new(0.95f, 1.05f);
            [field: SerializeField] public float CombatMinDistance { get; private set; } = 10f;
            [field: SerializeField] public float CombatMaxDistance { get; private set; } = 50f;
            // Indexed by CombatSound.
            [field: SerializeField] public float[] CombatVolumes { get; private set; } =
                { 0.65f, 0.8f, 0.65f, 0.9f, 0.65f, 0.4f, 0.45f, 0.65f, 0.3f, 0.6f };
            [field: SerializeField] public float[] CombatIntervals { get; private set; } =
                { 0.04f, 0.04f, 0.04f, 0.04f, 0.12f, 0.65f, 0.12f, 0.04f, 0.25f, 0.04f };

            [field: SerializeField] public AudioClip Navigate { get; private set; }
            [field: SerializeField] public AudioClip Confirm { get; private set; }
            [field: SerializeField] public AudioClip Open { get; private set; }
            [field: SerializeField] public AudioClip Close { get; private set; }
            [field: SerializeField] public AudioClip Purchase { get; private set; }
            [field: SerializeField] public AudioClip Reject { get; private set; }
            [field: SerializeField] public AudioClip[] Coins { get; private set; } = Array.Empty<AudioClip>();
            [field: SerializeField, Range(0f, 1f)] public float UiVolume { get; private set; } = 0.4f;
            [field: SerializeField, Range(0f, 1f)] public float CoinVolume { get; private set; } = 0.55f;
        }

        [Serializable]
        public sealed class CoinSettings
        {
            [field: SerializeField, Min(0)] public int PrewarmCount { get; private set; } = 200;
            [field: SerializeField, Min(1)] public int MaxCreatesPerFrame { get; private set; } = 8;
            [field: SerializeField, Min(1)] public int MaxSpawnsPerFrame { get; private set; } = 20;
            [field: SerializeField, Min(1)] public int MaxInactive { get; private set; } = 512;
            [field: SerializeField, Tooltip("Enemy reward is split across at most this many coins.")]
            public int MaxPieces { get; private set; } = 10;
            [field: SerializeField] public float SpawnHeight { get; private set; } = 0.8f;
            [field: SerializeField] public float ScatterSpeed { get; private set; } = 1f;
            [field: SerializeField] public float PopSpeed { get; private set; } = 2.5f;
            [field: SerializeField] public float Gravity { get; private set; } = 20f;
            [field: SerializeField] public float HoverHeight { get; private set; } = 0.25f;
            [field: SerializeField] public float SpinDegreesPerSecond { get; private set; } = 180f;
            [field: SerializeField, Tooltip("Seconds after dropping before a coin can be collected.")]
            public float PickupDelay { get; private set; } = 0.5f;
            [field: SerializeField] public float MagnetRadius { get; private set; } = 3f;
            [field: SerializeField] public float MagnetSpeed { get; private set; } = 12f;
            [field: SerializeField] public float CollectDistance { get; private set; } = 0.5f;
        }

        [Serializable]
        public sealed class MazeSettings
        {
            [field: SerializeField] public int BoardSize { get; private set; } = 5;
            [field: SerializeField] public int ObstacleCount { get; private set; } = 4;
            [field: SerializeField] public int StageOneThreshold { get; private set; } = 3;
            [field: SerializeField] public int StageTwoThreshold { get; private set; } = 5;
            [field: SerializeField] public float StartRange { get; private set; } = 11f;
            [field: SerializeField] public float BreakRange { get; private set; } = 14f;
            [field: SerializeField] public float PersonalCooldown { get; private set; } = 6f;
            [field: SerializeField] public float GestureThreshold { get; private set; } = 24f;
        }

        [Serializable]
        public sealed class UiSettings
        {
            [field: SerializeField] public Color RecoveryColor { get; private set; } = new(0.20f, 0.85f, 0.58f, 1f);
            [field: SerializeField] public Color DamageColor { get; private set; } = new(1f, 0.59f, 0.16f, 1f);
            [field: SerializeField] public Color ControlColor { get; private set; } = new(0.45f, 0.38f, 1f, 1f);
            [field: SerializeField] public float MinimapWorldRadius { get; private set; } = 60f;
            [field: SerializeField] public float MinimapRefreshRate { get; private set; } = 10f;
            [field: SerializeField, Tooltip("Resolution of the baked image shared by both campus maps.")]
            public int FullMapTextureSize { get; private set; } = 2048;
            [field: SerializeField] public float FullMapPadding { get; private set; } = 0.05f;
            [field: SerializeField] public Color NeutralColor { get; private set; } = new(0.35f, 0.35f, 0.35f, 1f);
            [field: SerializeField] public Color AimLineColor { get; private set; } = new(0.35f, 0.82f, 1f, 0.95f);
            [field: SerializeField] public Color MazeLinkColor { get; private set; } = new(0.68f, 0.48f, 1f, 0.95f);
            [field: SerializeField] public float AimLineWidth { get; private set; } = 0.045f;
            [field: SerializeField] public float AimLineLength { get; private set; } = 8f;
            [field: SerializeField] public float MazeLinkWidth { get; private set; } = 0.065f;
            [field: SerializeField] public float WorldLineHeight { get; private set; } = 0.7f;
            [field: SerializeField] public float TokenOutlineThickness { get; private set; } = 2f;
            [field: SerializeField] public float BoardCellSize { get; private set; } = 58f;
            [field: SerializeField] public float BoardSpacing { get; private set; } = 5f;
            [field: SerializeField] public float BoardPadding { get; private set; } = 15f;
            [field: SerializeField] public Vector2 BoardOffset { get; private set; } = new(-30f, -30f);
            [field: SerializeField] public Color CellEmptyColor { get; private set; } = new(.06f, .15f, .14f, .55f);
            [field: SerializeField] public Color CellStartColor { get; private set; } = new(.09f, .22f, .21f, .8f);
            [field: SerializeField] public Color CellObstacleColor { get; private set; } = new(.03f, .08f, .08f, .78f);
            [field: SerializeField] public Color CellVisitedColor { get; private set; } = new(.12f, .32f, .29f, .88f);
            [field: SerializeField] public Color EntangleColor { get; private set; } = new(0.68f, 0.48f, 1f, 1f);
            [field: SerializeField] public float EntangleWidth { get; private set; } = 0.055f;
            [field: SerializeField] public float EntangleRadius { get; private set; } = 0.72f;
            [field: SerializeField] public float EntangleHeight { get; private set; } = 1.7f;
            [field: SerializeField] public float EntangleTurns { get; private set; } = 2.5f;
            [field: SerializeField] public int EntangleSegments { get; private set; } = 40;
        }
    }
}
