using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MeleeRangeIndicator : MonoBehaviour
    {
        [SerializeField] private LineRenderer ring;
        [SerializeField] private MeshFilter sectorMeshFilter;
        [SerializeField] private int circleSegments = 64;
        [SerializeField] private float ringHeight = 0.05f;
        [SerializeField] private float sectorHeight = 0.04f;

        private Mesh sectorMesh;

        private void Start()
        {

            GameplayConfig.AttackSettings attackSettings = ConfigSystem.Instance.Config.Attack;
            DrawCircle(attackSettings.MeleeRange);
            DrawSector(attackSettings.MeleeRange, attackSettings.MeleeArcDegrees);
        }

        private void DrawCircle(float radius)
        {
            int segmentCount = Mathf.Max(3, circleSegments);
            Vector3[] positions = new Vector3[segmentCount];
            for (int index = 0; index < segmentCount; index++)
            {
                float angle = index / (float)segmentCount * Mathf.PI * 2f;
                positions[index] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    ringHeight,
                    Mathf.Sin(angle) * radius);
            }

            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = segmentCount;
            ring.SetPositions(positions);
        }

        private void DrawSector(float radius, float arcDegrees)
        {
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(3, circleSegments) * arcDegrees / 360f));
            Vector3[] vertices = new Vector3[segmentCount + 2];
            int[] triangles = new int[segmentCount * 3];
            vertices[0] = new Vector3(0f, sectorHeight, 0f);

            float halfArc = arcDegrees * 0.5f;
            for (int index = 0; index <= segmentCount; index++)
            {
                float angle = Mathf.Lerp(-halfArc, halfArc, index / (float)segmentCount) * Mathf.Deg2Rad;
                vertices[index + 1] = new Vector3(
                    Mathf.Sin(angle) * radius,
                    sectorHeight,
                    Mathf.Cos(angle) * radius);
            }

            for (int index = 0; index < segmentCount; index++)
            {
                int triangleIndex = index * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = index + 1;
                triangles[triangleIndex + 2] = index + 2;
            }

            sectorMesh = new Mesh { name = "Melee Sector" };
            sectorMesh.vertices = vertices;
            sectorMesh.triangles = triangles;
            sectorMesh.RecalculateNormals();
            sectorMesh.RecalculateBounds();
            sectorMeshFilter.sharedMesh = sectorMesh;
        }

        private void OnDestroy()
        {
            if (sectorMeshFilter != null && sectorMeshFilter.sharedMesh == sectorMesh)
            {
                sectorMeshFilter.sharedMesh = null;
            }

            if (sectorMesh != null)
            {
                Destroy(sectorMesh);
            }
        }
    }
}
