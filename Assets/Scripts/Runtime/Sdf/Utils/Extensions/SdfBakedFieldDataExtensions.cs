using Scripts.Runtime.Common;
using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Scripts.Runtime.Sdf.Utils.Extensions
{
    public static class SdfBakedFieldDataExtensions
    {
        public static SdfWorldPointSampleData Sample(this in SdfBakedFieldData fieldData, float3 worldPoint)
        {
            var clamped = clamp(worldPoint, fieldData.Grid.BoundsMin, fieldData.Grid.BoundsMax);
            var boundary = SampleTrilinear(fieldData, clamped);

            var offset = worldPoint - clamped;
            if (all(offset == 0f))
            {
                return boundary;
            }

            float boundaryDistance = max(boundary.Distance, 0f);
            float distance = sqrt(boundaryDistance * boundaryDistance + lengthsq(offset));

            var direction = normalizesafe(offset + boundaryDistance * boundary.Gradient, boundary.Gradient);
            return new SdfWorldPointSampleData(distance, direction);
        }

        private static SdfWorldPointSampleData SampleTrilinear(in SdfBakedFieldData fieldData, float3 worldPoint)
        {
            var gridPoint = fieldData.Grid.WorldToGrid(worldPoint);

            var cell = min((int3)floor(gridPoint), fieldData.Grid.Resolution - 2);
            var f = gridPoint - cell;

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

            float c00 = lerp(c000, c100, f.x);
            float c10 = lerp(c010, c110, f.x);
            float c01 = lerp(c001, c101, f.x);
            float c11 = lerp(c011, c111, f.x);
            float c0 = lerp(c00, c10, f.y);
            float c1 = lerp(c01, c11, f.y);
            float value = lerp(c0, c1, f.z);

            float dx = lerp(lerp(c100 - c000, c110 - c010, f.y), lerp(c101 - c001, c111 - c011, f.y), f.z);
            float dy = lerp(c10 - c00, c11 - c01, f.z);
            float dz = c1 - c0;

            var gradient = new float3(dx, dy, dz) / fieldData.Grid.CellSize;

            float magnitude = length(gradient);
            var direction = magnitude > MathConstants.Epsilon ? gradient / magnitude : new float3(0f, 1f, 0f);

            return new SdfWorldPointSampleData(value, direction);
        }
    }
}