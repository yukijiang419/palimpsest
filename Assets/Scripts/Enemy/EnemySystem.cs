using System;
using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace Caffeine.Gameplay.Enemy
{
    [DisallowMultipleComponent]
    public sealed class EnemySystem : MonoBehaviour
    {
        [SerializeField] private EnemyController lightSkirmisherPrefab;
        [SerializeField] private EnemyController meleeEnemyPrefab;
        [SerializeField] private EnemyController scannerEnemyPrefab;
        [SerializeField] private EnemyController[] placedEnemies = Array.Empty<EnemyController>();
        [SerializeField] private Transform[] rangedspawnTransforms = Array.Empty<Transform>();
        [SerializeField] private Transform[] meleespawnTransforms = Array.Empty<Transform>();
        [SerializeField] private Transform playerTransform;
        [SerializeField] private ProjectileSystem projectileSystem;
        [SerializeField] private MazeSystem mazeSystem;
        [SerializeField] private DamageSystem damageSystem;
        [SerializeField, Tooltip("Coin model dropped by dead enemies. When empty, coins go straight to the player.")]
        private GameObject coinPrefab;
        private readonly List<EnemyController> orderedTargets = new();
        private GameplayConfig.EnemySettings enemyConfig;
        private GameplayConfig.CoinSettings coinConfig;
        private readonly Queue<CoinDrop> pendingCoinDrops = new(32);
        private readonly List<CoinPickup> prewarmedCoins = new();
        private readonly List<CoinPickup> currentDropCoins = new();
        private ObjectPool<CoinPickup> coinPool;
        private Transform coinRoot;
        private CoinDrop currentDrop;
        private bool hasCurrentDrop;
        private bool warmingCoins;
        private bool shuttingDown;

        private struct CoinDrop
        {
            public PlayerWallet Wallet;
            public Vector3 Position;
            public int Total;
            public int Pieces;
            public int Index;
        }

        public IReadOnlyList<EnemyController> OrderedTargets => orderedTargets;


        private void Start()
        {
            enemyConfig = ConfigSystem.Instance.Config.Enemy;
            InitializeCoinPool();

            if (GetComponent<CampusEncounterDirector>() != null) return;

            if (placedEnemies.Length > 0)
            {
                for (int index = 0; index < placedEnemies.Length; index++)
                {
                    EnemyController enemy = placedEnemies[index];
                    if (enemy == null)
                    {
                        Debug.LogError($"Enemy placement {index} is missing.", this);
                        continue;
                    }

                    RegisterEnemy(enemy, index);
                }

                return;
            }

            SpawnGroup(enemyConfig.RangedCount, lightSkirmisherPrefab, rangedspawnTransforms);
            SpawnGroup(enemyConfig.MeleeCount, meleeEnemyPrefab, meleespawnTransforms);
        }

        internal void AlertGroup(int groupId)
        {
            foreach (EnemyController enemy in orderedTargets)
            {
                if (enemy != null && enemy.EncounterGroup == groupId)
                {
                    enemy.ForceAggro();
                }
            }
        }

        internal void Unregister(EnemyController enemy)
        {
            orderedTargets.Remove(enemy);
        }

        // Scatter the reward as several coins the player walks over to collect.
        internal void DropCoins(Vector3 position, int total)
        {
            if (total <= 0)
            {
                return;
            }

            PlayerWallet wallet = playerTransform.GetComponent<PlayerWallet>();
            if (coinPrefab == null)
            {
                wallet.AddCoins(total);
                return;
            }

            pendingCoinDrops.Enqueue(new CoinDrop
            {
                Wallet = wallet,
                Position = position + Vector3.up * coinConfig.SpawnHeight,
                Total = total,
                Pieces = Mathf.Clamp(coinConfig.MaxPieces, 1, total)
            });
        }

        private void InitializeCoinPool()
        {
            if (coinPrefab == null)
            {
                return;
            }

            coinConfig = ConfigSystem.Instance.Config.Coin;
            coinRoot = new GameObject("Coin pool").transform;
            coinRoot.SetParent(transform, false);
            prewarmedCoins.Capacity = coinConfig.PrewarmCount;
            currentDropCoins.Capacity = Mathf.Max(1, coinConfig.MaxPieces);
            coinPool = new ObjectPool<CoinPickup>(
                CreateCoin,
                actionOnRelease: coin => coin.Deactivate(),
                actionOnDestroy: coin =>
                {
                    if (coin != null)
                    {
                        Destroy(coin.Root.gameObject);
                    }
                },
                defaultCapacity: coinConfig.PrewarmCount,
                maxSize: coinConfig.MaxInactive);
            warmingCoins = coinConfig.PrewarmCount > 0;
        }

        private CoinPickup CreateCoin()
        {
            GameObject coin = Instantiate(coinPrefab, coinRoot);
            coin.SetActive(false);
            CoinPickup pickup = coin.GetComponentInChildren<CoinPickup>(true);
            if (pickup == null)
            {
                pickup = coin.AddComponent<CoinPickup>();
            }

            pickup.Initialize(coin.transform, ReleaseCoin);
            return pickup;
        }

        private void ReleaseCoin(CoinPickup coin)
        {
            if (shuttingDown)
            {
                return;
            }

            currentDropCoins.Remove(coin);
            coinPool.Release(coin);
        }

        private void FinishPrewarm()
        {
            foreach (CoinPickup coin in prewarmedCoins)
            {
                coinPool.Release(coin);
            }

            prewarmedCoins.Clear();
            warmingCoins = false;
        }

        private void Update()
        {
            if (coinPool == null || shuttingDown)
            {
                return;
            }

            int created = 0;
            if (warmingCoins)
            {
                if (pendingCoinDrops.Count == 0)
                {
                    // Hold distinct instances until prewarming finishes.
                    int target = Mathf.Min(coinConfig.PrewarmCount, coinConfig.MaxInactive);
                    while (prewarmedCoins.Count < target && created < coinConfig.MaxCreatesPerFrame)
                    {
                        prewarmedCoins.Add(coinPool.Get());
                        created++;
                    }

                    if (prewarmedCoins.Count >= target)
                    {
                        FinishPrewarm();
                    }

                    return;
                }

                FinishPrewarm();
            }

            if (Time.timeScale <= 0f)
            {
                return;
            }

            int spawned = 0;
            while (spawned < coinConfig.MaxSpawnsPerFrame)
            {
                if (!hasCurrentDrop)
                {
                    if (pendingCoinDrops.Count == 0)
                    {
                        break;
                    }

                    currentDrop = pendingCoinDrops.Dequeue();
                    hasCurrentDrop = true;
                }

                if (currentDrop.Wallet == null)
                {
                    hasCurrentDrop = false;
                    currentDropCoins.Clear();
                    continue;
                }

                bool needsCreation = coinPool.CountInactive == 0;
                if (needsCreation && created >= coinConfig.MaxCreatesPerFrame)
                {
                    break;
                }

                CoinPickup pickup = coinPool.Get();
                if (needsCreation)
                {
                    created++;
                }

                int value = currentDrop.Total / currentDrop.Pieces
                    + (currentDrop.Index < currentDrop.Total % currentDrop.Pieces ? 1 : 0);
                Vector2 scatter = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.4f, 1f);
                Vector3 velocity = new Vector3(scatter.x, 0f, scatter.y) * coinConfig.ScatterSpeed
                    + Vector3.up * coinConfig.PopSpeed * UnityEngine.Random.Range(0.8f, 1.2f);

                Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * coinPrefab.transform.rotation;
                pickup.Root.SetPositionAndRotation(currentDrop.Position, rotation);
                pickup.Launch(currentDrop.Wallet, value, velocity, coinConfig);
                foreach (CoinPickup previous in currentDropCoins)
                {
                    pickup.IgnoreCollisionsWith(previous);
                }

                currentDropCoins.Add(pickup);
                currentDrop.Index++;
                spawned++;
                if (currentDrop.Index == currentDrop.Pieces)
                {
                    currentDrop = default;
                    hasCurrentDrop = false;
                    currentDropCoins.Clear();
                }
            }
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            pendingCoinDrops.Clear();
            prewarmedCoins.Clear();
            currentDropCoins.Clear();
            currentDrop = default;
            coinPool?.Clear();
            if (coinRoot != null)
            {
                Destroy(coinRoot.gameObject);
            }
        }

        private void SpawnGroup(int count, EnemyController prefab, Transform[] spawnTransforms)
        {
            // Scenes without campus spawn points (Boss arena) simply spawn nothing here.
            for (int index = 0; index < count && index < spawnTransforms.Length; index++)
            {
                Transform spawn = spawnTransforms[index];
                if (spawn == null) continue;
                Vector3 position = spawn.position;
                if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    position = hit.point;
                }

                EnemyController enemy = Instantiate(prefab, position, spawn.rotation, transform);
                RegisterEnemy(enemy, index);
            }
        }

        // Spawns an enemy at runtime (Boss minions) and registers it like a placed one,
        // so it can be targeted by melee and Path Interaction.
        public EnemyController SpawnEnemy(EnemyController prefab, Vector3 position, Quaternion rotation)
        {
            if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                position = hit.point;
            }

            EnemyController enemy = Instantiate(prefab, position, rotation, transform);
            RegisterEnemy(enemy, orderedTargets.Count);
            return enemy;
        }

        public EnemyController SpawnTaskEnemy(EnemyType type, Vector3 position, Quaternion rotation,
            int encounterGroup, int stableIndex)
        {
            var prefab = type switch
            {
                EnemyType.Ranged => lightSkirmisherPrefab,
                EnemyType.Scanner => scannerEnemyPrefab,
                _ => meleeEnemyPrefab
            };
            if (prefab == null) return null;
            var enemy = Instantiate(prefab, position, rotation, transform);
            enemy.ConfigureEncounterGroup(encounterGroup);
            RegisterEnemy(enemy, stableIndex);
            return enemy;
        }

        private void RegisterEnemy(EnemyController enemy, int index)
        {
            int strafeSign = (index & 1) == 0 ? -1 : 1;
            enemy.Register(this, strafeSign, playerTransform, projectileSystem, mazeSystem, damageSystem);
            orderedTargets.Add(enemy);
        }
        [Serializable] public sealed class DropState { public Vector3 position; public int total, pieces, index; }
        [Serializable] public sealed class SaveState { public CoinPickup.SaveState[] coins; public DropState[] drops; }
        public SaveState CaptureState()
        {
            var coins = new List<CoinPickup.SaveState>();
            foreach(var coin in GetComponentsInChildren<CoinPickup>(true)) { var s=coin.CaptureState(); if(s!=null) coins.Add(s); }
            var drops = new List<DropState>();
            if(hasCurrentDrop) drops.Add(new DropState { position=currentDrop.Position,total=currentDrop.Total,pieces=currentDrop.Pieces,index=currentDrop.Index });
            foreach(var d in pendingCoinDrops) drops.Add(new DropState { position=d.Position,total=d.Total,pieces=d.Pieces,index=d.Index });
            return new SaveState { coins=coins.ToArray(),drops=drops.ToArray() };
        }
        public void RestoreState(SaveState s)
        {
            foreach(var coin in GetComponentsInChildren<CoinPickup>(true)) if(coin.CaptureState()!=null) coin.Deactivate();
            pendingCoinDrops.Clear(); hasCurrentDrop=false;
            var wallet=playerTransform.GetComponent<PlayerWallet>();
            foreach(var d in s.drops) pendingCoinDrops.Enqueue(new CoinDrop { Wallet=wallet,Position=d.position,Total=d.total,Pieces=d.pieces,Index=d.index });
            foreach(var c in s.coins) { var coin=coinPool.Get(); coin.RestoreState(c,wallet,coinConfig); }
        }

    }
}
