using Unity.Collections;

namespace Scripts.Runtime.Data
{
    /// <summary>
    /// Read-only view of the baked field, usable from a Burst job and from the main thread alike:
    /// the grid that says where the samples are and the array that holds them. Plain data with no
    /// managed references, so a job holds it by value. Does not own the array; whoever loaded it
    /// disposes it.
    /// </summary>
    public struct SdfBakedFieldData
    {
        public SdfGridData Grid;

        [ReadOnly]
        public NativeArray<float> Distances;
    }
}
