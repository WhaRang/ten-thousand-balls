using Scripts.Runtime.Common;
using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Scripts.Runtime.Sdf.Utils.Extensions
{
    /// <summary>
    /// The runtime sampler: how a world point is turned into a distance and an outward direction
    /// using the baked vertices. Extension methods rather than struct members so the data types
    /// stay plain; <c>in</c> parameters pass the struct by reference, so no copy is made per call.
    /// </summary>
    public static class SdfBakedFieldDataExtensions
    {
        /// <summary>
        /// Distance and outward direction at any world point, inside or outside the volume.
        /// </summary>
        public static SdfWorldPointSampleData Sample(this in SdfBakedFieldData fieldData, float3 worldPoint)
        {
            float3 clamped = clamp(worldPoint, fieldData.Grid.BoundsMin, fieldData.Grid.BoundsMax);
            SdfWorldPointSampleData boundary = SampleTrilinear(fieldData, clamped);

            float3 offset = worldPoint - clamped;
            if (all(offset == 0f))
            {
                return boundary;
            }

            // Outside the volume there is no data, only a guarantee: every surface lies inside the
            // volume (the bake margin ensures it). For a point p outside an axis-aligned box and its
            // clamp c, the offset to any point s inside the box never cancels the offset p - c on
            // any axis, so |p - s|^2 >= |p - c|^2 + |c - s|^2 for every surface point s. Hence
            //     true distance(p) >= sqrt(distance(c)^2 + |p - c|^2)
            // which is what we return: a rigorous lower bound, so a ball can only ever think it is
            // closer to a surface than it is, never farther, and sphere tracing stays safe.
            // (Linear extrapolation along the boundary gradient was tried first; the verifier
            // showed it overshooting by over a metre where the gradient blends near a medial
            // surface, because the field is a min of two convex functions and not itself convex.)
            float boundaryDistance = max(boundary.Distance, 0f);
            float distance = sqrt(boundaryDistance * boundaryDistance + lengthsq(offset));

            // Direction in which that bound grows: away from the clamp point, bent by the boundary
            // gradient in proportion to how far the surface already was. Only used if a contact is
            // ever resolved out here, which the margin makes impossible; kept sane regardless.
            float3 direction = normalizesafe(offset + boundaryDistance * boundary.Gradient, boundary.Gradient);
            return new SdfWorldPointSampleData(distance, direction);
        }

        /// <summary>
        /// Trilinear value and its exact gradient at a point within the bounds. Private because
        /// an outside point would read past the array; <see cref="Sample"/> clamps first.
        /// </summary>
        private static SdfWorldPointSampleData SampleTrilinear(in SdfBakedFieldData fieldData, float3 worldPoint)
        {
            float3 gridPoint = fieldData.Grid.WorldToGrid(worldPoint);

            // The cell containing the point, capped at the second-to-last vertex so a point on the
            // far face still has a cell to interpolate in; its fraction is then exactly 1 and all
            // weight lands on the last vertex.
            int3 cell = min((int3)floor(gridPoint), fieldData.Grid.Resolution - 2);
            float3 f = gridPoint - cell;

            // Eight corner values. cXYZ: each letter is 0 or 1, the low or high side of the cell
            // on that axis. Neighbours along x are adjacent in memory; y and z are strided.
            int origin = fieldData.Grid.Flatten(cell);
            int strideY = fieldData.Grid.Resolution.x;
            int strideZ = fieldData.Grid.Resolution.x * fieldData.Grid.Resolution.y;

            float c000 = fieldData.Distances[origin];
            float c100 = fieldData.Distances[origin + 1];
            float c010 = fieldData.Distances[origin + strideY];
            float c110 = fieldData.Distances[origin + 1 + strideY];
            float c001 = fieldData.Distances[origin + strideZ];
            float c101 = fieldData.Distances[origin + 1 + strideZ];
            float c011 = fieldData.Distances[origin + strideY + strideZ];
            float c111 = fieldData.Distances[origin + 1 + strideY + strideZ];

            // Trilinear interpolation as three rounds of linear interpolation. Collapse x first:
            // four values on the yz-face through the point. Then y: two values along z. Then z.
            float c00 = lerp(c000, c100, f.x);
            float c10 = lerp(c010, c110, f.x);
            float c01 = lerp(c001, c101, f.x);
            float c11 = lerp(c011, c111, f.x);
            float c0 = lerp(c00, c10, f.y);
            float c1 = lerp(c01, c11, f.y);
            float value = lerp(c0, c1, f.z);

            // Gradient of that same function. Along each axis it is the difference across the cell,
            // weighted by the position along the other two axes. The intermediates above already
            // carry those weightings, so the three components cost a handful of subtractions.
            float dx = lerp(lerp(c100 - c000, c110 - c010, f.y),
                            lerp(c101 - c001, c111 - c011, f.y), f.z);
            float dy = lerp(c10 - c00, c11 - c01, f.z);
            float dz = c1 - c0;

            // The differences are per grid unit; dividing by the cell size makes them per metre.
            float3 gradient = new float3(dx, dy, dz) / fieldData.Grid.CellSize;

            // A true SDF has a unit gradient and the interpolant's is only approximately unit, so
            // normalise: the contact code needs a direction. Zero length is only possible on a
            // medial axis deep inside a solid; +Y is the way out for everything in this scene.
            float magnitude = length(gradient);
            float3 direction = magnitude > MathConstants.Epsilon ? gradient / magnitude : new float3(0f, 1f, 0f);

            return new SdfWorldPointSampleData(value, direction);
        }
    }
}