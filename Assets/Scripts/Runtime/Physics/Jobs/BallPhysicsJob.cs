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
    /// Advances every ball through this frame's substeps. One Execute per ball, all substeps inside
    /// it: balls never interact, so each one's whole frame is independent work and its state stays
    /// in registers from the first substep to the last.
    ///
    /// Per substep: respawn if fallen away, gravity, move along the velocity in field-bounded steps
    /// (no tunnelling), then resolve whatever contact the move ended in.
    /// </summary>
    [BurstCompile]
    public struct BallPhysicsJob : IJobParallelFor
    {
        /// <summary>The baked field. Its sample array carries its own ReadOnly attribute.</summary>
        public SdfBakedFieldData SdfField;

        public BallPhysicsSettingsData PhysicsSettings;

        /// <summary>How many fixed substeps this frame owes. Decided by the accumulator on the main thread.</summary>
        public int StepsThisFrame;

        /// <summary>Changes every frame so a ball respawning twice does not land in the same spot.</summary>
        public uint FrameSeed;

        public NativeArray<float3> Positions;
        public NativeArray<float3> Velocities;

        public void Execute(int index)
        {
            float3 position = Positions[index];
            float3 velocity = Velocities[index];
            float dt = PhysicsSettings.FixedDeltaTime;

            for (int step = 0; step < StepsThisFrame; step++)
            {
                if (position.y < PhysicsSettings.KillHeight)
                {
                    Respawn(index, out position, out velocity);
                }

                // Symplectic Euler: velocity first, then position with the new velocity. Bounded
                // energy error, so bounces decay as restitution says instead of growing.
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
            var random = Random.CreateFromIndex(min(seed, uint.MaxValue - 1)); // MaxValue is the one reserved index
            position = random.NextFloat3(PhysicsSettings.SpawnMin, PhysicsSettings.SpawnMax);
            velocity = Unity.Mathematics.float3.zero;
        }

        /// <summary>
        /// Applies a displacement without ever crossing a surface. The field value at the ball's
        /// centre minus the radius is how far the ball can travel in any direction and touch
        /// nothing, so the displacement is consumed in pieces of that length. Almost every ball
        /// finishes on the first sample; only balls near a surface and moving fast need more.
        /// </summary>
        private float3 Move(float3 position, float3 displacement)
        {
            float3 remaining = displacement;
            SdfWorldPointSampleData sdfSample = default;

            for (int i = 0; i < PhysicsSettings.MaxTraceSteps; i++)
            {
                sdfSample = SdfField.Sample(position);
                float free = sdfSample.Distance - PhysicsSettings.Radius;
                float remainingLength = length(remaining);

                if (free >= remainingLength)
                {
                    return position + remaining; // the whole remaining step is clear
                }

                if (free <= PhysicsSettings.ContactEpsilon)
                {
                    return position + Slide(remaining, sdfSample.Gradient); // touching: slide along the surface
                }

                // Division is safe: free > epsilon >= 0 and remainingLength > free, so both are positive.
                float3 direction = remaining / remainingLength;
                position += direction * free;
                remaining -= direction * free;
            }

            // Budget spent while skimming just above a surface. Do not drop the leftover motion, only
            // the part of it that points into the surface; the contact step catches any shallow overlap.
            return position + Slide(remaining, sdfSample.Gradient);
        }

        /// <summary>Removes the component of a displacement that points into a surface, keeps the rest.</summary>
        private static float3 Slide(float3 displacement, float3 outwardNormal)
        {
            float into = min(dot(displacement, outwardNormal), 0f);
            return displacement - into * outwardNormal;
        }

        /// <summary>
        /// If the ball is touching a surface: push it out of any overlap along the field gradient,
        /// then respond in velocity. Position-based projection has no spring term, so there is
        /// nothing to oscillate.
        ///
        /// "Touching" means within the contact epsilon, not only overlapping. A resting ball sits
        /// exactly at the radius, so an overlap-only test would skip it every substep and its
        /// velocity would keep accumulating gravity while its position stood still.
        /// </summary>
        private void ResolveContact(ref float3 position, ref float3 velocity)
        {
            SdfWorldPointSampleData sdfSample = SdfField.Sample(position);
            float clearance = sdfSample.Distance - PhysicsSettings.Radius;
            if (clearance > PhysicsSettings.ContactEpsilon)
            {
                return; // not touching anything
            }

            float3 normal = sdfSample.Gradient;
            if (clearance < 0f)
            {
                position -= clearance * normal; // overlapping: move out by exactly the overlap
            }

            float normalSpeed = dot(velocity, normal);
            if (normalSpeed >= 0f)
            {
                return; // already moving away; the push-out was enough
            }

            float approach = -normalSpeed;
            float3 tangential = velocity - normalSpeed * normal;

            // Below the threshold there is no bounce at all. A settled ball approaches at g*dt per
            // substep, far under it, so its normal velocity is simply zeroed: this is what stops
            // micro-bouncing. Above it, a fraction of the approach speed comes back.
            float bounce = approach >= PhysicsSettings.BounceThreshold ? approach * PhysicsSettings.Restitution : 0f;

            // Coulomb friction in impulse form: the tangential impulse cannot exceed μ times the
            // normal impulse that stopped (and possibly reversed) the approach. For a resting ball
            // that impulse is g*dt, so sliding decelerates at exactly μ*g, independent of dt.
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
