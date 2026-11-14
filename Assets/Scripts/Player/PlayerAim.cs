using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAim : MonoBehaviour
    {
        [SerializeField] private Transform rotationTarget;

        private Vector3 mazeLockedAimDirection = Vector3.forward;
        private bool isMazeAimLocked;

        public Vector3 AimDirection => isMazeAimLocked
            ? mazeLockedAimDirection
            : ResolvePlanarForward(rotationTarget.forward);

        private void Start()
        {
        }

        private void OnDisable()
        {
            ReleaseMazeAimLock();
        }

        public void CaptureMazeAimLock()
        {
            mazeLockedAimDirection = ResolvePlanarForward(rotationTarget.forward);
            isMazeAimLocked = true;
        }

        public void ReleaseMazeAimLock()
        {
            isMazeAimLocked = false;
        }

        private static Vector3 ResolvePlanarForward(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
        }
    }
}
