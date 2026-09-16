using Scripts.Runtime.Sdf.Mono;
using Unity.Mathematics;

namespace Scripts.Runtime.Sdf.Data
{
    /// <summary>
    /// Which distance function a shape uses. Deliberately smaller than the set of Unity
    /// primitives: a Plane bakes as a Box (see <see cref="SdfShapeBehaviour"/>), so the job only ever
    /// needs these two.
    /// </summary>
    public enum SdfShapeKind
    {
        Box,
        Cylinder,
    }

    /// <summary>
    /// Everything the bake job needs to know about one static shape, in a plain struct so it can
    /// live in a NativeArray and be read from Burst. Built once per bake from an
    /// <see cref="SdfShapeBehaviour"/> component; the job never touches a Transform.
    /// </summary>
    public struct SdfShapeData
    {
        public SdfShapeKind Kind;

        /// <summary>World-space centre of the shape.</summary>
        public float3 Position;

        /// <summary>
        /// Rotation from world space into the shape's local frame. Stored already inverted so the
        /// hot loop does one rotate per point and no conjugation.
        /// </summary>
        public quaternion WorldToLocalRotation;

        /// <summary>Box only. Half the size along each local axis.</summary>
        public float3 HalfExtents;

        /// <summary>Cylinder only. Distance from the local Y axis to the side surface.</summary>
        public float Radius;

        /// <summary>Cylinder only. Half the height along the local Y axis.</summary>
        public float HalfHeight;

        /// <summary>
        /// Half size of the shape's local-space bounding box, whatever the kind. Used to fit
        /// the bake volume around the shapes.
        /// </summary>
        public float3 LocalHalfExtents => Kind == SdfShapeKind.Cylinder
            ? new float3(Radius, HalfHeight, Radius)
            : HalfExtents;
    }
}
