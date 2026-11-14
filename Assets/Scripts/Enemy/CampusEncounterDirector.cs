using System;
using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Quests;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.Enemy
{
    // Lives on the campus EnemySystem. Only the anchor transforms are authored in the scene.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(EnemySystem))]
    public sealed class CampusEncounterDirector : MonoBehaviour
    {
        [Serializable] public sealed class EnemySnapshot
        {
            public Vector3 Position;
            public Vector3 Home;
            public Quaternion Rotation;
            public float Health = -1f;
            public float Shield;
            public bool Dead;
            public bool spawned;
            public EnemyController.SaveState combat;
            public HealthShield.SaveState vitals;
        }

        private static bool[] ActivatedRounds = Array.Empty<bool>();
        private static EnemySnapshot[] Snapshots = Array.Empty<EnemySnapshot>();
        private static GameplayConfig.EncounterSettings Settings => ConfigSystem.Instance.Config.Encounters;

        [SerializeField] private Transform[] squadAnchors = Array.Empty<Transform>();
        private EnemySystem enemies;
        private bool warned;

        public static int LiveCount
        {
            get
            {
                int count = 0;
                foreach (var snapshot in Snapshots)
                    if (snapshot != null && snapshot.spawned && !snapshot.Dead) count++;
                return count;
            }
        }

        public static void ResetRun()
        {
            ActivatedRounds = Array.Empty<bool>();
            Snapshots = Array.Empty<EnemySnapshot>();
        }

        public static EnemySnapshot Snapshot(int id) => id >= 0 && id < Snapshots.Length ? Snapshots[id] : null;

        public bool TryGetRoundOverview(int round, out IReadOnlyList<Vector3> centers,
            out IReadOnlyList<EnemyController> liveEnemies)
        {
            centers = null;
            liveEnemies = null;
            if (round < 1 || round > ActivatedRounds.Length || !ActivatedRounds[round - 1]) return false;
            int start = Settings.RoundStart(round);
            int count = Settings.SquadsPerRound[round - 1];
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                if (squadAnchors == null || start + i >= squadAnchors.Length || squadAnchors[start + i] == null)
                    return false;
                points[i] = squadAnchors[start + i].position;
            }
            var targets = new List<EnemyController>();
            foreach (var enemy in enemies.OrderedTargets)
            {
                if (enemy != null && enemy.IsAlive && enemy.EncounterGroup > start
                    && enemy.EncounterGroup <= start + count) targets.Add(enemy);
            }
            centers = Array.AsReadOnly(points);
            liveEnemies = targets.AsReadOnly();
            return true;
        }
        internal static void MarkDead(int id)
        {
            if (Snapshot(id) != null) Snapshots[id].Dead = true;
        }

        private void Start()
        {
            enemies = GetComponent<EnemySystem>();
            if (SceneManager.GetActiveScene().name != "StartScene") return;
            EnsureRunStorage();
            for (int id = 0; id < Snapshots.Length; id++)
                if (Snapshots[id] != null && Snapshots[id].spawned && !Snapshots[id].Dead) Spawn(id);
            ActivateAvailableRound();
        }

        [Serializable] public sealed class SaveState { public bool[] rounds; public EnemySnapshot[] enemies; }
        public static SaveState CaptureState()
        {
            foreach (var member in UnityEngine.Object.FindObjectsByType<CampusEncounterMember>(FindObjectsSortMode.None)) member.CaptureNow();
            return new SaveState { rounds = ActivatedRounds, enemies = Snapshots };
        }
        public static void RestoreState(SaveState s) { ActivatedRounds = s.rounds; Snapshots = s.enemies; }

        private static void EnsureRunStorage()
        {
            if (ActivatedRounds.Length != 0) return;
            ActivatedRounds = new bool[Settings.SquadsPerRound.Length];
            Snapshots = new EnemySnapshot[Settings.EnemyCount];
        }

        public void ActivateAvailableRound()
        {
            if (enemies == null) enemies = GetComponent<EnemySystem>();
            var config = ConfigSystem.Instance?.Config;
            if (config?.Quests == null) return;
            EnsureRunStorage();
            int members = Settings.Formation.Length;
            for (int round = 1; round <= Settings.SquadsPerRound.Length; round++)
            {
                if (ActivatedRounds[round - 1]
                    || !config.Quests.TryGetPuzzleDefinition(round, out var definition)
                    || QuestProgress.ForRun(definition.QuestId).Stage != QuestStage.Travelling) continue;
                int startSquad = Settings.RoundStart(round);
                int count = Settings.SquadsPerRound[round - 1] * members;
                var positions = new Vector3[count];
                bool valid = squadAnchors != null && squadAnchors.Length == Settings.TotalSquads;
                for (int i = 0; valid && i < count; i++)
                {
                    var anchor = squadAnchors[startSquad + i / members];
                    valid = anchor != null && TrySpawnPoint(anchor.TransformPoint(Settings.Formation[i % members].offset), out positions[i]);
                }
                if (!valid)
                {
                    if (!warned) Debug.LogError($"Round {round} has missing or non-navigable squad anchors.", this);
                    warned = true;
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    int squad = startSquad + i / members;
                    int id = squad * members + i % members;
                    Snapshots[id] = new EnemySnapshot { spawned = true, Position = positions[i], Home = positions[i],
                        Rotation = squadAnchors[squad].rotation };
                }
                ActivatedRounds[round - 1] = true;
                for (int i = 0; i < count; i++) Spawn((startSquad * members) + i);
            }
        }

        public static bool TrySpawnPoint(Vector3 candidate, out Vector3 result)
        {
            result = default;
            // Keep samples on the candidate's floor; the search radius can also reach nearby roofs.
            if (!NavMesh.SamplePosition(candidate, out var nav, 4f, NavMesh.AllAreas)
                || Mathf.Abs(nav.position.y - candidate.y) > .6f) return false;
            // Probe the sampled floor locally, so overhead roofs and restored actors cannot mask it.
            foreach (var ground in Physics.RaycastAll(nav.position + Vector3.up * .75f, Vector3.down, 1.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (ground.collider.GetComponentInParent<EnemyController>() != null
                    || ground.collider.GetComponentInParent<Caffeine.Gameplay.Player.PlayerInputReader>() != null
                    || ground.normal.y < .7f || Mathf.Abs(ground.point.y - nav.position.y) > .6f) continue;
                result = nav.position;
                return true;
            }
            return false;
        }

        private void Spawn(int id)
        {
            var snapshot = Snapshots[id];
            if (snapshot == null || !snapshot.spawned || snapshot.Dead) return;
            if (!TrySpawnPoint(snapshot.Position, out var position))
            {
                Debug.LogError($"Enemy {id} cannot restore on the NavMesh.", this);
                return;
            }
            var enemy = enemies.SpawnTaskEnemy(Settings.Formation[id % Settings.Formation.Length].type,
                position, snapshot.Rotation, id / Settings.Formation.Length + 1, id);
            if (enemy != null)
            {
                enemy.ConfigureEncounterHome(snapshot.Home);
                enemy.gameObject.AddComponent<CampusEncounterMember>().Bind(id);
            }
        }
    }

    [RequireComponent(typeof(EnemyController))]
    public sealed class CampusEncounterMember : MonoBehaviour
    {
        private int id = -1;
        private HealthShield vitals;

        public void Bind(int stableId)
        {
            id = stableId;
            vitals = GetComponent<HealthShield>();
            vitals.Died += OnDied;
        }

        private void OnDisable() { if (vitals != null) vitals.Died -= OnDied; CaptureNow(); }
        private void Start()
        {
            var snapshot = CampusEncounterDirector.Snapshot(id);
            if (snapshot != null && snapshot.Health > 0f)
                vitals.RestoreForSceneTravel(snapshot.Health, snapshot.Shield);
            // A checkpoint may occur in the spawn frame, before HealthShield.Start.
            if (snapshot != null && snapshot.Health > 0f)
            {
                if (snapshot.combat != null) GetComponent<EnemyController>().RestoreState(snapshot.combat);
                if (snapshot.vitals != null) vitals.RestoreState(snapshot.vitals);
            }
        }

        private void OnDied(HealthShield source) => CampusEncounterDirector.MarkDead(id);

        public int StableId => id;
        public void CaptureNow()
        {
            if (Caffeine.Gameplay.Persistence.SaveGameService.Restoring) return;
            var snapshot = CampusEncounterDirector.Snapshot(id);
            if (snapshot == null || !snapshot.spawned || snapshot.Dead || vitals == null || !vitals.IsInitialized) return;
            snapshot.Position = transform.position;
            snapshot.Rotation = transform.rotation;
            snapshot.Health = vitals.CurrentHealth;
            snapshot.Shield = vitals.CurrentShield;
            snapshot.combat = GetComponent<EnemyController>().CaptureState();
            snapshot.vitals = vitals.CaptureState();
        }
    }
}
