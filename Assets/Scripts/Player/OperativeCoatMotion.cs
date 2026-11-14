using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    // Lightweight motion for the visual-only coat panels. No physics or collision.
    [DisallowMultipleComponent]
    public sealed class OperativeCoatMotion : MonoBehaviour
    {
        [SerializeField] private int side = 1;
        private PlayerMoveController motor;
        private Quaternion restRotation;
        private float sway;

        public void SetSide(int value) => side = value < 0 ? -1 : 1;

        private void Awake()
        {
            motor = GetComponentInParent<PlayerMoveController>();
            restRotation = transform.localRotation;
        }

        private void LateUpdate()
        {
            float movement = motor ? Mathf.Clamp01(motor.CurrentSpeed / 6f) : 0f;
            float stride = Mathf.Sin(Time.time * (5f + movement * 6f) + side * 1.2f);
            float target = 5f + movement * (13f + stride * 9f);
            sway = Mathf.Lerp(sway, target, 1f - Mathf.Exp(-9f * Time.deltaTime));
            transform.localRotation = restRotation * Quaternion.Euler(-sway, side * movement * 3f, side * stride * movement * 4f);
        }
    }
}
