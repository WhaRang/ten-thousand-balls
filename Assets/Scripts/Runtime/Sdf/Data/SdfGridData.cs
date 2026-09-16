using System;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Data
{
    /// <summary>
    /// Describes where the baked samples sit in the world and how they are laid out in memory.
    /// Shared by the baker (which writes samples) and the runtime sampler (which reads them), so
    /// the two can never disagree about the layout.
    ///
    /// Conventions, fixed here and nowhere else:
    ///  - Grid-point convention: sample (x, y, z) is the field value at exactly
    ///    BoundsMin + (x, y, z) * CellSize. The last sample lies on BoundsMax.
    ///  - Cells are cubes. One cell size for all axes keeps the interpolation error isotropic.
    ///  - Memory order is x fastest, then y, then z, which is also how Texture3D stores its
    ///    pixels, so the baked array can be handed to the texture without reshuffling.
    /// </summary>
    [Serializable]
    public struct SdfGridData
    {
        /// <summary>World position of sample (0, 0, 0).</summary>
        public float3 BoundsMin;

        /// <summary>Distance between neighbouring samples, in world units.</summary>
        public float CellSize;

        /// <summary>Number of samples along each axis. At least 2 per axis to interpolate.</summary>
        public int3 Resolution;

        /// <summary>World position of the last sample on each axis.</summary>
        public float3 BoundsMax => BoundsMin + (float3)(Resolution - 1) * CellSize;

        /// <summary>Total number of samples in the volume.</summary>
        public int SampleCount => Resolution.x * Resolution.y * Resolution.z;
        
        /// <summary>
        /// Builds a grid that covers at least the given world bounds. The resolution is rounded
        /// up so the last sample lands on or just past the requested max; the bounds are never
        /// shrunk, because a sample missing at the edge is worse than a sample too many.
        /// </summary>
        public static SdfGridData Generate(Bounds worldBounds, float cellSize)
        {
            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            float3 size = worldBounds.size;

            // Samples sit on both ends of each axis, hence the +1: a span of one cell needs
            // two samples.
            int3 resolution = (int3)math.ceil(size / cellSize) + 1;

            return new SdfGridData
            {
                BoundsMin = worldBounds.min,
                CellSize = cellSize,
                Resolution = math.max(resolution, 2),
            };
        }
    }
}
