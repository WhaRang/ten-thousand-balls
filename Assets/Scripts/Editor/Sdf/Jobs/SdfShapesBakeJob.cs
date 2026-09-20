using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Scripts.Editor.Sdf.Jobs
{
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
            var point = GridData.SampleToWorld(GridData.Unflatten(index));

            float minDistanceToShape = float.PositiveInfinity;
            for (int i = 0; i < Shapes.Length; i++)
            {
                minDistanceToShape = math.min(minDistanceToShape, Shapes[i].Distance(point));
            }

            Distances[index] = minDistanceToShape;
        }
    }
}
