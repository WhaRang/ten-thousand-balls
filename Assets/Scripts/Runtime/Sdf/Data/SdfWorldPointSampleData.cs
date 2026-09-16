using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Data
{
    /// <summary>
    /// What the field says about one world point. Both values come from the same eight vertex
    /// fetches, which is why they are returned together: asking for them separately would double
    /// the memory traffic of a job that is bound by memory, not arithmetic.
    /// </summary>
    public readonly struct SdfWorldPointSampleData
    {
        /// <summary>Signed distance to the nearest surface in metres: positive outside, negative inside.</summary>
        public readonly float Distance;

        /// <summary>
        /// Unit vector pointing away from the nearest surface, i.e. the direction along which
        /// Distance grows fastest. Moving a point by (radius - Distance) along it resolves a contact.
        /// </summary>
        public readonly float3 Gradient;

        public SdfWorldPointSampleData(float distance, float3 gradient)
        {
            Distance = distance;
            Gradient = gradient;
        }
    }
}
