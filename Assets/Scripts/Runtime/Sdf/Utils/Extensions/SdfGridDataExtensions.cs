using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Utils.Extensions
{
    /// <summary>
    /// Conversions between world space, grid space and the flat sample array. These are the
    /// operational form of the conventions documented on <see cref="SdfGridData"/>; the baker
    /// and the sampler both go through them, so they cannot disagree about the layout.
    /// </summary>
    public static class SdfGridDataExtensions
    {
        /// <summary>Position of the point in continuous grid units, where integers are sample positions.</summary>
        public static float3 WorldToGrid(this in SdfGridData gridData, float3 worldPoint)
        {
            return (worldPoint - gridData.BoundsMin) / gridData.CellSize;
        }

        /// <summary>World position of a sample.</summary>
        public static float3 SampleToWorld(this in SdfGridData gridData, int3 sample)
        {
            return gridData.BoundsMin + (float3)sample * gridData.CellSize;
        }

        /// <summary>Index of a sample in the flat array.</summary>
        public static int Flatten(this in SdfGridData gridData, int3 sample)
        {
            return sample.x + gridData.Resolution.x * (sample.y + gridData.Resolution.y * sample.z);
        }

        /// <summary>Inverse of <see cref="Flatten"/>. Used by the bake job, which iterates flat indices.</summary>
        public static int3 Unflatten(this in SdfGridData gridData, int flatIndex)
        {
            int x = flatIndex % gridData.Resolution.x;
            int rest = flatIndex / gridData.Resolution.x;
            int y = rest % gridData.Resolution.y;
            int z = rest / gridData.Resolution.y;
            
            return new int3(x, y, z);
        }
    }
}