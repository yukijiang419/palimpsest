using UnityEngine;

namespace Caffeine.Gameplay.Enemy
{
    [DisallowMultipleComponent]
    public sealed class EnemyHitFeedback : MonoBehaviour
    {
        [SerializeField, Range(0.01f, 1f), Tooltip("Animation speed multiplier while absorbing a hit.")]
        private float hitAnimationSpeed = 0.2f;
        [SerializeField, Min(0f), Tooltip("Animation slowdown duration in game seconds, excluding pauses and hit stop.")]
        private float hitAnimationDuration = 0.16f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color FlashColor = Color.white;

        private Renderer[] renderers;
        private MaterialPropertyBlock[] savedBlocks;
        private MaterialPropertyBlock workBlock;
        private Material[][] originalMaterials;
        private readonly System.Collections.Generic.Dictionary<Material, Material> flashMaterials = new();
        private Transform visual;
        private Transform upperBody;
        private Vector3 worldRecoilDirection;
        private Vector3 visualBasePosition;
        private Quaternion visualBaseRotation;
        private Vector3 recoilDirection;
        private float recoilDistance;
        private float recoilDegrees;
        private float recoilDuration;
        private float recoilStartedAt;
        private float flashEndsAt;
        private bool flashing;
        private bool recoiling;
        private Animator characterAnimator;
        private float animationBaseSpeed;
        private float animationSlowEndsAt;
        private bool animationSlowed;

        private void Awake()
        {
            renderers = CharacterRenderers(this);
            savedBlocks = new MaterialPropertyBlock[renderers.Length];
            originalMaterials = new Material[renderers.Length][];
            for (int index = 0; index < renderers.Length; index++)
            {
                savedBlocks[index] = new MaterialPropertyBlock();
                originalMaterials[index] = renderers[index].sharedMaterials;
                Material[] materials = renderers[index].sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material source = materials[slot];
                    if (source == null || !source.HasProperty(EmissionId))
                    {
                        continue;
                    }

                    if (!flashMaterials.TryGetValue(source, out Material instance))
                    {
                        instance = new Material(source) { name = source.name + " (Hit Feedback)" };
                        // use emission for the hit flash because Standard textures already have a white tint
                        if (!source.IsKeywordEnabled("_EMISSION"))
                        {
                            instance.SetColor(EmissionId, Color.black);
                        }

                        instance.EnableKeyword("_EMISSION");
                        flashMaterials.Add(source, instance);
                    }
                    materials[slot] = instance;
                }
                renderers[index].sharedMaterials = materials;
            }
            workBlock = new MaterialPropertyBlock();

            characterAnimator = GetComponent<EnemyController>()?.VisualAnimator;
            if (characterAnimator == null)
            {
                characterAnimator = GetComponentInChildren<Animator>();
            }
            if (characterAnimator != null)
            {
                visual = characterAnimator.transform;
                visualBasePosition = visual.localPosition;
                visualBaseRotation = visual.localRotation;
                upperBody = EnemyRagdoll.FindBone(characterAnimator, HumanBodyBones.Chest,
                    "Spine2") ?? EnemyRagdoll.FindBone(characterAnimator, HumanBodyBones.Spine,
                    "Spine");
            }
        }

        private void LateUpdate()
        {
            if (animationSlowed && Time.time >= animationSlowEndsAt)
                RestoreAnimationSpeed();

            if (flashing && Time.unscaledTime >= flashEndsAt)
                RestoreColors();

            if (!recoiling || visual == null)
            {
                return;
            }

            float progress = Mathf.Clamp01((Time.time - recoilStartedAt) / recoilDuration);
            float weight = 1f - Mathf.SmoothStep(0f, 1f, progress);
            visual.localPosition = visualBasePosition + recoilDirection * (recoilDistance * weight);
            visual.localRotation = visualBaseRotation * Quaternion.Euler(
                -recoilDirection.z * recoilDegrees * weight, 0f,
                recoilDirection.x * recoilDegrees * weight);
            // The Animator does not refresh its pose during hit stop. Avoid stacking the
            // same lean every paused frame; it will resume from the next animated pose.
            if (upperBody != null && Time.deltaTime > 0f)
            {
                Vector3 leanAxis = Vector3.Cross(Vector3.up, worldRecoilDirection);
                upperBody.rotation = Quaternion.AngleAxis(recoilDegrees * 0.35f * weight, leanAxis)
                    * upperBody.rotation;
            }
            if (progress >= 1f)
            {
                StopRecoil();
            }
        }

