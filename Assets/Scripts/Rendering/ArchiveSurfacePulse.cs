using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Caffeine.Gameplay.Rendering
{
    public sealed class ArchiveSurfacePulse : MonoBehaviour
    {
        private struct Surface
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private readonly List<Surface> surfaces = new();
        private Material material;
        private MaterialPropertyBlock properties;
        private Camera targetCamera;
        private Vector3 origin;
        private float seconds;
        private float radius;
        private float width;
        private float elapsed;

        public static ArchiveSurfacePulse Play(Transform owner, MeshRenderer[] renderers,
            Vector3 center, Color color, float duration, float distance, float bandWidth)
        {
            var visual = new GameObject("Archive surface pulse");
            visual.transform.SetParent(owner, false);
            var pulse = visual.AddComponent<ArchiveSurfacePulse>();
            pulse.origin = center;
            pulse.seconds = duration;
            pulse.radius = distance;
            pulse.width = bandWidth;
            pulse.targetCamera = Camera.main;
            pulse.material = Resources.Load<Material>("Effects/ArchiveSurfacePulse");
            pulse.properties = new MaterialPropertyBlock();
            pulse.properties.SetVector("_PulseOrigin", center);
            pulse.properties.SetColor("_Tint", color);
            pulse.properties.SetFloat("_PulseWidth", bandWidth);
            foreach (var renderer in renderers)
            {
                if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    pulse.surfaces.Add(new Surface { Renderer = renderer, Mesh = filter.sharedMesh });
                }
            }
            return pulse;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= seconds)
            {
                Destroy(gameObject);
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null || material == null)
            {
                return;
            }
            float progress = Mathf.Clamp01(elapsed / seconds);
            float waveRadius = progress * (radius + width);
            properties.SetFloat("_PulseRadius", waveRadius);
            properties.SetFloat("_Fade", Mathf.Sqrt(1f - progress) * .8f);
            foreach (var surface in surfaces)
            {
                var renderer = surface.Renderer;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Bounds bounds = renderer.bounds;
                float outer = waveRadius + width;
                float inner = Mathf.Max(0f, waveRadius - width);
                Vector3 farthest = new Vector3(Mathf.Abs(bounds.center.x - origin.x),
                    Mathf.Abs(bounds.center.y - origin.y), Mathf.Abs(bounds.center.z - origin.z)) + bounds.extents;
                if (bounds.SqrDistance(origin) > outer * outer || farthest.sqrMagnitude < inner * inner)
                {
                    continue;
                }
                for (int submesh = 0; submesh < surface.Mesh.subMeshCount; submesh++)
                {
                    Graphics.DrawMesh(surface.Mesh, renderer.localToWorldMatrix, material, renderer.gameObject.layer,
                        targetCamera, submesh, properties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                }
            }
        }
    }
}
