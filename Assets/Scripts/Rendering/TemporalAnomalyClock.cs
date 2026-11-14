using UnityEngine;

namespace Caffeine.Gameplay.Rendering
{
    [DisallowMultipleComponent]
    public sealed class TemporalAnomalyClock : MonoBehaviour
    {
        private static readonly int ClockId = Shader.PropertyToID("_TemporalAnomalyClock");
        private static readonly int EnabledId = Shader.PropertyToID("_TemporalAnomalyEnabled");
        public bool UseUnscaledTime { get; set; }

        private void OnEnable()
        {
            Shader.SetGlobalFloat(EnabledId, 1f);
        }

        private void Update()
        {
            Shader.SetGlobalFloat(ClockId, UseUnscaledTime ? Time.unscaledTime : Time.time);
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(EnabledId, 0f);
        }
    }
}
