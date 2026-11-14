using UnityEngine;
using UnityEngine.Rendering;

namespace Caffeine.Gameplay.Rendering
{
    public static class ArchiveEffectBurst
    {
        public static ParticleSystem Play(Transform owner, Vector3 position, Color color,
            int count, float seconds, float radius, bool fragments, float rise, Vector3[] origins = null)
        {
            var visual = new GameObject(fragments ? "Archive effect fragments" : "Archive effect motes");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(visual, owner.gameObject.scene);
            visual.transform.position = position;
            var particles = visual.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = .06f;
            main.startLifetime = seconds;
            main.startSpeed = 0f;
            main.startSize = .12f;
            main.maxParticles = Mathf.Max(1, count);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var fading = particles.colorOverLifetime;
            fading.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.8f, 0f), new GradientAlphaKey(.55f, .45f), new GradientAlphaKey(0f, 1f) });
            fading.color = fade;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>(fragments ? "Effects/ArchiveFragments" : "Effects/ArchiveMotes");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (fragments)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = Resources.Load<Mesh>("Effects/ArchiveFragment");
                main.startRotation3D = true;
                var rotation = particles.rotationOverLifetime;
                rotation.enabled = true;
                rotation.separateAxes = true;
                rotation.x = 2.4f;
                rotation.y = 1.7f;
                rotation.z = .8f;
            }
            particles.Play();
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.399963f;
                float height = (i + .5f) / Mathf.Max(1, count);
                Vector3 ray = new Vector3(Mathf.Cos(angle), fragments ? height * 1.6f - .8f : .1f, Mathf.Sin(angle));
                Vector3 origin = origins == null ? position : origins[i % origins.Length];
                var particle = new ParticleSystem.EmitParams
                {
                    position = origin + ray * radius * (.35f + .65f * height),
                    velocity = ray * (fragments ? 1.6f : .35f) + Vector3.up * rise,
                    startColor = color,
                    startLifetime = seconds * (.75f + .25f * height),
                    startSize = fragments ? .12f + .1f * height : .08f + .13f * height,
                    rotation3D = new Vector3(i * 47f, i * 31f, i * 73f)
                };
                particles.Emit(particle, 1);
            }
            return particles;
        }
    }
}
