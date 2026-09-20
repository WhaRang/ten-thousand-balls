using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Utils.Extensions
{
    public static class SdfShapeDataExtensions
    {
        public static float Distance(this in SdfShapeData shapeData, float3 worldPoint)
        {
            var local = math.rotate(shapeData.WorldToLocalRotation, worldPoint - shapeData.Position);

            switch (shapeData.Kind)
            {
                case SdfShapeKind.Box:
                    return SdfPrimitives.Box(local, shapeData.HalfExtents);
                case SdfShapeKind.Cylinder:
                    return SdfPrimitives.CappedCylinder(local, shapeData.Radius, shapeData.HalfHeight);
                default:
                    return float.PositiveInfinity;
            }
        }
    }
}