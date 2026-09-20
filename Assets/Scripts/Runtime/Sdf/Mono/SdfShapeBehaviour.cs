using System;
using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Mono
{
    public sealed class SdfShapeBehaviour : MonoBehaviour
    {
        public enum Primitive
        {
            Cylinder,
            Plane,
        }

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

        public SdfShapeData ToData()
        {
            var t = transform;
            var lossyScale = math.abs(t.lossyScale);
            quaternion rotation = t.rotation;

            var shapeData = new SdfShapeData
            {
                Position = t.position,
                WorldToLocalRotation = math.inverse(rotation),
            };

            switch (primitive)
            {
                case Primitive.Cylinder:
                    if (!Mathf.Approximately(lossyScale.x, lossyScale.z))
                    {
                        throw new InvalidOperationException(
                            $"{name}: a cylinder scaled unevenly in X and Z is an elliptic cylinder, " +
                            "which has no cheap exact distance function. Use equal X and Z scale.");
                    }
                    
                    shapeData.Kind = SdfShapeKind.Cylinder;
                    shapeData.Radius = CylinderRadius * lossyScale.x;
                    shapeData.HalfHeight = CylinderHalfHeight * lossyScale.y;
                    
                    break;

                case Primitive.Plane:
                    float halfThickness = planeThickness * 0.5f;
                    
                    shapeData.Kind = SdfShapeKind.Box;
                    shapeData.HalfExtents = new float3(PlaneHalfExtent * lossyScale.x, halfThickness, PlaneHalfExtent * lossyScale.z);
                    shapeData.Position -= math.rotate(rotation, new float3(0f, halfThickness, 0f));
                    
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(primitive), primitive, null);
            }

            return shapeData;
        }

#if UNITY_EDITOR
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
