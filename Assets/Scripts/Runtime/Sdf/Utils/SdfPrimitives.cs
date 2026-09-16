using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Scripts.Runtime.Sdf.Utils
{
    /// <summary>
    /// Exact signed distance functions for the primitives the scene is built from.
    /// All functions take a point in the primitive's local frame, centred on the shape,
    /// and return the Euclidean distance to its surface: positive outside, negative inside.
    ///
    /// Only Unity.Mathematics types and static math calls are used so the same code is
    /// valid inside Burst jobs (baker and, later, the runtime sampler).
    /// </summary>
    public static class SdfPrimitives
    {
        /// <summary>
        /// Axis-aligned box centred at the origin with the given half extents.
        /// </summary>
        public static float Box(float3 p, float3 halfExtents)
        {
            // Fold all eight octants onto one so that we only reason about the corner where
            // every coordinate is positive. After the subtraction, q.axis > 0 means the point is
            // beyond the face on that axis, q.axis < 0 means it is between the two faces.
            float3 q = abs(p) - halfExtents;

            // Outside: the nearest surface point is found by clamping each coordinate back to the
            // face, so the distance is the length of the part of q that sticks out. Axes with
            // negative q contribute nothing because they are already within the box's extent.
            float outside = length(max(q, 0f));

            // Inside: every q is negative and the nearest face is the one we are least deep behind,
            // i.e. the largest (least negative) component. Outside the box this term is 0 because
            // at least one component is positive, so the two terms never overlap.
            float inside = min(max(q.x, max(q.y, q.z)), 0f);

            return outside + inside;
        }

        /// <summary>
        /// Cylinder along the local Y axis, centred at the origin, with flat caps.
        /// </summary>
        public static float CappedCylinder(float3 p, float radius, float halfHeight)
        {
            // A cylinder is rotationally symmetric about Y, so collapse the problem into 2D:
            // horizontal distance from the axis vs. height. In that (r, y) plane the cylinder is
            // a rectangle of size (radius, halfHeight), and the box formula above applies verbatim.
            float2 d = abs(float2(length(p.xz), p.y)) - float2(radius, halfHeight);

            float outside = length(max(d, 0f));
            float inside = min(max(d.x, d.y), 0f);

            return outside + inside;
        }
    }
}
