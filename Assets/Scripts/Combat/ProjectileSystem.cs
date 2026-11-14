using System.Collections.Generic;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.Pool;

namespace Caffeine.Gameplay.Combat
{

    // deal with the projectile
    [DisallowMultipleComponent]
    public sealed class ProjectileSystem : MonoBehaviour
    {
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private DamageSystem damageSystem;

        private readonly List<Projectile> activeProjectiles = new();
        private GameplayConfig.ProjectileSettings projectileSettings;
        private GameplayConfig.EnemySettings enemySettings;
        private ObjectPool<Projectile> projectilePool;
        private bool shuttingDown;

        private void Start()
        {
            projectileSettings = ConfigSystem.Instance.Config.Projectile;
            enemySettings = ConfigSystem.Instance.Config.Enemy;


            // use object pool to reuse the gameobjects
            projectilePool = new ObjectPool<Projectile>(
                CreateProjectile,
                projectile => projectile.gameObject.SetActive(true),
                DeactivateProjectile,
                projectile => Destroy(projectile.gameObject));
        }

        private void FixedUpdate()
        {
            for (var index = activeProjectiles.Count - 1; index >= 0; index--)
            {
                activeProjectiles[index].Simulate(Time.fixedDeltaTime, Time.time);
            }
        }


        // get a projectile from the pool and activate it
        public void SpawnEnemyShot(Vector3 origin, Vector3 direction)
        {
            Projectile projectile = projectilePool.Get();
            projectile.Activate(
                origin,
                direction,
                projectileSettings.Speed,
                enemySettings.ProjectileDamage,
                projectileSettings.Lifetime);
            activeProjectiles.Add(projectile);
            CombatAudioController.Play(CombatSound.EnemyShot, origin);
        }


        // custom init function for the projectile
        private Projectile CreateProjectile()
        {
            Projectile projectile = Instantiate(projectilePrefab, transform);
            projectile.gameObject.SetActive(false);
            projectile.Initialize(damageSystem, ReleaseProjectile);
            return projectile;
        }


        // custom destroy function for the projectile
        private void DeactivateProjectile(Projectile projectile)
        {
            activeProjectiles.Remove(projectile);
            projectile.Deactivate();
            projectile.gameObject.SetActive(false);
        }

        private void ReleaseProjectile(Projectile projectile)
        {
            if (!shuttingDown)
            {
                projectilePool.Release(projectile);
            }
        }


        // when the gameobject is destroyed, clear the active projectiles and the pool
        private void OnDestroy()
        {
            shuttingDown = true;
            foreach (Projectile projectile in activeProjectiles)
            {
                Destroy(projectile.gameObject);
            }

            activeProjectiles.Clear();
            projectilePool?.Clear();
        }
    }
}
