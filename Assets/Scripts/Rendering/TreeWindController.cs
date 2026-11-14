using System.Collections.Generic;
using UnityEngine;

namespace Caffeine.Gameplay.Rendering
{

    /// Publishes one shared set of wind values for every Tree Wind material.
    /// Place a single instance in the scene; each tree offsets its sway phase
    /// using a hash of its world origin in the vertex shader.
    [ExecuteAlways] // enable the shader on edit mode
    [DisallowMultipleComponent]
    public sealed class TreeWindController : MonoBehaviour
    {


        private static readonly int DirectionId = Shader.PropertyToID("_GlobalTreeWindDirection");
        private static readonly int StrengthId = Shader.PropertyToID("_GlobalTreeWindStrength");
        private static readonly int SpeedId = Shader.PropertyToID("_GlobalTreeWindSpeed");
        private static readonly int SecondarySwaySpeedRatioId = Shader.PropertyToID("_GlobalTreeSecondarySwaySpeedRatio");
        private static readonly int SecondarySwayWeightId = Shader.PropertyToID("_GlobalTreeSecondarySwayWeight");
        private static readonly int SwayWaveVectorId = Shader.PropertyToID("_GlobalTreeSwayWaveVector");
        private static readonly int FlutterWaveVectorId = Shader.PropertyToID("_GlobalTreeFlutterWaveVector");
        private static readonly int GustStrengthId = Shader.PropertyToID("_GlobalTreeGustStrength");
        private static readonly int GustFrequencyId = Shader.PropertyToID("_GlobalTreeGustFrequency");

        private readonly Dictionary<Material, List<Renderer>> grassRenderers = new();
        private readonly Dictionary<Material, float> grassDisplacements = new();
        private bool refreshGrassBounds = true;

        [SerializeField] private Vector2 direction = new(1f, 0.35f);
        [SerializeField, Min(0f)] private float strength = 0.25f;
        [SerializeField, Min(0f)] private float speed = 1.2f;

        // Artistic defaults retained from the original two-frequency sway.
        [SerializeField, HideInInspector, Min(0f)] private float secondarySwaySpeedRatio = 1.73f;
        [SerializeField, HideInInspector, Range(0f, 1f)] private float secondarySwayWeight = 0.35f;

        // Artistic defaults in metres; zero disables the corresponding spatial variation.
        [SerializeField, HideInInspector, Min(0f)] private float swayWavelength = 90f;
        [SerializeField, HideInInspector, Min(0f)] private float flutterWavelength = 1.5f;

        [SerializeField, Range(0f, 2f)] private float gustStrength = 0.35f;
        [SerializeField, HideInInspector, Min(0f)] private float gustFrequency = 0.18f;

        private void OnEnable()
        {
            ApplyWindGlobals();
        }

        private void OnValidate()
        {
            Debug.Assert(direction.sqrMagnitude > 0f, "Wind direction must not be zero.");
            ApplyWindGlobals();
        }

        private void LateUpdate()
        {
            if (refreshGrassBounds)
            {
                RefreshGrassBounds();
            }
            else
            {
                UpdateGrassBounds();
            }
        }

        // Vertex movement does not update Unity's CPU culling bounds.
        public void RefreshGrassBounds()
        {
            refreshGrassBounds = false;
            grassRenderers.Clear();
            grassDisplacements.Clear();
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var material = renderer.sharedMaterial;
                if (material == null || material.shader.name != "Caffeine/Tree Wind" ||
                    material.GetFloat("_IsLeaf") != 2f)
                {
                    continue;
                }

                if (!grassRenderers.TryGetValue(material, out var renderers))
                {
                    renderers = new List<Renderer>();
                    grassRenderers.Add(material, renderers);
                }
                renderers.Add(renderer);
            }
            UpdateGrassBounds();
        }

        private void UpdateGrassBounds()
        {
            float strength = Mathf.Abs(Shader.GetGlobalFloat(StrengthId));
            float gust = Mathf.Abs(Shader.GetGlobalFloat(GustStrengthId));
            foreach (var pair in grassRenderers)
            {
                var material = pair.Key;
                if (material == null)
                {
                    continue;
                }

                float displacement = strength * Mathf.Abs(material.GetFloat("_SwayStrengthScale")) * (1f + gust)
                    + Mathf.Abs(material.GetFloat("_LeafFlutterStrength"));
                if (grassDisplacements.TryGetValue(material, out float previous) && previous == displacement)
                {
                    continue;
                }

                foreach (var renderer in pair.Value)
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    var bounds = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var matrix = renderer.worldToLocalMatrix;
                    var padding = new Vector3(
                        new Vector3(matrix.m00, matrix.m01, matrix.m02).magnitude,
                        new Vector3(matrix.m10, matrix.m11, matrix.m12).magnitude,
                        new Vector3(matrix.m20, matrix.m21, matrix.m22).magnitude) * displacement;
                    bounds.Expand(padding * 2f);
                    renderer.localBounds = bounds;
                }
                grassDisplacements[material] = displacement;
            }
        }

        private void ApplyWindGlobals()
        {
            Vector2 normalizedDirection = direction.normalized;
            Vector4 waveDirection = new(normalizedDirection.x, 0f, normalizedDirection.y, 0f);
            float swayWaveNumber = swayWavelength > 0f ? 2f * Mathf.PI / swayWavelength : 0f;
            float flutterWaveNumber = flutterWavelength > 0f ? 2f * Mathf.PI / flutterWavelength : 0f;

            Shader.SetGlobalVector(
                DirectionId,
                new Vector4(normalizedDirection.x, 0f, normalizedDirection.y, 0f));
            Shader.SetGlobalFloat(StrengthId, strength);
            Shader.SetGlobalFloat(SpeedId, speed);
            Shader.SetGlobalFloat(SecondarySwaySpeedRatioId, secondarySwaySpeedRatio);
            Shader.SetGlobalFloat(SecondarySwayWeightId, secondarySwayWeight);
            Shader.SetGlobalVector(SwayWaveVectorId, waveDirection * swayWaveNumber);
            Shader.SetGlobalVector(FlutterWaveVectorId, waveDirection * flutterWaveNumber);
            Shader.SetGlobalFloat(GustStrengthId, gustStrength);
            Shader.SetGlobalFloat(GustFrequencyId, gustFrequency);
            refreshGrassBounds = true;
        }
    }
}
