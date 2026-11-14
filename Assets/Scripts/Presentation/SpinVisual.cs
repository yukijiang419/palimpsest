using UnityEngine;

namespace Caffeine.Gameplay.Presentation
{
    // Tumbles a purely visual child, such as the soda can on an enemy shot.
    [DisallowMultipleComponent]
    public sealed class SpinVisual : MonoBehaviour
    {
        [SerializeField] private Vector3 degreesPerSecond = new(720f, 0f, 240f);

        private void Update()
        {
            transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
