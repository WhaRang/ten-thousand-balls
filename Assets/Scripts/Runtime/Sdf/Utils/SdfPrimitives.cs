using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Scripts.Runtime.Sdf.Utils
{
    public static class SdfPrimitives
    {
        public static float Box(float3 p, float3 halfExtents)
        {
            var q = abs(p) - halfExtents;
            
            float outside = length(max(q, 0f));

            float inside = min(max(q.x, max(q.y, q.z)), 0f);

            return outside + inside;
        }

        public static float CappedCylinder(float3 p, float radius, float halfHeight)
        {
            var d = abs(float2(length(p.xz), p.y)) - float2(radius, halfHeight);

            float outside = length(max(d, 0f));
            float inside = min(max(d.x, d.y), 0f);

            return outside + inside;
        }
    }
}
