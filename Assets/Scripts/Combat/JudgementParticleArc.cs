using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    /// <summary>Moves seeded particle arcs through a curved cut, implosion or recoil pulse.</summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class JudgementParticleArc : MonoBehaviour
    {
        public enum Motion { Sweep, Implode, Expand }
        private readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[64];
        private ParticleSystem particles;
        private Vector3 center;
        private Quaternion frame;
        private float radius, arc;
        private int count;
        private Motion motion;

        public void Initialize(Vector3 origin, Vector3 direction, float size, float degrees,
            float tilt, int particleCount, Motion movement)
        {
            particles = GetComponent<ParticleSystem>();
            center = origin;
            frame = Quaternion.AngleAxis(tilt, direction) * Quaternion.LookRotation(direction, Vector3.up);
            radius = size;
            arc = degrees;
            count = particleCount;
            motion = movement;
        }

        private void LateUpdate()
        {
            if (particles == null || Time.deltaTime <= 0f) return;
            int alive = particles.GetParticles(buffer);
            for (int i = 0; i < alive; i++)
            {
                var particle = buffer[i];
                float age = Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);
                Vector3 point = Position(particle.randomSeed, age);
                float nextAge = Mathf.Min(1f, age + 0.002f);
                particle.velocity = nextAge > age
                    ? (Position(particle.randomSeed, nextAge) - point) / ((nextAge - age) * particle.startLifetime)
                    : Vector3.zero;
                particle.position = point;
                buffer[i] = particle;
            }
            particles.SetParticles(buffer, alive);
        }

        private Vector3 Position(uint seed, float age)
        {
            float t = (seed - 0.5f) / count;
            float ease = Mathf.SmoothStep(0f, 1f, age);
            float size = motion == Motion.Implode ? radius * (1f - ease)
                : motion == Motion.Expand ? radius * (0.2f + 0.8f * Mathf.Sin(age * Mathf.PI * 0.5f))
                : radius * (1f - 0.18f * ease);
            float spin = motion == Motion.Implode ? 55f : motion == Motion.Expand ? -18f : 28f;
            float angle = (Mathf.Lerp(-arc * 0.5f, arc * 0.5f, t) + spin * ease) * Mathf.Deg2Rad;
            float ripple = Mathf.Sin(t * Mathf.PI * 6f + age * Mathf.PI * 2f)
                * 0.025f * Mathf.Sin(age * Mathf.PI);
            return center + frame * new Vector3(Mathf.Sin(angle) * size, ripple, Mathf.Cos(angle) * size);
        }
    }
}
