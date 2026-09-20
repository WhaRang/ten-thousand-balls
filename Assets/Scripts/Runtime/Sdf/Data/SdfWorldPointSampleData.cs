using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Data
{
    public readonly struct SdfWorldPointSampleData
    {
        public readonly float Distance;

        public readonly float3 Gradient;

        public SdfWorldPointSampleData(float distance, float3 gradient)
        {
            Distance = distance;
            Gradient = gradient;
        }
    }
}
