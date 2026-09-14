using System;
using Scripts.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Mono
{
    /// <summary>
    /// Marks a static Unity primitive as part of the baked distance field and converts its
    /// transform into the plain <see cref="SdfShapeData"/> the bake job consumes.
    ///
    /// The shape is declared explicitly rather than sniffed from the mesh so that a wrong
    /// choice is visible in the inspector, not hidden in a string comparison. It has no
    /// per-frame behaviour; the component only exists to be read at bake time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SdfShapeBehaviour : MonoBehaviour
    {
        /// <summary>The Unity primitive this component sits on.</summary>
        public enum Primitive
        {
            Cylinder,
            Plane,
        }

        // Unity's built-in primitive meshes, in local units before the transform's scale.
        private const float CylinderRadius = 0.5f;
        private const float CylinderHalfHeight = 1f;
        private const float PlaneHalfExtent = 5f;

        [SerializeField]
        [Tooltip("Which built-in primitive this object is. Size is derived from that mesh and the transform's scale.")]
        private Primitive primitive = Primitive.Cylinder;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Plane only. The plane mesh has no thickness, so the field treats it as a slab this deep below the surface.")]
        private float planeThickness = 0.5f;

        /// <summary>
        /// Snapshot of this shape for the bake job. Called once per bake, on the main thread,
        /// because only the main thread may read a Transform.
        /// </summary>
        public SdfShapeData ToData()
        {
            Transform t = transform;

            // A negative scale mirrors the mesh, and a mirrored box or cylinder is the same
            // solid, so only the magnitude matters for distances.
            float3 scale = math.abs((float3)t.lossyScale);
            quaternion rotation = t.rotation;

            var data = new SdfShapeData
            {
                Position = t.position,
                WorldToLocalRotation = math.inverse(rotation),
            };

            switch (primitive)
            {
                case Primitive.Cylinder:
                    if (!Mathf.Approximately(scale.x, scale.z))
                    {
                        throw new InvalidOperationException(
                            $"{name}: a cylinder scaled unevenly in X and Z is an elliptic cylinder, " +
                            "which has no cheap exact distance function. Use equal X and Z scale.");
                    }
                    data.Kind = SdfShapeKind.Cylinder;
                    data.Radius = CylinderRadius * scale.x;
                    data.HalfHeight = CylinderHalfHeight * scale.y;
                    break;

                case Primitive.Plane:
                    // The mesh becomes the top face of a box that extends planeThickness downward.
                    // The centre moves down by half the thickness along the object's own down axis,
                    // so a tilted plane still gets its slab on the correct side.
                    float halfThickness = planeThickness * 0.5f;
                    data.Kind = SdfShapeKind.Box;
                    data.HalfExtents = new float3(PlaneHalfExtent * scale.x, halfThickness, PlaneHalfExtent * scale.z);
                    data.Position -= math.rotate(rotation, new float3(0f, halfThickness, 0f));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(primitive), primitive, null);
            }

            return data;
        }

#if UNITY_EDITOR
        // Editor conveniences only: pick the right primitive when the component is added, and
        // warn if the dropdown disagrees with the mesh actually on the object.

        private void Reset()
        {
            primitive = SharedMeshName() == "Plane" ? Primitive.Plane : Primitive.Cylinder;
        }

        private void OnValidate()
        {
            string meshName = SharedMeshName();
            if (meshName != null && meshName != primitive.ToString())
            {
                Debug.LogWarning($"{name}: SdfShape is set to {primitive} but the mesh is '{meshName}'. " +
                                 "The baked field will not match what is rendered.", this);
            }
        }

        private string SharedMeshName()
        {
            var filter = GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : null;
        }
#endif
    }
}
