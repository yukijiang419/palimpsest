using System;
using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    // a bullet
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField] private Rigidbody projectileBody;

        private DamageSystem damageSystem;
        private Action<Projectile> returnToPool;
        private Vector3 velocity;
        private float damage;
        private float expiresAt;
        private bool active;

        internal void Initialize(DamageSystem requiredDamageSystem, Action<Projectile> release)
        {
            damageSystem = requiredDamageSystem;
            returnToPool = release;
        }

        internal void Activate(
            Vector3 origin,
            Vector3 direction,
            float speed,
            float baseDamage,
            float lifetime)
        {
            velocity = direction.normalized * speed;
            damage = baseDamage;
            expiresAt = Time.time + lifetime;
            active = true;
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
            projectileBody.position = origin;
            ResetBody();
        }

        internal void Simulate(float deltaTime, float currentTime)
        {
            if (currentTime >= expiresAt)
            {
                Release();
                return;
            }

            projectileBody.MovePosition(projectileBody.position + velocity * deltaTime);
        }

        internal void Deactivate()
        {
            active = false;
            velocity = Vector3.zero;
            ResetBody();
        }

        /// handle collision with other objects
        /// if the projectile hits an enemy, apply damage and release the projectile back to the pool
        private void OnTriggerEnter(Collider other)
        {
            if (!active)
            {
                return;
            }

            HealthShield target = other.GetComponentInParent<HealthShield>();
            if (target && target.Faction == Faction.Enemy)
            {
                return;
            }

            if (target)
            {
                damageSystem.Resolve(
                    target,
                    new DamageRequest(DamageSource.NormalAttack, Faction.Enemy, damage), other.ClosestPoint(transform.position));
            }

            Release();
        }

        private void Release()
        {
            if (!active)
            {
                return;
            }

            active = false;
            returnToPool(this);
        }

        private void ResetBody()
        {
            projectileBody.linearVelocity = Vector3.zero;
            projectileBody.angularVelocity = Vector3.zero;
        }
    }
}
