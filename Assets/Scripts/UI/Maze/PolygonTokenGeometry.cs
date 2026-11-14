using System.Collections.Generic;
using UnityEngine;

namespace Caffeine.Gameplay.UI
{
    // build the vertices and triangles used by maze tokens
    public static class PolygonTokenGeometry
    {
        public static Vector2[] BuildVertices(Rect rect, int sideCount)
        {
            // make all polygon points
            sideCount = Mathf.Max(3, sideCount);
            var vertices = new Vector2[sideCount];
            Vector2 center = rect.center;
            float radius = Mathf.Max(0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            const float startAngleDegrees = 90f;

            for (int index = 0; index < sideCount; index++)
            {
                float angle = (startAngleDegrees - (360f * index / sideCount)) * Mathf.Deg2Rad;
                vertices[index] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            return vertices;
        }

                // add the inside triangles
                public static void AddClockwiseFill(
            List<UIVertex> vertices,
            List<int> triangles,
            Vector2 center,
            Vector2[] polygon,
            float fillFraction,
            Color color)
        {
            int sideCount = polygon.Length;
            float endBoundary = Mathf.Clamp01(fillFraction) * sideCount;
            if (endBoundary <= 0f)
            {
                return;
            }

            // build a triangle fan from the center to the filled perimeter
            Vector2 startPoint = PointOnClockwisePerimeter(polygon, 0f);
            Vector2 endPoint = PointOnClockwisePerimeter(polygon, endBoundary);
            int centerIndex = vertices.Count;
            vertices.Add(CreateVertex(center, color));
            vertices.Add(CreateVertex(startPoint, color));
            int lastIntermediateVertex = Mathf.CeilToInt(endBoundary) - 1;
            for (int vertex = 1; vertex <= lastIntermediateVertex; vertex++)
            {
                vertices.Add(CreateVertex(polygon[vertex % sideCount], color));
            }

            vertices.Add(CreateVertex(endPoint, color));
            int triangleCount = vertices.Count - centerIndex - 2;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                triangles.Add(centerIndex);
                triangles.Add(centerIndex + triangle + 1);
                triangles.Add(centerIndex + triangle + 2);
            }
        }

        public static void AddEdgeQuad(
            List<UIVertex> vertices,
            List<int> triangles,
            Vector2 from,
            Vector2 to,
            float thickness,
            Color color)
        {
            // make one thick border line
            Vector2 delta = to - from;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * Mathf.Max(0.5f, thickness) * 0.5f;
            int baseIndex = vertices.Count;
            vertices.Add(CreateVertex(from - normal, color));
            vertices.Add(CreateVertex(from + normal, color));
            vertices.Add(CreateVertex(to + normal, color));
            vertices.Add(CreateVertex(to - normal, color));
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        private static UIVertex CreateVertex(Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            return vertex;
        }

        // find a point along an edge and wrap around at the end of the polygon
        private static Vector2 PointOnClockwisePerimeter(Vector2[] polygon, float boundary)
        {
            int sideCount = polygon.Length;
            float wrappedBoundary = Mathf.Repeat(boundary, sideCount);
            int edge = Mathf.FloorToInt(wrappedBoundary);
            float edgeFraction = wrappedBoundary - edge;
            return Vector2.LerpUnclamped(polygon[edge], polygon[(edge + 1) % sideCount], edgeFraction);
        }
    }
}
