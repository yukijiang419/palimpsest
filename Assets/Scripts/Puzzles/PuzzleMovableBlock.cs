using UnityEngine;

namespace Caffeine.Gameplay.Puzzles
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PuzzleMovableBlock : MonoBehaviour
    {
        public TimelinePuzzleArea area;
        public Vector3 startLocalPosition;
        public Vector2 minLocalXZ;
        public Vector2 maxLocalXZ;
        public bool Locked { get; private set; }
        private Quaternion startLocalRotation;
        private bool startIsKinematic;

        private void Awake()
        {
            startLocalRotation = transform.localRotation;
            startIsKinematic = GetComponent<Rigidbody>().isKinematic;
        }

        public bool CanPush => !Locked && area != null && area.Era == TimelineEra.Past;

        public void LockAt(Vector3 localPosition)
        {
            var body = GetComponent<Rigidbody>();
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            transform.localPosition = localPosition;
            Locked = true;
            Physics.SyncTransforms();
        }

        public void ResetToStart()
        {
            var body = GetComponent<Rigidbody>();
            Locked = false;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = transform.parent.TransformPoint(startLocalPosition);
            body.rotation = transform.parent.rotation * startLocalRotation;
            transform.localPosition = startLocalPosition;
            transform.localRotation = startLocalRotation;
            body.isKinematic = startIsKinematic;
            Physics.SyncTransforms();
        }

        private void FixedUpdate()
        {
            Vector3 local = transform.localPosition;
            Vector3 clamped = new Vector3(
                Mathf.Clamp(local.x, minLocalXZ.x, maxLocalXZ.x),
                local.y,
                Mathf.Clamp(local.z, minLocalXZ.y, maxLocalXZ.y));
            if ((clamped - local).sqrMagnitude < .0001f) return;
            var body = GetComponent<Rigidbody>();
            body.position = transform.parent.TransformPoint(clamped);
            body.linearVelocity = Vector3.zero;
        }
    }
}
