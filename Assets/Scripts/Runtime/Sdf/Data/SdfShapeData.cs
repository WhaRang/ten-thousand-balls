using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Data
{
    public enum SdfShapeKind
    {
        Box,
        Cylinder,
    }

    public struct SdfShapeData
    {
        public SdfShapeKind Kind;

        public float3 Position;

        public quaternion WorldToLocalRotation;

        public float3 HalfExtents;

        public float Radius;

        public float HalfHeight;

        public float3 LocalHalfExtents => Kind == SdfShapeKind.Cylinder
            ? new float3(Radius, HalfHeight, Radius)
            : HalfExtents;
    }
}
