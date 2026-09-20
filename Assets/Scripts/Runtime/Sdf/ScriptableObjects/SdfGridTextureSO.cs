using Scripts.Runtime.Sdf.Data;
using UnityEngine;

namespace Scripts.Runtime.Sdf.ScriptableObjects
{
    /// <summary>
    /// The baked signed distance field as a project asset: the grid that says where the samples
    /// are, and the Texture3D that holds them. The texture is a sub-asset of this object so that
    /// re-baking replaces it in place and nothing that references the volume ever breaks.
    /// </summary>
    public sealed class SdfGridTextureSO : ScriptableObject
    {
        [SerializeField]
        private SdfGridData gridData;

        [SerializeField]
        private Texture3D texture;

        public SdfGridData GridData => gridData;

        public Texture3D Texture => texture;

        public bool IsBaked => texture != null;

        public void AssignBake(SdfGridData bakedGridData, Texture3D bakedTexture)
        {
            gridData = bakedGridData;
            texture = bakedTexture;
        }
    }
}
