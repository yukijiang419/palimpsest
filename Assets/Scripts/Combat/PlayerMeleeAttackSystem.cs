using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Presentation;
using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    // player's melee attack system
    public sealed class PlayerMeleeAttackSystem : MonoBehaviour
    {
        [SerializeField] private PlayerAttackController attackController;
        [SerializeField] private EnemySystem enemySystem;
        [SerializeField] private DamageSystem damageSystem;
        [SerializeField] private GameTimeController timeController;
        [SerializeField] private WeaponManager weaponManager;

        private GameplayConfig.AttackSettings attackSettings;
        private PlayerTargetLock targetLock;
        private Material slashMaterial;
        private Material particleMaterial;
        private Texture2D particleTexture;
        private Material judgementSlashMaterial;
        private Material judgementDistortionMaterial;
        private void Start()
        {
            attackSettings = ConfigSystem.Instance.Config.Attack;
            targetLock = attackController.GetComponent<PlayerTargetLock>();
            if (weaponManager == null)
            {
                weaponManager = attackController.GetComponentInChildren<WeaponManager>();
            }
        }

        private void OnEnable()
        {
            attackController.AttackImpactRequested += HandleAttackImpactRequested;
        }

        private void OnDisable()
        {
            attackController.AttackImpactRequested -= HandleAttackImpactRequested;
        }


        private bool HasLineOfSight(Vector3 origin, Vector3 target, Transform targetRoot)
        {
            Vector3 start = origin + Vector3.up;
            Vector3 delta = target + Vector3.up - start;
            foreach (RaycastHit hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(attackController.transform) || hit.transform.IsChildOf(targetRoot)) continue;
                if (hit.transform.GetComponentInParent<EnemyController>() != null
                    || hit.transform.GetComponentInParent<BossController>() != null) continue;
                return false;
            }
            return true;
        }

        private void ShowSlash(Vector3 origin, Vector3 direction, float range, float arc)
        {
            SwordMove move = attackController.CurrentMove;
            Color color = move switch
            {
                SwordMove.ReturnCut or SwordMove.RapidCut => new Color(2.2f, 0.66f, 0.25f),
                SwordMove.Finisher => new Color(1.4f, 1.8f, 2.3f),
                _ => new Color(0.28f, 1.35f, 2.1f)
            };
            float width = move == SwordMove.Finisher ? 0.46f
                : move == SwordMove.RapidCut ? 0.14f : 0.27f;
            float lifetime = move == SwordMove.RapidCut ? 0.12f : 0.26f;
            float radius = Mathf.Min(range * 0.83f, 2.8f);
            float tilt = SweepTilt(move);
            Vector3 center = origin + Vector3.up * 1.08f;
            if (move == SwordMove.PrecisionCut)
            {
                StartCoroutine(ShowJudgementCut(center, direction));
                return;
            }
            CreateRibbon(move + " sword edge", center, direction, radius, arc,
                width, color, lifetime, tilt);
            CreateRibbon(move + " sword core", center, direction, radius, arc,
                width * 0.20f, new Color(1.8f, 2f, 2.2f), lifetime * 0.65f, tilt);
            ShowSlashParticles(origin, direction, range, arc);
        }

        private System.Collections.IEnumerator ShowJudgementCut(Vector3 center, Vector3 direction)
        {
            // Fixed at the resolved hit position: a small spatial cutting field,
            // with staggered incisions instead of an ordinary sword-swing crescent.
            Color violet = new Color(0.65f, 0.42f, 1.8f);
            Color ice = new Color(0.4f, 1.35f, 2.4f);
            ShowJudgementDistortion(center);
            ShowJudgementVortex(center, direction, violet, ice);
            yield return new WaitForSeconds(0.055f);
            EmitJudgementArc("Judgement boundary particles", center, direction, 1.1f,
                360f, 72f, violet * 0.6f, 0.32f, true);
            var motes = CreateBurst("Judgement inward fragments", center, 0.38f);
            var main = motes.main;
            main.gravityModifier = 0f;
            var drag = motes.limitVelocityOverLifetime;
            drag.enabled = false;
            for (int i = 0; i < 36; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / 36f;
                float radial = Mathf.Sqrt(1f - y * y);
                float angle = i * 2.399963f;
                Vector3 ray = new Vector3(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
                motes.Emit(new ParticleSystem.EmitParams
                {
                    position = center + ray * 1.08f,
                    velocity = -ray * 2.6f,
                    startLifetime = 0.30f + (i % 4) * 0.02f,
                    startColor = i % 3 == 0 ? violet : ice,
                    startSize = i % 5 == 0 ? 0.09f : 0.035f
                }, 1);
            }
            for (int wave = 0; wave < 4; wave++)
            {
                for (int cut = 0; cut < 2; cut++)
                {
                    int index = wave * 2 + cut;
                    Vector3 axis = Quaternion.AngleAxis(index * 137.5f, Vector3.up) * direction;
                    float tilt = -78f + Mathf.Repeat(index * 53f, 156f);
                    float radius = 0.9f + (index % 3) * 0.07f;
                    float arc = 145f + (index % 2) * 65f;
                    EmitJudgementArc("Judgement incision particles", center, axis, radius,
                        arc, tilt, index % 2 == 0 ? ice : violet, 0.19f, false);
                }
                yield return new WaitForSeconds(0.035f);
            }
            var flash = CreateBurst("Judgement collapse flash", center, 0.13f);
            flash.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            flash.Emit(new ParticleSystem.EmitParams
            {
                position = center,
                startColor = new Color(0.85f, 1.25f, 2.2f),
                startSize = 0.8f,
                startLifetime = 0.13f
            }, 1);
            EmitJudgementArc("Judgement collapse particles", center, direction, 0.88f,
                360f, -70f, ice, 0.17f, true);
            yield return new WaitForSeconds(0.10f);
            EmitJudgementArc("Judgement recoil pulse", center, direction, 1.35f,
                360f, 12f, violet * 0.55f, 0.24f, false, true);
        }

        private void ShowJudgementDistortion(Vector3 center)
        {
            if (judgementDistortionMaterial == null)
            {
                Material source = Resources.Load<Material>("Effects/JudgementDistortion");
                if (source == null || !source.shader.isSupported) return;
                judgementDistortionMaterial = new Material(source);
                ConfigureJudgementMaterial(judgementDistortionMaterial, true);
            }
            var field = CreateBurst("Judgement refraction field", center, 0.46f);
            var renderer = field.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = judgementDistortionMaterial;
            var colors = field.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.2f),
                    new GradientAlphaKey(0.55f, 0.65f), new GradientAlphaKey(0f, 1f) });
            colors.color = gradient;
            field.Emit(new ParticleSystem.EmitParams
            {
                position = center,
                startSize = 2.6f,
                startLifetime = 0.46f,
                startColor = Color.white
            }, 1);
        }

        private Material JudgementSlashMaterial()
        {
            if (judgementSlashMaterial != null) return judgementSlashMaterial;
            Material source = Resources.Load<Material>("Effects/JudgementSlash");
            if (source == null || !source.shader.isSupported) return ParticleMaterial();
            judgementSlashMaterial = new Material(source);
            ConfigureJudgementMaterial(judgementSlashMaterial, false);
            return judgementSlashMaterial;
        }

        private static void ConfigureJudgementMaterial(Material material, bool refraction)
        {
            // Both materials use the same shader, with separate shading modes and blending.
            material.SetFloat("_Refraction", refraction ? 1f : 0f);
            material.SetFloat("_DstBlend", refraction
                ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                : (float)UnityEngine.Rendering.BlendMode.One);
            material.renderQueue = refraction ? 3005 : 3010;
        }

        private void ShowJudgementVortex(Vector3 center, Vector3 direction, Color violet, Color ice)
        {
            var vortex = CreateBurst("Judgement spiral gathering", center, 0.43f);
            var main = vortex.main;
            main.gravityModifier = 0f;
            var drag = vortex.limitVelocityOverLifetime;
            drag.enabled = false;
            var colors = vortex.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(violet, 0f),
                new GradientColorKey(ice, 0.55f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.12f),
                    new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) });
            colors.color = gradient;
            var trails = vortex.trails;
            trails.enabled = true;
            trails.ratio = 0.65f;
            trails.lifetime = 0.08f;
            trails.minVertexDistance = 0.035f;
            trails.worldSpace = true;
            trails.inheritParticleColor = true;
            trails.sizeAffectsWidth = true;
            trails.dieWithParticles = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.Linear(0f, 0.55f, 1f, 0f));
            var vortexRenderer = vortex.GetComponent<ParticleSystemRenderer>();
            vortexRenderer.trailMaterial = ParticleMaterial();
            vortexRenderer.velocityScale = 0.008f;
            vortexRenderer.lengthScale = 1.4f;
            vortex.gameObject.AddComponent<JudgementParticleVortex>().Initialize(center, direction);

            // Each converging particle releases one short-lived residual spark on death.
            var residual = CreateBurst("Judgement residual subemitter", center, 0.14f, 0.7f);
            residual.transform.SetParent(vortex.transform, true);
            residual.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var residualMain = residual.main;
            residualMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            residualMain.startSize = 0.045f;
            residualMain.startColor = ice;
            var residualEmission = residual.emission;
            residualEmission.enabled = true;
            residualEmission.rateOverTime = 0f;
            residualEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var shape = residual.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.025f;
            var subEmitters = vortex.subEmitters;
            subEmitters.enabled = true;
            subEmitters.AddSubEmitter(residual, ParticleSystemSubEmitterType.Death,
                ParticleSystemSubEmitterProperties.InheritNothing);
            for (int i = 0; i < 24; i++)
            {
                float height = 1f - 2f * (i + 0.5f) / 24f;
                float horizontal = Mathf.Sqrt(1f - height * height);
                Vector3 initial = new Vector3(Mathf.Cos(i * 2.399963f) * horizontal,
                    height, Mathf.Sin(i * 2.399963f) * horizontal);
                vortex.Emit(new ParticleSystem.EmitParams
                {
                    position = center + Quaternion.LookRotation(direction, Vector3.up) * initial * 1.18f,
                    randomSeed = (uint)(i + 1),
                    startLifetime = 0.32f + (i % 4) * 0.035f,
                    startSize = i % 4 == 0 ? 0.055f : 0.028f,
                    startColor = Color.white
                }, 1);
            }
        }

        private void EmitJudgementArc(string name, Vector3 center, Vector3 direction,
            float radius, float arc, float tilt, Color color, float lifetime, bool implode, bool recoil = false)
        {
            var particles = CreateBurst(name, center, lifetime);
            var main = particles.main;
            main.gravityModifier = 0f;
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = implode ? 1.2f : 3.4f;
            if (!implode && !recoil)
            {
                renderer.sharedMaterial = JudgementSlashMaterial();
                // The authored mask has a fine core, so give its outer glow room.
                renderer.lengthScale = 2.5f;
            }
            Quaternion plane = Quaternion.AngleAxis(tilt, direction);
            int count = recoil ? 32 : implode ? 48 : 28;
            var movement = recoil ? JudgementParticleArc.Motion.Expand
                : implode ? JudgementParticleArc.Motion.Implode : JudgementParticleArc.Motion.Sweep;
            particles.gameObject.AddComponent<JudgementParticleArc>().Initialize(
                center, direction, radius, arc, tilt, count, movement);
            if (recoil)
            {
                renderer.velocityScale = 0.015f;
                renderer.lengthScale = 1.2f;
                var colors = particles.colorOverLifetime;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.15f), new GradientAlphaKey(0f, 1f) });
                colors.color = gradient;
            }
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                Vector3 radial = Quaternion.AngleAxis(Mathf.Lerp(-arc * 0.5f, arc * 0.5f, t), Vector3.up) * direction;
                Vector3 normal = plane * radial;
                Vector3 tangent = plane * Vector3.Cross(Vector3.up, radial);
                float taper = implode ? 1f : Mathf.Sqrt(Mathf.Sin(t * Mathf.PI));
                float variation = Mathf.Repeat(i * 0.618034f, 1f);
                particles.Emit(new ParticleSystem.EmitParams
                {
                    position = center + normal * radius * (recoil ? 0.2f : 1f),
                    randomSeed = (uint)(i + 1),
                    velocity = implode ? -normal * (radius / lifetime * 0.85f)
                        : tangent * (1.3f + variation * 0.7f) - normal * 0.65f,
                    startColor = !implode && i % 4 == 0 ? new Color(1.7f, 1.9f, 2.4f) : color,
                    startSize = (recoil ? 0.04f : implode ? 0.035f : 0.15f) * taper * (0.85f + variation * 0.3f),
                    startLifetime = lifetime * (0.8f + variation * 0.2f)
                }, 1);
            }
        }

        private static float SweepTilt(SwordMove move) => move switch
        {
            SwordMove.Cleave => -18f,
            SwordMove.ReturnCut => 24f,
            SwordMove.Finisher => -42f,
            SwordMove.PrecisionCut => 65f,
            SwordMove.RapidCut => 12f,
            _ => 0f
        };

        private void CreateRibbon(string name, Vector3 center, Vector3 direction, float radius,
            float arc, float width, Color color, float lifetime, float tilt = 0f, float endScale = 1.06f)
        {
            if (slashMaterial == null)
            {
                slashMaterial = NewEnergyMaterial("Archive cut energy");
                if (slashMaterial == null) return;
            }
            bool ring = arc >= 359f;
            int segments = ring ? 48 : 32;
            var vertices = new Vector3[(segments + 1) * 3];
            var colors = new Color[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 12];
            bool reverse = attackController.CurrentMove == SwordMove.ReturnCut;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(-arc * 0.5f, arc * 0.5f, reverse ? 1f - t : t);
                Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                float taper = ring ? 1f : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.55f);
                float centerRadius = radius * (ring ? 1f : 0.92f + 0.08f * Mathf.Sin(t * Mathf.PI));
                float halfWidth = width * taper * 0.5f;
                float lift = ring ? 0f : 0.12f * Mathf.Sin(t * Mathf.PI);
                int vertex = i * 3;
                vertices[vertex] = radial * (centerRadius - halfWidth) + Vector3.up * lift;
                vertices[vertex + 1] = radial * centerRadius + Vector3.up * (lift + 0.015f);
                vertices[vertex + 2] = radial * (centerRadius + halfWidth) + Vector3.up * lift;
                colors[vertex] = new Color(0.35f, 0.75f, 1f, 0f);
                colors[vertex + 1] = new Color(1f, 1f, 1f, 0.9f * taper);
                colors[vertex + 2] = new Color(0.35f, 0.75f, 1f, 0f);
                uv[vertex] = new Vector2(t, 0f);
                uv[vertex + 1] = new Vector2(t, 0.5f);
                uv[vertex + 2] = new Vector2(t, 1f);
                if (i == segments) continue;
                int index = i * 12;
                for (int band = 0; band < 2; band++)
                {
                    int a = vertex + band;
                    int b = vertex + 3 + band;
                    triangles[index++] = a;
                    triangles[index++] = b;
                    triangles[index++] = a + 1;
                    triangles[index++] = a + 1;
                    triangles[index++] = b;
                    triangles[index++] = b + 1;
                }
            }
            var mesh = new Mesh { name = name + " ribbon" };
            Quaternion plane = Quaternion.AngleAxis(tilt, direction);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = plane * vertices[i];
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            var visual = new GameObject(name);
            visual.transform.position = center;
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = slashMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            visual.AddComponent<CombatSlashVisual>().Initialize(mesh, renderer, color, lifetime, !ring, endScale);
        }

        private ParticleSystem CreateBurst(string name, Vector3 position, float lifetime, float cleanupDelay = 0.2f)
        {
            var visual = new GameObject(name);
            visual.transform.position = position;
            var particles = visual.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.06f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = 0f;
            main.startSize = 0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            main.gravityModifier = name == "Archive impact sparks" ? 0.35f : 0.04f;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var fading = particles.colorOverLifetime;
            fading.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fading.color = fade;
            var shrinking = particles.sizeOverLifetime;
            shrinking.enabled = true;
            shrinking.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.65f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f)));
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 1.4f;
            drag.dampen = 0.18f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.lengthScale = 1.3f;
            particles.Play();
            Destroy(visual, lifetime + cleanupDelay);
            return particles;
        }

        private Material ParticleMaterial()
        {
            if (particleMaterial != null) return particleMaterial;
            particleTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                name = "Archive soft mote",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x + 0.5f - 32f) / 31.5f;
                float dy = (y + 0.5f - 32f) / 31.5f;
                float fade = Mathf.Pow(Mathf.Clamp01(1f - dx * dx - dy * dy), 2f);
                pixels[y * 64 + x] = new Color(1f, 1f, 1f, fade);
            }
            particleTexture.SetPixels32(pixels);
            particleTexture.Apply();
            particleMaterial = NewEnergyMaterial("Archive attack particles");
            if (particleMaterial == null)
                particleMaterial = new Material(Shader.Find("Sprites/Default"));
            particleMaterial.mainTexture = particleTexture;
            if (particleMaterial.HasProperty("_Tint"))
                particleMaterial.SetColor("_Tint", Color.white);
            return particleMaterial;
        }

        private static Material NewEnergyMaterial(string name)
        {
            Material source = Resources.Load<Material>("CombatEnergy");
            if (source != null) return new Material(source) { name = name };
            Shader shader = Shader.Find("Caffeine/Combat Energy");
            return shader != null ? new Material(shader) { name = name } : null;
        }

        private void ShowSlashParticles(Vector3 origin, Vector3 direction, float range, float arc)
        {
            SwordMove move = attackController.CurrentMove;
            int count = move == SwordMove.RapidCut ? 9 : move == SwordMove.Finisher ? 40 : 24;
            float lifetime = move == SwordMove.RapidCut ? 0.14f : move == SwordMove.Finisher ? 0.44f : 0.32f;
            var particles = CreateBurst("Archive sword motes", origin, lifetime);
            Color color = move switch
            {
                SwordMove.ReturnCut or SwordMove.RapidCut => new Color(1f, 0.55f, 0.27f),
                SwordMove.Finisher => new Color(0.88f, 0.97f, 1f),
                _ => new Color(0.35f, 0.87f, 1f)
            };
            float radius = Mathf.Min(range * 0.78f, 2.1f);
            Quaternion plane = Quaternion.AngleAxis(SweepTilt(move), direction);
            if (move == SwordMove.PrecisionCut) radius = 1.15f;
            for (int i = 0; i < count; i++)
            {
                // Stable local variation avoids evenly spaced beads without consuming
                // Unity's shared random stream used by gameplay.
                float jitter = Mathf.Repeat(i * 0.618034f + 0.27f, 1f);
                float scatter = Mathf.Repeat(i * 0.754878f + 0.41f, 1f);
                float t = (i + 0.15f + jitter * 0.7f) / count;
                float angle = Mathf.Lerp(-arc * 0.5f, arc * 0.5f, t);
                Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                Vector3 tangent = plane * Vector3.Cross(Vector3.up, radial);
                if (move == SwordMove.ReturnCut) tangent = -tangent;
                Vector3 point = origin + Vector3.up * 1.08f + plane * (radial * (radius + (scatter - 0.5f) * 0.16f));
                Vector3 velocity = tangent * (move == SwordMove.RapidCut ? 1.2f : 2.1f) * (0.65f + scatter * 0.7f)
                    + plane * radial * (jitter - 0.35f) * 0.65f;
                if (move == SwordMove.Rush)
                {
                    // Leave sparks along the travelled corridor, including short blocked rushes.
                    point = Vector3.Lerp(attackController.RushStart, attackController.RushEnd, t)
                        + Vector3.up * (0.4f + (i % 4) * 0.2f)
                        + Vector3.Cross(Vector3.up, direction) * ((i % 2 == 0 ? -1f : 1f) * 0.22f);
                    velocity = -direction * 2.8f + Vector3.up * 0.35f;
                }
                var emission = new ParticleSystem.EmitParams
                {
                    position = point,
                    velocity = velocity + Vector3.up * 0.2f,
                    startColor = i % 5 == 0 ? new Color(1.5f, 1.7f, 1.9f) : color,
                    startSize = (i % 4 == 0 ? 0.105f : 0.045f) * (0.75f + scatter * 0.5f),
                    startLifetime = lifetime * (0.55f + jitter * 0.45f)
                };
                particles.Emit(emission, 1);
            }
        }

        private void ShowHitParticles(Vector3 position, bool shield, bool heavy)
        {
            Color flash = shield ? new Color(0.45f, 1.55f, 2.4f) : new Color(2.2f, 0.9f, 0.32f);
            CreateRibbon("Sword impact ring", position + Vector3.up, Vector3.forward,
                heavy ? 0.8f : 0.53f, 360f, heavy ? 0.34f : 0.22f,
                flash, heavy ? 0.31f : 0.23f);
            var particles = CreateBurst("Archive impact sparks", position + Vector3.up, heavy ? 0.42f : 0.28f);
            Color color = shield ? new Color(0.38f, 0.9f, 1f) : new Color(1f, 0.61f, 0.29f);
            var flashParticles = CreateBurst("Archive contact flash", position + Vector3.up, 0.1f);
            flashParticles.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            flashParticles.Emit(new ParticleSystem.EmitParams
            {
                position = position + Vector3.up,
                startColor = flash,
                startSize = heavy ? 0.65f : 0.36f,
                startLifetime = 0.1f
            }, 1);
            int count = heavy ? 28 : 16;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                // A spherical fan reads from above and from the side.
                float height = 1f - 2f * (i + 0.5f) / count;
                float radial = Mathf.Sqrt(1f - height * height);
                angle = i * 2.399963f;
                Vector3 ray = new Vector3(Mathf.Cos(angle) * radial, height, Mathf.Sin(angle) * radial);
                var emission = new ParticleSystem.EmitParams
                {
                    position = position + Vector3.up + ray * 0.06f,
                    velocity = ray * (heavy ? 4.4f : 3.1f) * (0.6f + Mathf.Repeat(i * 0.618034f, 1f) * 0.8f),
                    startColor = i % 4 == 0 ? Color.white : color,
                    startSize = i % 4 == 0 ? 0.11f : 0.045f,
                    startLifetime = (heavy ? 0.42f : 0.28f) * (0.5f + Mathf.Repeat(i * 0.754878f, 1f) * 0.5f)
                };
                particles.Emit(emission, 1);
            }
        }

        private void OnDestroy()
        {
            if (slashMaterial != null) Destroy(slashMaterial);
            if (particleMaterial != null) Destroy(particleMaterial);
            if (particleTexture != null) Destroy(particleTexture);
            if (judgementSlashMaterial != null) Destroy(judgementSlashMaterial);
            if (judgementDistortionMaterial != null) Destroy(judgementDistortionMaterial);
        }

        // Q prioritizes the view lock while normal melee keeps its captured attack direction.
        private Vector3 ResolveRangedCutCenter(Vector3 origin, Vector3 direction)
        {
            if (targetLock == null) targetLock = attackController.GetComponent<PlayerTargetLock>();
            Transform locked = targetLock != null && targetLock.IsLocked ? targetLock.LockedTransform : null;
            EnemyController lockedEnemy = targetLock != null ? targetLock.LockedEnemy : null;
            BossController boss = BossController.Current;
            bool lockedAlive = locked != null &&
                ((lockedEnemy != null && lockedEnemy.isActiveAndEnabled && lockedEnemy.IsAlive)
                || (boss != null && boss.transform == locked && boss.isActiveAndEnabled && boss.IsAlive));
            if (lockedAlive)
            {
                Vector3 offset = locked.position - origin;
                offset.y = 0f;
                if (offset.sqrMagnitude < 12f * 12f && HasLineOfSight(origin, locked.position, locked))
                    return locked.position;
            }

            // Without an available lock, keep the nearest visible target in the original forward cone.
            float closest = 12f;
            Vector3 center = origin + direction * 8f;
            foreach (EnemyController target in enemySystem.OrderedTargets)
            {
                if (target == null || !target.IsAlive) continue;
                Vector3 offset = target.transform.position - origin;
                offset.y = 0f;
                if (offset.magnitude < closest && Vector3.Dot(direction, offset.normalized) > 0.85f
                    && HasLineOfSight(origin, target.transform.position, target.transform))
                { closest = offset.magnitude; center = target.transform.position; }
            }
            if (boss != null && boss.IsAlive)
            {
                Vector3 offset = boss.transform.position - origin;
                offset.y = 0f;
                if (offset.magnitude < closest && Vector3.Dot(direction, offset.normalized) > 0.85f
                    && HasLineOfSight(origin, boss.transform.position, boss.transform))
                    center = boss.transform.position;
            }
            return center;
        }

        private bool CanHitTarget(Vector3 origin, Vector3 direction, float range,
            float minimumFacingDot, Transform target, float hitRadius = 0f)
        {
            Vector3 toTarget = target.position - origin;
            toTarget.y = 0f;
            float reach = range + hitRadius;
            bool inArc = toTarget.sqrMagnitude <= hitRadius * hitRadius
                || Vector3.Dot(direction, toTarget.normalized) >= minimumFacingDot;
            if (toTarget.sqrMagnitude <= reach * reach && inArc
                && HasLineOfSight(attackController.transform.position, target.position, target))
                return true;

            if (attackController.CurrentMove != SwordMove.Rush) return false;
            Vector3 segment = attackController.RushEnd - attackController.RushStart;
            segment.y = 0f;
            if (segment.sqrMagnitude < 0.000001f) return false;
            Vector3 fromStart = target.position - attackController.RushStart;
            fromStart.y = 0f;
            float progress = Mathf.Clamp01(Vector3.Dot(fromStart, segment) / segment.sqrMagnitude);
            Vector3 closest = Vector3.Lerp(attackController.RushStart, attackController.RushEnd, progress);
            Vector3 offset = target.position - closest;
            offset.y = 0f;
            float corridorReach = attackSettings.RushHitHalfWidth + hitRadius;
            return offset.sqrMagnitude <= corridorReach * corridorReach
                && HasLineOfSight(closest, target.position, target);
        }

        // handle the melee attack impact event
        private void HandleAttackImpactRequested(Vector3 direction)
        {
            direction.y = 0f;
            direction.Normalize();
            Vector3 origin = attackController.transform.position;
            CombatAudioController.Play(CombatSound.Swing, origin, false);

            // use the equipped weapon's range and arc, falling back to the global config
            WeaponData weapon = weaponManager != null ? weaponManager.Current : null;
            float range = weapon != null && weapon.range > 0f ? weapon.range : attackSettings.MeleeRange;
            float arcDegrees = weapon != null && weapon.arcDegrees > 0f ? weapon.arcDegrees : attackSettings.MeleeArcDegrees;
            float damage = weapon != null && weapon.damage > 0f ? weapon.damage : attackSettings.MeleeDamage;
            if (weapon != null)
            {
                damage += weaponManager.CurrentDamageBonus;
            }
            damage *= attackController.DamageMultiplier;
            range *= attackController.RangeMultiplier;
            arcDegrees = Mathf.Min(360f, arcDegrees * attackController.ArcMultiplier);
            if (attackController.IsRangedCut)
            {
                if (weapon != null && weapon.isTwoHanded)
                    damage *= attackSettings.TwoHandPrecisionDamageMultiplier;
                origin = ResolveRangedCutCenter(origin, direction);
                range = 1.8f;
                arcDegrees = 360f;
            }
            ShowSlash(origin, direction, range, arcDegrees);
            float minimumFacingDot = Mathf.Cos(arcDegrees * 0.5f * Mathf.Deg2Rad);

            DamageRequest request = new(
                DamageSource.NormalAttack,
                Faction.Player,
                damage);
            bool damagedTarget = false;

            foreach (EnemyController enemy in enemySystem.OrderedTargets)
            {
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                Vector3 toTarget = enemy.transform.position - origin;
                toTarget.y = 0f;
                if (!CanHitTarget(origin, direction, range, minimumFacingDot, enemy.transform))
                {
                    continue;
                }


                // apply damage to the enemy and trigger hit feedback
                // Damage can raise Died synchronously, so provide the ragdoll impulse first.
                enemy.RecordHitDirection(toTarget.normalized);
                Vector3 impact = enemy.GetComponent<CharacterController>().ClosestPoint(origin + Vector3.up);
                DamageResult result = damageSystem.Resolve(enemy.HealthShield, request, impact);
                if (!result.HasEffect)
                {
                    continue;
                }

                damagedTarget = true;
                ShowHitParticles(enemy.transform.position, result.ShieldLoss > 0f,
                    attackController.CurrentMove == SwordMove.Finisher);
                enemy.ApplyHitStun(attackSettings.HitStunDuration * (attackController.CurrentMove == SwordMove.Finisher ? 3f : 1f));
                enemy.PlayHitFeedback(attackSettings.HitFlashDuration, toTarget.normalized);
            }

            // The Boss is not a normal enemy (and not a Path target); its shield blocks hits until broken.
            BossController boss = BossController.Current;
            if (boss != null && boss.IsAlive)
            {
                if (CanHitTarget(origin, direction, range, minimumFacingDot, boss.transform, boss.HitRadius))
                {
                    Vector3 impact = boss.transform.position + Vector3.up + (origin - boss.transform.position).normalized * boss.HitRadius;
                    DamageResult result = damageSystem.Resolve(boss.Health, request, impact);
                    if (result.HasEffect)
                    {
                        damagedTarget = true;
                        ShowHitParticles(boss.transform.position, result.ShieldLoss > 0f,
                            attackController.CurrentMove == SwordMove.Finisher);
                        boss.PlayHit();
                    }
                }
            }

            if (damagedTarget)
            {
                timeController.RequestHitStop(attackSettings.HitStopDuration);
            }
        }
    }
}