        public void Play(float flashDuration, Vector3 worldDirection,
            float distance, float degrees, float duration)
        {
            SlowAnimation();
            flashEndsAt = Time.unscaledTime + flashDuration;
            if (!flashing)
            {
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer target = renderers[index];
                    target.GetPropertyBlock(savedBlocks[index]);
                    target.GetPropertyBlock(workBlock);
                    foreach (Material material in target.sharedMaterials)
                    {
                        if (material == null)
                        {
                            continue;
                        }

                        if (material.HasProperty(BaseColorId))
                        {
                            workBlock.SetColor(BaseColorId, FlashColor);
                        }

                        if (material.HasProperty(ColorId))
                        {
                            workBlock.SetColor(ColorId, FlashColor);
                        }

                        if (material.HasProperty(EmissionId))
                        {
                            workBlock.SetColor(EmissionId, FlashColor * 2f);
                        }
                    }
                    target.SetPropertyBlock(workBlock);
                    workBlock.Clear();
                }
                flashing = true;
            }

            if (visual == null || duration <= 0f)
            {
                return;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            recoilDirection = visual.parent.InverseTransformDirection(worldDirection.normalized);
            worldRecoilDirection = worldDirection.normalized;
            recoilDirection.y = 0f;
            recoilDirection.Normalize();
            recoilDistance = distance;
            recoilDegrees = degrees;
            recoilDuration = duration;
            recoilStartedAt = Time.time;
            recoiling = true;
        }

        public void Stop()
        {
            RestoreAnimationSpeed();
            RestoreColors();
            StopRecoil();
        }

        private void OnDisable() => Stop();

        private void SlowAnimation()
        {
            if (characterAnimator == null || hitAnimationDuration <= 0f)
            {
                return;
            }

            // Repeated hits extend the slowdown without multiplying an already slowed speed.
            if (!animationSlowed)
            {
                animationBaseSpeed = characterAnimator.speed;
            }
            characterAnimator.speed = animationBaseSpeed * hitAnimationSpeed;
            animationSlowEndsAt = Time.time + hitAnimationDuration;
            animationSlowed = true;
        }

        private void RestoreAnimationSpeed()
        {
            if (!animationSlowed)
            {
                return;
            }

            if (characterAnimator != null)
            {
                characterAnimator.speed = animationBaseSpeed;
            }
            animationSlowed = false;
        }

        private void OnDestroy()
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].sharedMaterials = originalMaterials[index];
                }
            }

            foreach (Material material in flashMaterials.Values)
            {
                Destroy(material);
            }
        }

        // The animated model is the character; shield bubbles and other attachments are not.
        internal static Renderer[] CharacterRenderers(Component owner)
        {
            Animator characterAnimator = owner.GetComponentInChildren<Animator>(true);
            if (characterAnimator == null)
            {
                return owner.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            return System.Array.FindAll(characterAnimator.GetComponentsInChildren<Renderer>(true),
                renderer => renderer is SkinnedMeshRenderer || renderer is MeshRenderer);
        }

        private void RestoreColors()
        {
            if (!flashing)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
                renderers[index].SetPropertyBlock(savedBlocks[index]);
            flashing = false;
        }

        private void StopRecoil()
        {
            if (!recoiling)
            {
                return;
            }

            if (visual != null)
            {
                visual.localPosition = visualBasePosition;
                visual.localRotation = visualBaseRotation;
            }
            recoiling = false;
        }
    }
}
