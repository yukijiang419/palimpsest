using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Caffeine.Gameplay.Puzzles
{
    [DisallowMultipleComponent]
    public sealed class PuzzleFeedback : MonoBehaviour
    {
        private sealed class Surface
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public Bounds Bounds;
            public Material[] Materials;
            public MaterialPropertyBlock[] Properties;
        }

        [SerializeField] private MeshRenderer[] environmentSurfaces;
        [SerializeField] private MeshRenderer[] repairSurfaces;

        private readonly List<Surface> previousSurfaces = new();
        private readonly List<Surface> outgoingSurfaces = new();
        private readonly List<Surface> currentSurfaces = new();
        private readonly Dictionary<(Material, int), Material> scanMaterials = new();
        private Material scanTemplate;
        private PlayerInputReader playerInput;
        private HealthShield playerHealth;
        private Light eraLight;
        private Color previousLightColor;
        private Color nextLightColor;
        private Vector3 origin;
        private float elapsed;
        private float seconds;
        private float radius;
        private float width;
        private static bool scanWarmed;

        public bool IsShifting { get; private set; }

        private void Start()
        {
            scanTemplate = Resources.Load<Material>("Effects/PuzzleTimelineScan");
            if (scanWarmed || scanTemplate == null) return;
            var variants = new ShaderVariantCollection();
            var sources = new HashSet<Material>();
            foreach (var renderer in environmentSurfaces)
                if (renderer != null)
                    foreach (var material in renderer.sharedMaterials) sources.Add(material);
            foreach (var source in sources)
            {
                if (source == null) continue;
                var keywords = new List<string>();
                foreach (string keyword in new[] { "_NORMALMAP", "_METALLICGLOSSMAP", "_EMISSION", "_ALPHATEST_ON" })
                    if (source.IsKeywordEnabled(keyword)) keywords.Add(keyword);
                foreach (var pass in new[] { PassType.ForwardBase, PassType.ForwardAdd, PassType.ShadowCaster })
                    variants.Add(new ShaderVariantCollection.ShaderVariant(scanTemplate.shader, pass, keywords.ToArray()));
            }
            variants.WarmUp();
            Destroy(variants);
            scanWarmed = true;
        }

        public void Configure(MeshRenderer[] environment, MeshRenderer[] repairs)
        {
            environmentSurfaces = environment;
            repairSurfaces = repairs;
        }

        // Capture before Refresh disables the old timeline. No colliders are copied.
        public void PrepareShift()
        {
            if (!Application.isPlaying) return;
            if (scanTemplate == null) scanTemplate = Resources.Load<Material>("Effects/PuzzleTimelineScan");
            if (scanTemplate == null)
                throw new System.InvalidOperationException("Missing puzzle timeline scan material.");
            previousSurfaces.Clear();
            foreach (var renderer in environmentSurfaces)
            {
                Surface surface = Capture(renderer);
                if (surface != null) previousSurfaces.Add(surface);
            }
            eraLight = GetComponent<TimelinePuzzleArea>().eraLight;
            if (eraLight != null) previousLightColor = eraLight.color;
        }

        private static Surface Capture(MeshRenderer renderer)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff
                || !renderer.gameObject.activeInHierarchy
                || !renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) return null;
            var properties = new MaterialPropertyBlock[renderer.sharedMaterials.Length];
            for (int i = 0; i < properties.Length; i++)
            {
                properties[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties[i], i);
                if (properties[i].isEmpty) renderer.GetPropertyBlock(properties[i]);
            }
            return new Surface
            {
                Renderer = renderer,
                Mesh = filter.sharedMesh,
                Matrix = renderer.localToWorldMatrix,
                Bounds = renderer.bounds,
                Materials = renderer.sharedMaterials,
                Properties = properties
            };
        }

        public void Shifted(Vector3 position, TimelineEra era, CharacterController player)
        {
            if (!Application.isPlaying) return;
            var settings = ConfigSystem.Instance.Config.Effects;
            Color color = era switch
            {
                TimelineEra.Past => settings.PastColor,
                TimelineEra.Present => settings.PresentColor,
                _ => settings.FutureColor
            };
            origin = position + player.transform.rotation * Vector3.Scale(player.center, player.transform.lossyScale)
                - Vector3.up * player.height * Mathf.Abs(player.transform.lossyScale.y) * .5f;
            seconds = settings.ShiftSeconds;
            width = settings.SurfaceWidth;
            radius = settings.ShiftRadius;
            elapsed = 0f;
            var previous = new HashSet<MeshRenderer>();
            foreach (var surface in previousSurfaces) previous.Add(surface.Renderer);
            var current = new HashSet<MeshRenderer>();
            foreach (var renderer in environmentSurfaces)
            {
                Surface surface = Capture(renderer);
                if (surface == null) continue;
                current.Add(renderer);
                currentSurfaces.Add(surface);
                Material[] materials = new Material[surface.Materials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = ScanMaterial(surface.Materials[i], previous.Contains(renderer) ? 0 : 1, color);
                renderer.sharedMaterials = materials;
                ExpandRadius(surface.Bounds);
            }
            foreach (var surface in previousSurfaces)
            {
                ExpandRadius(surface.Bounds);
                if (current.Contains(surface.Renderer)) continue;
                outgoingSurfaces.Add(surface);
                foreach (var material in surface.Materials) ScanMaterial(material, -1, color);
            }
            if (eraLight != null)
            {
                nextLightColor = eraLight.color;
                eraLight.color = previousLightColor;
            }
            playerInput = player.GetComponent<PlayerInputReader>();
            playerHealth = player.GetComponent<HealthShield>();
            player.GetComponent<PuzzleBlockPusher>()?.ReleaseGrip();
            playerInput.SetTimelineShiftBlocked(true);
            IsShifting = true;
            SetRadius(-width);
        }

        private Material ScanMaterial(Material source, int side, Color color)
        {
            if (!scanMaterials.TryGetValue((source, side), out var material))
            {
                material = new Material(source) { shader = scanTemplate.shader, name = "Timeline scan " + source.name };
                material.SetVector("_ScanOrigin", origin);
                material.SetFloat("_ScanWidth", width);
                material.SetFloat("_ScanSide", side);
                material.SetColor("_ScanColor", color);
                scanMaterials.Add((source, side), material);
            }
            return material;
        }

        private void ExpandRadius(Bounds bounds)
        {
            Vector2 farthest = new(Mathf.Abs(bounds.center.x - origin.x) + bounds.extents.x,
                Mathf.Abs(bounds.center.z - origin.z) + bounds.extents.z);
            radius = Mathf.Max(radius, farthest.magnitude);
        }

        private void SetRadius(float value)
        {
            foreach (var material in scanMaterials.Values) material.SetFloat("_ScanRadius", value);
        }

        private void Update()
        {
            if (!IsShifting) return;
            if (playerInput == null || playerHealth != null && playerHealth.IsDead)
            {
                CancelShift();
                return;
            }
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / seconds);
            SetRadius(Mathf.Lerp(-width, radius + width, progress));
            if (eraLight != null) eraLight.color = Color.Lerp(previousLightColor, nextLightColor, progress);
            if (progress >= 1f) CancelShift();
        }

        private void LateUpdate()
        {
            if (!IsShifting) return;
            foreach (var surface in outgoingSurfaces)
            {
                if (surface.Renderer == null) continue;
                for (int i = 0; i < surface.Materials.Length; i++)
                {
                    Graphics.DrawMesh(surface.Mesh, surface.Matrix, scanMaterials[(surface.Materials[i], -1)],
                        surface.Renderer.gameObject.layer, null, Mathf.Min(i, surface.Mesh.subMeshCount - 1),
                        surface.Properties[i], surface.Renderer.shadowCastingMode, surface.Renderer.receiveShadows,
                        null, LightProbeUsage.BlendProbes);
                }
            }
        }

        public void CancelShift()
        {
            foreach (var surface in currentSurfaces)
                if (surface.Renderer != null) surface.Renderer.sharedMaterials = surface.Materials;
            foreach (var material in scanMaterials.Values) Destroy(material);
            if (IsShifting)
            {
                if (eraLight != null) eraLight.color = nextLightColor;
                if (playerInput != null) playerInput.SetTimelineShiftBlocked(false);
            }
            IsShifting = false;
            playerInput = null;
            playerHealth = null;
            previousSurfaces.Clear();
            currentSurfaces.Clear();
            outgoingSurfaces.Clear();
            scanMaterials.Clear();
        }

        private void OnDisable() => CancelShift();

        public void Repaired(Vector3 position)
        {
            if (!Application.isPlaying) return;
            var settings = ConfigSystem.Instance.Config.Effects;
            float repairRadius = 1f;
            foreach (var surface in repairSurfaces)
            {
                if (surface == null || !surface.gameObject.activeInHierarchy) continue;
                repairRadius = Mathf.Max(repairRadius,
                    Vector3.Distance(position, surface.bounds.center) + surface.bounds.extents.magnitude);
            }
            ArchiveSurfacePulse.Play(transform, repairSurfaces, position, settings.PresentColor,
                settings.RepairSeconds, repairRadius, settings.SurfaceWidth);
        }
    }
}
