using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Utils.Extensions
{
    /// <summary>
    /// Evaluates a shape's analytic distance function at a world point. Used by the bake job for
    /// every grid vertex and by the field verifier as the ground truth to compare the baked field
    /// against.
    /// </summary>
    public static class SdfShapeDataExtensions
    {
        /// <summary>
        /// Signed distance from a world-space point to this shape's surface.
        /// </summary>
        public static float Distance(this in SdfShapeData shapeData, float3 worldPoint)
        {
            // Rigid transform only (translate, then rotate). Scale is already folded into the
            // dimensions so the local-space distance is the world-space distance: a Euclidean
            // distance survives rotation and translation but not scaling.
            float3 local = math.rotate(shapeData.WorldToLocalRotation, worldPoint - shapeData.Position);

            switch (shapeData.Kind)
            {
                case SdfShapeKind.Box:
                    return SdfPrimitives.Box(local, shapeData.HalfExtents);
                case SdfShapeKind.Cylinder:
                    return SdfPrimitives.CappedCylinder(local, shapeData.Radius, shapeData.HalfHeight);
                default:
                    // Unreachable for a well-formed shape. Returning +infinity makes a broken
                    // entry contribute nothing to the min-union instead of corrupting the field.
                    return float.PositiveInfinity;
            }
        }
    }
}