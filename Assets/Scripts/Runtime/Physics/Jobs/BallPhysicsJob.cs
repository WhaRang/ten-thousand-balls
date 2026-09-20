using Scripts.Runtime.Common;
using Scripts.Runtime.Physics.Data;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Random = Unity.Mathematics.Random;

namespace Scripts.Runtime.Physics.Jobs
{
    /// <summary>
    /// Advances every ball through this frame's substeps. 
    ///
    /// Per substep: respawn if fallen away, gravity, move along the velocity in field-bounded steps
    /// (no tunnelling), then resolve whatever contact the move ended in.
    /// </summary>
    [BurstCompile]
    public struct BallPhysicsJob : IJobParallelFor
    {
        public SdfBakedFieldData SdfField;

        public BallPhysicsSettingsData PhysicsSettings;

        public int StepsThisFrame;

        public uint FrameSeed;

        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;

        public void Execute(int index)
        {
            var position = Positions[index];
            var velocity = Velocities[index];
            
            float dt = PhysicsSettings.FixedDeltaTime;

            for (int step = 0; step < StepsThisFrame; step++)
            {
                if (position.y < PhysicsSettings.KillHeight)
                {
                    Respawn(index, out position, out velocity);
                }

                velocity += PhysicsSettings.Gravity * dt;
                position = Move(position, velocity * dt);
                
                ResolveContact(ref position, ref velocity);
            }

            Positions[index] = position;
            Velocities[index] = velocity;
        }

        /// <summary>
        /// New random position in the spawn box, at rest. Index and frame seed are hashed together
        /// so neighbouring balls, and the same ball on later frames, get unrelated positions.
        /// </summary>
        private void Respawn(int index, out float3 position, out float3 velocity)
        {
            uint seed = hash(new uint2((uint)index, FrameSeed));
            var random = Random.CreateFromIndex(min(seed, uint.MaxValue - 1));
            
            position = random.NextFloat3(PhysicsSettings.SpawnMin, PhysicsSettings.SpawnMax);
            velocity = Unity.Mathematics.float3.zero;
        }

        private float3 Move(float3 position, float3 displacement)
        {
            var remaining = displacement;
            SdfWorldPointSampleData sdfSample = default;

            for (int i = 0; i < PhysicsSettings.MaxTraceSteps; i++)
            {
                sdfSample = SdfField.Sample(position);
                float free = sdfSample.Distance - PhysicsSettings.Radius;
                float remainingLength = length(remaining);

                if (free >= remainingLength)
                {
                    return position + remaining;
                }

                if (free <= PhysicsSettings.ContactEpsilon)
                {
                    return position + Slide(remaining, sdfSample.Gradient);
                }

                var direction = remaining / remainingLength;
                
                position += direction * free;
                remaining -= direction * free;
            }

            return position + Slide(remaining, sdfSample.Gradient);
        }

        private static float3 Slide(float3 displacement, float3 outwardNormal)
        {
            float into = min(dot(displacement, outwardNormal), 0f);
            return displacement - into * outwardNormal;
        }

        private void ResolveContact(ref float3 position, ref float3 velocity)
        {
            var sdfSample = SdfField.Sample(position);
            float clearance = sdfSample.Distance - PhysicsSettings.Radius;
            
            if (clearance > PhysicsSettings.ContactEpsilon)
            {
                return;
            }

            var normal = sdfSample.Gradient;
            if (clearance < 0f)
            {
                position -= clearance * normal;
            }

            float normalSpeed = dot(velocity, normal);
            if (normalSpeed >= 0f)
            {
                return;
            }

            float approach = -normalSpeed;
            var tangential = velocity - normalSpeed * normal;

            float bounce = approach >= PhysicsSettings.BounceThreshold ? approach * PhysicsSettings.Restitution : 0f;

            // Coulomb friction in impulse form
            float normalImpulse = approach + bounce;
            float tangentialSpeed = length(tangential);
            
            if (tangentialSpeed > MathConstants.Epsilon)
            {
                float reduced = max(0f, tangentialSpeed - PhysicsSettings.Friction * normalImpulse);
                tangential *= reduced / tangentialSpeed;
            }

            velocity = tangential + bounce * normal;
        }
    }
}
