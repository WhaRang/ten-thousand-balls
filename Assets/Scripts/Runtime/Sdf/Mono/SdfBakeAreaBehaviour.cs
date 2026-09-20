using System.Collections.Generic;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Mono
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

        public SdfGridTextureSO Output => output;

        public SdfGridData GridData => SdfGridData.Generate(bounds, cellSize);

        public void SetOutput(SdfGridTextureSO gridTextureSo)
        {
            output = gridTextureSo;
        }

        public void FitToShapes(IReadOnlyList<SdfShapeData> shapesData)
        {
            if (shapesData.Count == 0)
            {
                return;
            }

            float3 minBound = float.PositiveInfinity;
            float3 maxBound = float.NegativeInfinity;

            foreach (var shapeData in shapesData)
            {
                var localToWorld = new float3x3(math.inverse(shapeData.WorldToLocalRotation));
                var half = shapeData.LocalHalfExtents;
                var worldHalf = math.abs(localToWorld.c0) * half.x
                                + math.abs(localToWorld.c1) * half.y
                                + math.abs(localToWorld.c2) * half.z;

                minBound = math.min(minBound, shapeData.Position - worldHalf);
                maxBound = math.max(maxBound, shapeData.Position + worldHalf);
            }

            minBound -= fitMargin;
            maxBound += fitMargin;
            
            bounds = new Bounds((minBound + maxBound) * 0.5f, maxBound - minBound);
        }

        #region Gizmos
        
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        #endregion
    }
}
