using Scripts.Runtime.Data;
using Scripts.Runtime.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Scripts.Editor.Jobs
{
    /// <summary>
    /// Evaluates the signed distance field at every grid vertex. One Execute call per vertex,
    /// spread across all cores by the job system and compiled by Burst.
    ///
    /// The field of the whole scene is the union of its shapes, and the distance to a union is
    /// the minimum of the distances to its parts: whichever surface is nearest is the one that
    /// matters. That is the entire algorithm.
    /// </summary>
    [BurstCompile]
    internal struct SdfShapesBakeJob : IJobParallelFor
    {
        public SdfGridData GridData;

        [ReadOnly]
        public NativeArray<SdfShapeData> Shapes;

        [WriteOnly]
        public NativeArray<float> Distances;

        public void Execute(int index)
        {
            float3 point = GridData.SampleToWorld(GridData.Unflatten(index));

            // Start at +infinity so the first shape always wins; an empty shape list leaves the
            // field "infinitely far from anything", which is the correct field for an empty scene.
            float distance = float.PositiveInfinity;
            for (int i = 0; i < Shapes.Length; i++)
            {
                distance = math.min(distance, Shapes[i].Distance(point));
            }

            Distances[index] = distance;
        }
    }
}
