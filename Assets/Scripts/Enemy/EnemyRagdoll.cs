using System.Collections.Generic;
using UnityEngine;

namespace Caffeine.Gameplay.Enemy
{
    // The enemy prefabs share a Humanoid avatar. Build the physics body from its current
    // animation pose only when killed, so live enemies keep their normal controller collision.
    [DisallowMultipleComponent]
    public sealed class EnemyRagdoll : MonoBehaviour
    {
        private readonly struct BonePart
        {
            public readonly HumanBodyBones Bone;
            public readonly HumanBodyBones Tip;
            public readonly string GenericBone;
            public readonly string GenericTip;
            public readonly float Mass;

            public BonePart(HumanBodyBones bone, HumanBodyBones tip,
                string genericBone, string genericTip, float mass)
            {
                Bone = bone;
                Tip = tip;
                GenericBone = genericBone;
                GenericTip = genericTip;
                Mass = mass;
            }
        }

        private static readonly BonePart[] Parts =
        {
            new(HumanBodyBones.Hips, HumanBodyBones.Spine, "Hips", "Spine", 4f),
            new(HumanBodyBones.Spine, HumanBodyBones.Chest, "Spine", "Spine2", 3f),
            new(HumanBodyBones.Chest, HumanBodyBones.Neck, "Spine2", "Neck", 3f),
            new(HumanBodyBones.Head, HumanBodyBones.LastBone, "Head", null, 1f),
            new(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, "LeftArm", "LeftForeArm", 0.8f),
            new(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "LeftForeArm", "LeftHand", 0.6f),
            new(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, "RightArm", "RightForeArm", 0.8f),
            new(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "RightForeArm", "RightHand", 0.6f),
            new(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, "LeftUpLeg", "LeftLeg", 1.8f),
            new(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "LeftLeg", "LeftFoot", 1.2f),
            new(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, "RightUpLeg", "RightLeg", 1.8f),
            new(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "RightLeg", "RightFoot", 1.2f)
        };

        private bool played;

        public void Play(Animator animator, Vector3 hitDirection)
        {
            if (played || animator == null)
            {
                return;
            }

            played = true;
            var bodies = new Dictionary<Transform, Rigidbody>();
            var colliders = new List<Collider>();

            foreach (BonePart part in Parts)
            {
                Transform bone = FindBone(animator, part.Bone, part.GenericBone);
                if (bone == null)
                {
                    continue;
                }

                Transform tip = part.Tip == HumanBodyBones.LastBone
                    ? null : FindBone(animator, part.Tip, part.GenericTip);
                Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = part.Mass;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.maxAngularVelocity = 20f;
                bodies.Add(bone, body);

                Vector3 toTip = tip == null ? Vector3.zero : bone.InverseTransformPoint(tip.position);
                Collider shape = AddShape(bone, toTip, part.Bone == HumanBodyBones.Head);
                colliders.Add(shape);
            }

            if (bodies.Count == 0)
            {
                BreakApart(animator, hitDirection);
                return;
            }

            foreach (SkinnedMeshRenderer renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }

            foreach (KeyValuePair<Transform, Rigidbody> pair in bodies)
            {
                Transform ancestor = pair.Key.parent;
                while (ancestor != null && !bodies.ContainsKey(ancestor))
                {
                    ancestor = ancestor.parent;
                }
                if (ancestor == null)
                {
                    continue;
                }

                CharacterJoint joint = pair.Key.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = bodies[ancestor];
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
                joint.lowTwistLimit = new SoftJointLimit { limit = -25f };
                joint.highTwistLimit = new SoftJointLimit { limit = 25f };
                joint.swing1Limit = new SoftJointLimit { limit = 35f };
                joint.swing2Limit = new SoftJointLimit { limit = 35f };
            }

            // Non-neighbouring limbs overlap in the death pose; self-collision would explode
            // the body before the joints have a chance to settle.
            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
                }
            }

            animator.enabled = false;
            foreach (Rigidbody body in bodies.Values)
            {
                body.isKinematic = false;
                body.useGravity = true;
            }

            Transform chest = FindBone(animator, HumanBodyBones.Chest, "Spine2");
            if (chest == null || !bodies.TryGetValue(chest, out Rigidbody impactBody))
            {
                Transform hips = FindBone(animator, HumanBodyBones.Hips, "Hips");
                if (hips == null || !bodies.TryGetValue(hips, out impactBody))
                {
                    return;
                }
            }

            hitDirection.y = 0f;
            Vector3 impulse = hitDirection.sqrMagnitude > 0.0001f
                ? hitDirection.normalized * 5f + Vector3.up * 1.5f
                : transform.forward * -3f + Vector3.up * 1.5f;
            impactBody.AddForce(impulse, ForceMode.Impulse);
        }

        // Rigid models without a skeleton (the furniture enemies) fall apart into their parts.
        private void BreakApart(Animator animator, Vector3 hitDirection)
        {
            MeshFilter[] pieces = animator.GetComponentsInChildren<MeshFilter>(true);
            if (pieces.Length == 0)
            {
                Debug.LogWarning("Enemy has neither ragdoll bones nor rigid parts; using dissolve only.", this);
                return;
            }

            animator.enabled = false;
            foreach (MeshFilter piece in pieces)
            {
                piece.transform.SetParent(animator.transform, true);
            }

            hitDirection.y = 0f;
            Vector3 push = hitDirection.sqrMagnitude > 0.0001f
                ? hitDirection.normalized * 2.5f
                : transform.forward * -2f;
            var colliders = new List<Collider>();
            foreach (MeshFilter piece in pieces)
            {
                colliders.Add(piece.gameObject.AddComponent<BoxCollider>());
                Rigidbody body = piece.gameObject.AddComponent<Rigidbody>();
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.AddForce(push + Vector3.up * 2f + Random.insideUnitSphere * 1.5f, ForceMode.VelocityChange);
                body.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
            }

            // The parts start interlocked; letting them collide would fling them apart violently.
            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
                }
            }
        }

        internal static Transform FindBone(Animator animator, HumanBodyBones humanBone,
            string genericName)
        {
            if (animator.isHuman)
            {
                Transform human = animator.GetBoneTransform(humanBone);
                if (human != null)
                {
                    return human;
                }
            }

            if (string.IsNullOrEmpty(genericName))
            {
                return null;
            }

            foreach (Transform candidate in animator.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == genericName || candidate.name.EndsWith(":" + genericName))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static Collider AddShape(Transform bone, Vector3 toTip, bool head)
        {
            if (head || toTip.sqrMagnitude < 0.0001f)
            {
                SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.radius = WorldRadiusInBoneSpace(bone, head ? 0.11f : 0.08f);
                return sphere;
            }

            Vector3 absolute = new(Mathf.Abs(toTip.x), Mathf.Abs(toTip.y), Mathf.Abs(toTip.z));
            int axis = absolute.x > absolute.y && absolute.x > absolute.z ? 0
                : absolute.z > absolute.y ? 2 : 1;
            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = axis;
            capsule.center = toTip * 0.5f;
            capsule.radius = Mathf.Max(WorldRadiusInBoneSpace(bone, 0.025f),
                toTip.magnitude * 0.2f);
            capsule.height = Mathf.Max(capsule.radius * 2f, absolute[axis] + capsule.radius);
            return capsule;
        }

        private static float WorldRadiusInBoneSpace(Transform bone, float radius)
        {
            Vector3 scale = bone.lossyScale;
            return radius / Mathf.Max(0.0001f, Mathf.Max(
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
    }
}
