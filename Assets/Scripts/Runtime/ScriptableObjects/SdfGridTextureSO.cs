using Scripts.Runtime.Data;
using UnityEngine;
using UnityEngine.Serialization;

namespace Scripts.Runtime.ScriptableObjects
{
    /// <summary>
    /// The baked signed distance field as a project asset: the grid that says where the samples
    /// are, and the Texture3D that holds them. The texture is a sub-asset of this object so that
    /// re-baking replaces it in place and nothing that references the volume ever breaks.
    ///
    /// Written by the editor-side baker, read once at runtime. Holds no logic of its own.
    /// </summary>
    public sealed class SdfGridTextureSO : ScriptableObject
    {
        [FormerlySerializedAs("grid")] [SerializeField]
        private SdfGridData gridData;

        [SerializeField]
        private Texture3D texture;

        public SdfGridData GridData => gridData;

        /// <summary>
        /// One RFloat sample per grid point, in the memory order defined by <see cref="SdfGridData"/>.
        /// Readable on the CPU, which is how the data reaches the simulation job.
        /// </summary>
        public Texture3D Texture => texture;

        public bool IsBaked => texture != null;

        /// <summary>
        /// Called by the baker after it has produced a new texture. Kept as a method rather than
        /// public setters so the two values can only change together.
        /// </summary>
        public void AssignBake(SdfGridData bakedGridData, Texture3D bakedTexture)
        {
            gridData = bakedGridData;
            texture = bakedTexture;
        }
    }
}
