using System;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Physics.Data
{
    /// <summary>
    /// Every number the simulation job needs, in one serializable block: edited in the inspector,
    /// copied by value into the job each frame. Plain floats and float3s only, so Burst can hold it.
    ///
    /// Ball count and random seed are deliberately not here: they describe a run, not the physics.
    /// </summary>
    [Serializable]
    public struct BallPhysicsSettingsData
    {
        [Header("Balls")]
        [Min(0.001f)]
        [Tooltip("Ball radius in metres. Contact is decided at this distance from a surface.")]
        public float Radius;

        [Tooltip("Acceleration applied every substep, m/s².")]
        public float3 Gravity;

        [Header("Time")]
        [Min(0.0001f)]
        [Tooltip("Fixed substep length in seconds. The contact constants below are tuned for this value.")]
        public float FixedDeltaTime;

        [Range(1, 16)]
        [Tooltip("Cap on substeps per frame. A slow frame runs the simulation in slow motion instead of doing more work.")]
        public int MaxStepsPerFrame;

        [Header("Contact")]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the approach speed returned as bounce. 0 = dead stop, 1 = perfectly elastic.")]
        public float Restitution;

        [Min(0f)]
        [Tooltip("Approach speeds below this do not bounce at all. This is what stops a settled ball from buzzing.")]
        public float BounceThreshold;

        [Min(0f)]
        [Tooltip("Coulomb friction coefficient μ. A ball sliding on the floor decelerates at μ·g.")]
        public float Friction;

        [Min(0f)]
        [Tooltip("Distance to a surface, in metres, below which a ball counts as touching it during the move.")]
        public float ContactEpsilon;

        [Range(1, 8)]
        [Tooltip("Maximum field samples spent walking one substep's displacement. Almost always 1 is enough.")]
        public int MaxTraceSteps;

        [Header("Spawn")]
        [Tooltip("World-space box balls are spawned into, and respawned into after falling off.")]
        public float3 SpawnMin;
        public float3 SpawnMax;

        [Tooltip("A ball whose centre drops below this height is respawned.")]
        public float KillHeight;

        public static BallPhysicsSettingsData Default => new BallPhysicsSettingsData
        {
            Radius = 0.06f,
            Gravity = new float3(0f, -9.81f, 0f),
            FixedDeltaTime = 1f / 120f,
            MaxStepsPerFrame = 4,
            Restitution = 0.3f,
            BounceThreshold = 0.5f,
            Friction = 0.4f,
            ContactEpsilon = 0.001f,
            MaxTraceSteps = 4,
            SpawnMin = new float3(-4.5f, 6f, -4.5f),
            SpawnMax = new float3(4.5f, 10f, 4.5f),
            KillHeight = -1f,
        };

        /// <summary>
        /// Repairs values the inspector attributes cannot guard (they only apply to what is typed into
        /// a field, not to a struct built in code) so the job never sees a value it cannot handle.
        /// </summary>
        public void Clamp()
        {
            Radius = math.max(Radius, 0.001f);
            FixedDeltaTime = math.max(FixedDeltaTime, 0.0001f);
            MaxStepsPerFrame = math.clamp(MaxStepsPerFrame, 1, 16);
            Restitution = math.saturate(Restitution);
            BounceThreshold = math.max(BounceThreshold, 0f);
            Friction = math.max(Friction, 0f);
            ContactEpsilon = math.max(ContactEpsilon, 0f);
            MaxTraceSteps = math.clamp(MaxTraceSteps, 1, 8);

            // A box with min above max would make NextFloat3 misbehave; sort the corners.
            float3 lo = math.min(SpawnMin, SpawnMax);
            float3 hi = math.max(SpawnMin, SpawnMax);
            SpawnMin = lo;
            SpawnMax = hi;
        }
    }
}
