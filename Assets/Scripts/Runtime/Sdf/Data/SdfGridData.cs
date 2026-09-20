using System;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Data
{
    /// <summary>
    /// Describes where the baked samples sit in the world and how they are laid out in memory.
    /// Shared by the baker (which writes samples) and the runtime sampler (which reads them), so
    /// the two can never disagree about the layout.
    /// </summary>
    [Serializable]
    public struct SdfGridData
    {
        public float3 BoundsMin;

        public float CellSize;

        public int3 Resolution;

        public float3 BoundsMax => BoundsMin + (float3)(Resolution - 1) * CellSize;

        public int SampleCount => Resolution.x * Resolution.y * Resolution.z;
        
        public static SdfGridData Generate(Bounds worldBounds, float cellSize)
        {
            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            float3 size = worldBounds.size;

            var resolution = (int3)math.ceil(size / cellSize) + 1;

            return new SdfGridData
            {
                BoundsMin = worldBounds.min,
                CellSize = cellSize,
                Resolution = math.max(resolution, 2),
            };
        }
    }
}
