using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Rendering.Utils
{
    /// <summary>
    /// Builds a unit sphere with the fewest triangles for a given smoothness: an icosahedron whose
    /// triangles are split into four, with every new vertex pushed back onto the sphere.
    ///
    ///   level 0:  12 vertices /   20 triangles
    ///   level 1:  42 vertices /   80 triangles   (default: 10k balls = 800k triangles)
    ///   level 2: 162 vertices /  320 triangles
    ///   level 3: 642 vertices / 1280 triangles   (Unity's built-in sphere is 760 triangles)
    ///
    /// Runs once at startup; the managed lists it allocates are gone before the first frame.
    /// The mesh has positions and normals only. Normals equal positions on a unit sphere, so the
    /// shader needs no normal transform, and nothing reads UVs or tangents.
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
            mesh.SetNormals(vertices); // unit sphere: the normal at a vertex is the vertex itself
            mesh.SetTriangles(triangles, submesh: 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);

            // Push to the GPU and drop the CPU copy: nothing reads the mesh back.
            mesh.UploadMeshData(markNoLongerReadable: true);
            return mesh;
        }

        /// <summary>
        /// The twelve vertices of an icosahedron are the corners of three mutually perpendicular
        /// golden rectangles (1 × φ). The twenty faces connect them; five meet at every vertex.
        /// </summary>
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

        /// <summary>
        /// Splits every triangle into four using the midpoints of its edges. Midpoints are cached
        /// by edge so the two triangles sharing an edge get the same vertex: no duplicates, no
        /// cracks, and the vertex count stays at 10·4ⁿ + 2.
        /// </summary>
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
            // Order the pair so (a, b) and (b, a) hit the same key.
            long key = ((long)math.min(a, b) << 32) | (uint)math.max(a, b);
            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            // Normalising projects the midpoint from the chord back onto the sphere.
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
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                Vector3 centroid = a + b + c; // direction from the origin is all that matters

                if (Vector3.Dot(faceNormal, centroid) < 0f)
                {
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                }
            }
        }
    }
}
