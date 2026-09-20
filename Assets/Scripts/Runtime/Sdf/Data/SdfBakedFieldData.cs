using Unity.Collections;

namespace Scripts.Runtime.Sdf.Data
{
    public struct SdfBakedFieldData
    {
        public SdfGridData Grid;

        [ReadOnly]
        public NativeArray<float> Distances;
    }
}
