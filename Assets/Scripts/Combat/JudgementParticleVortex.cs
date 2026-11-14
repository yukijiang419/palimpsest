using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    /// <summary>Analytic spherical orbits collapse into a fixed world-space cut centre.</summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class JudgementParticleVortex : MonoBehaviour
    {
        private readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[64];
        private ParticleSystem particles;
        private Vector3 center;
        private Quaternion orientation;

        public void Initialize(Vector3 origin, Vector3 forward)
        {
            particles = GetComponent<ParticleSystem>();
            center = origin;
            orientation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void LateUpdate()
        {
            if (particles == null || Time.deltaTime <= 0f) return;
            int count = particles.GetParticles(buffer);
            for (int i = 0; i < count; i++)
            {
                var particle = buffer[i];
                float age = 1f - particle.remainingLifetime / particle.startLifetime;
                Vector3 next = Position(particle.randomSeed, age);
                // The renderer stretches along the actual curved trajectory.
                particle.velocity = (Position(particle.randomSeed,
                    Mathf.Clamp01(age + 0.002f)) - next) / (particle.startLifetime * 0.002f);
                particle.position = next;
                buffer[i] = particle;
            }
            particles.SetParticles(buffer, count);
        }

        private Vector3 Position(uint seed, float age)
        {
            float index = seed - 1f;
            float height = 1f - 2f * (index + 0.5f) / 24f;
            float radius = 1.18f * (1f - Mathf.SmoothStep(0f, 1f, age));
            float angle = index * 2.399963f + age * Mathf.PI * 2.6f;
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - height * height));
            Vector3 orbit = new Vector3(Mathf.Cos(angle) * horizontal, height, Mathf.Sin(angle) * horizontal);
            return center + orientation * Quaternion.AngleAxis(age * 70f, Vector3.right) * orbit * radius;
        }
    }
}
