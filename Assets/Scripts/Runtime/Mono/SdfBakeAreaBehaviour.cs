using System.Collections.Generic;
using Scripts.Runtime.Data;
using Scripts.Runtime.ScriptableObjects;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Mono
{
    /// <summary>
    /// Scene-side settings for the bake: which region of the world to sample, how densely, and
    /// which asset receives the result. Lives in the scene so the volume can be seen and tuned in
    /// the scene view next to the shapes it covers. The bake itself is triggered from this
    /// component's inspector (editor assembly); this class only holds the data and draws the gizmo.
    ///
    /// The bounds are in world space and the object's transform is ignored, so moving this object
    /// by accident cannot silently move the field.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SdfBakeAreaBehaviour : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("World-space region that gets sampled. Everything a ball can touch must lie inside it.")]
        private Bounds bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(12f, 4f, 12f));

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Distance between samples in world units. Smaller is more accurate on curved surfaces and costs memory cubically.")]
        private float cellSize = 0.07f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Padding added around the shapes by Fit To Shapes. Should exceed ball radius plus a couple of cells so contacts near the rim never sample outside the volume.")]
        private float fitMargin = 0.5f;

        [SerializeField]
        [Tooltip("Asset that receives the bake. Created by the baker if empty.")]
        private SdfGridTextureSO output;

        public Bounds Bounds => bounds;
        public float CellSize => cellSize;
        public SdfGridTextureSO Output => output;

        /// <summary>The grid these settings describe. Derived, never stored, so it cannot go stale.</summary>
        public SdfGridData GridData => SdfGridData.Generate(bounds, cellSize);

        /// <summary>Called by the baker when it has to create the output asset.</summary>
        public void SetOutput(SdfGridTextureSO gridTextureSo)
        {
            output = gridTextureSo;
        }

        /// <summary>
        /// Shrink-wraps the bounds around the given shapes plus the margin. Works from the same
        /// shape data the baker uses, so what is fitted is exactly what gets baked.
        /// </summary>
        public void FitToShapes(IReadOnlyList<SdfShapeData> shapes)
        {
            if (shapes.Count == 0)
            {
                return;
            }

            float3 min = float.PositiveInfinity;
            float3 max = float.NegativeInfinity;

            foreach (SdfShapeData shape in shapes)
            {
                // World-space AABB of a rotated box: each local axis contributes its half extent
                // spread over the world axes by the absolute value of the rotation matrix column.
                float3x3 localToWorld = new float3x3(math.inverse(shape.WorldToLocalRotation));
                float3 half = shape.LocalHalfExtents;
                float3 worldHalf = math.abs(localToWorld.c0) * half.x
                                 + math.abs(localToWorld.c1) * half.y
                                 + math.abs(localToWorld.c2) * half.z;

                min = math.min(min, shape.Position - worldHalf);
                max = math.max(max, shape.Position + worldHalf);
            }

            min -= fitMargin;
            max += fitMargin;
            bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
