using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Utils.Extensions
{
    public static class SdfGridDataExtensions
    {
        public static float3 WorldToGrid(this in SdfGridData gridData, float3 worldPoint)
        {
            return (worldPoint - gridData.BoundsMin) / gridData.CellSize;
        }

        public static float3 SampleToWorld(this in SdfGridData gridData, int3 sample)
        {
            return gridData.BoundsMin + (float3)sample * gridData.CellSize;
        }

        public static int Flatten(this in SdfGridData gridData, int3 sample)
        {
            return sample.x + gridData.Resolution.x * (sample.y + gridData.Resolution.y * sample.z);
        }

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