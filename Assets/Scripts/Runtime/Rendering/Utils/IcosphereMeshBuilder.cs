using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Rendering.Utils
{
    /// <summary>
    /// Builds a unit sphere with the fewest triangles for a given smoothness.
    /// </summary>
    public static class IcosphereMeshBuilder
    {
        public const int MaxSubdivisions = 3;

        public static Mesh Build(int subdivisions)
        {
            subdivisions = math.clamp(subdivisions, 0, MaxSubdivisions);

            var vertices = new List<Vector3>(12);
            var triangles = new List<int>(60);
            AddIcosahedron(vertices, triangles);

            for (int level = 0; level < subdivisions; level++)
            {
                triangles = Subdivide(vertices, triangles);
            }

            EnforceOutwardWinding(vertices, triangles);

            var mesh = new Mesh { name = $"Icosphere L{subdivisions}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertices);
            mesh.SetTriangles(triangles, submesh: 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);

            mesh.UploadMeshData(markNoLongerReadable: true);
            return mesh;
        }

        private static void AddIcosahedron(List<Vector3> vertices, List<int> triangles)
        {
            float phi = (1f + math.sqrt(5f)) * 0.5f;

            vertices.Add(new Vector3(-1f, phi, 0f).normalized);
            vertices.Add(new Vector3(1f, phi, 0f).normalized);
            vertices.Add(new Vector3(-1f, -phi, 0f).normalized);
            vertices.Add(new Vector3(1f, -phi, 0f).normalized);

            vertices.Add(new Vector3(0f, -1f, phi).normalized);
            vertices.Add(new Vector3(0f, 1f, phi).normalized);
            vertices.Add(new Vector3(0f, -1f, -phi).normalized);
            vertices.Add(new Vector3(0f, 1f, -phi).normalized);

            vertices.Add(new Vector3(phi, 0f, -1f).normalized);
            vertices.Add(new Vector3(phi, 0f, 1f).normalized);
            vertices.Add(new Vector3(-phi, 0f, -1f).normalized);
            vertices.Add(new Vector3(-phi, 0f, 1f).normalized);

            triangles.AddRange(new[]
            {
                0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,  // five faces around vertex 0
                1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,    // adjacent belt
                3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,    // five faces around vertex 3
                4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1,    // adjacent belt
            });
        }

        private static List<int> Subdivide(List<Vector3> vertices, List<int> triangles)
        {
            var midpointByEdge = new Dictionary<long, int>(triangles.Count);
            var result = new List<int>(triangles.Count * 4);

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                int ab = Midpoint(a, b, vertices, midpointByEdge);
                int bc = Midpoint(b, c, vertices, midpointByEdge);
                int ca = Midpoint(c, a, vertices, midpointByEdge);

                // Three corner triangles and one in the middle, all with the parent's winding.
                result.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            return result;
        }

        private static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = ((long)math.min(a, b) << 32) | (uint)math.max(a, b);
            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
            int index = vertices.Count - 1;
            cache[key] = index;
            
            return index;
        }

        /// <summary>
        /// Makes every triangle face outward by construction instead of trusting the face table's
        /// convention: Unity treats cross(b − a, c − a) as the front-face normal, so a triangle whose
        /// normal points toward the centre is flipped.
        /// </summary>
        private static void EnforceOutwardWinding(List<Vector3> vertices, List<int> triangles)
        {
            for (int i = 0; i < triangles.Count; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var faceNormal = Vector3.Cross(b - a, c - a);
                var centroid = a + b + c;

                if (Vector3.Dot(faceNormal, centroid) < 0f)
                {
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                }
            }
        }
    }
}
