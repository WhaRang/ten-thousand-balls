using System.Diagnostics;
using Scripts.Runtime.Physics.Data;
using Scripts.Runtime.Physics.Jobs;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Scripts.Runtime.Sdf.Utils;
using Scripts.Runtime.Sdf.Utils.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = Unity.Mathematics.Random;

namespace Scripts.Runtime.Physics.Mono
{
    /// <summary>
    /// The one object that owns the simulation: the loaded field, the two ball arrays, and the job
    /// handle. There is nothing per ball here; balls are rows in two arrays.
    ///
    /// Frame flow: Update decides how many fixed substeps this frame owes and schedules the job;
    /// LateUpdate waits for it. Between LateUpdate and the next Update the arrays are safe to read
    /// on the main thread, which is when the renderer (Phase 4) uploads positions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallPhysicsSimulationBehaviour : MonoBehaviour
    {
        /// <summary>
        /// Balls per work item handed to a worker thread. ~156 items at 10k balls: enough for the
        /// cores to balance the uneven cost of balls in contact against balls in free fall.
        /// </summary>
        private const int JobBatchSize = 64;

        /// <summary>Temporary, Phase 3 only: how many balls the gizmo draw shows.</summary>
        private const int GizmoBallCount = 300;

        [SerializeField]
        private SdfGridTextureSO sdfAsset;

        [SerializeField]
        [Min(1)]
        private int ballCount = 10_000;

        [SerializeField]
        [Tooltip("Seed for the initial spawn positions. Same seed, same start.")]
        private uint seed = 1;

        [SerializeField]
        private BallPhysicsSettingsData physicsSettings = BallPhysicsSettingsData.Default;

        private SdfBakedFieldData field;
        private NativeArray<float3> positions;
        private NativeArray<float3> velocities;
        private JobHandle physicsJob;
        private bool isRunning;

        /// <summary>Simulated time not yet consumed by whole substeps.</summary>
        private float timeDebt;

        private uint frameSeed;
        private readonly Stopwatch jobStopwatch = new Stopwatch();

        public int Count => ballCount;
        public float Radius => physicsSettings.Radius;

        /// <summary>Wall time from scheduling the job to its completion, for the on-screen readout.</summary>
        public double LastJobMilliseconds { get; private set; }

        /// <summary>
        /// Current positions. Valid to read between LateUpdate and the next Update, i.e. after the
        /// job has completed and before the next one is scheduled.
        /// </summary>
        public NativeArray<float3>.ReadOnly Positions => positions.AsReadOnly();

        private void Start()
        {
            physicsSettings.Clamp();
            field = SdfBakedFieldLoader.Load(sdfAsset, Allocator.Persistent);
            AllocateNativeArrays(ballCount);
            SpawnAllBalls();
            isRunning = true;
        }

        private void Update()
        {
            if (!isRunning)
            {
                return;
            }

            // Live tweaking in the inspector is allowed; make sure the job never sees nonsense.
            physicsSettings.Clamp();

            // Fixed-step accumulator. Whole substeps are taken out of the debt; the cap turns an
            // overloaded frame into slow motion instead of more work, and the leftover debt is
            // capped too so a long stall does not become a burst of catch-up steps afterwards.
            timeDebt += Time.deltaTime;
            int steps = math.min((int)(timeDebt / physicsSettings.FixedDeltaTime), physicsSettings.MaxStepsPerFrame);
            timeDebt = math.min(timeDebt - steps * physicsSettings.FixedDeltaTime, physicsSettings.FixedDeltaTime);
            
            if (steps == 0)
            {
                return; // faster than the fixed rate this frame: nothing to simulate yet
            }

            frameSeed++;
            jobStopwatch.Restart();
            
            physicsJob = new BallPhysicsJob
            {
                SdfField = field,
                PhysicsSettings = physicsSettings,
                StepsThisFrame = steps,
                FrameSeed = frameSeed,
                Positions = positions,
                Velocities = velocities,
            }.Schedule(ballCount, JobBatchSize);
        }

        private void LateUpdate()
        {
            if (!isRunning)
            {
                return;
            }

            physicsJob.Complete();
            jobStopwatch.Stop();
            LastJobMilliseconds = jobStopwatch.Elapsed.TotalMilliseconds;
        }

        private void OnDestroy()
        {
            // A job must never outlive the memory it reads; complete before freeing anything.
            physicsJob.Complete();
            isRunning = false;

            if (positions.IsCreated) 
                positions.Dispose();
            
            if (velocities.IsCreated) 
                velocities.Dispose();
            
            if (field.Distances.IsCreated) 
                field.Distances.Dispose();
        }

        /// <summary>Puts every ball back in the spawn box at rest. The "Reset" of the on-screen panel.</summary>
        public void Respawn()
        {
            physicsJob.Complete();
            SpawnAllBalls();
        }

        /// <summary>Changes the ball count. Reallocates once, then respawns; not a per-frame operation.</summary>
        public void Resize(int newCount)
        {
            physicsJob.Complete();
            positions.Dispose();
            velocities.Dispose();
            ballCount = math.max(newCount, 1);
            AllocateNativeArrays(ballCount);
            SpawnAllBalls();
        }

        private void AllocateNativeArrays(int count)
        {
            // Uninitialised because SpawnAll writes every element before anything reads them.
            positions = new NativeArray<float3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            velocities = new NativeArray<float3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private void SpawnAllBalls()
        {
            // Random(0) is invalid by design of the generator; 1 is the smallest usable seed.
            var random = new Random(math.max(seed, 1u));
            for (int i = 0; i < ballCount; i++)
            {
                positions[i] = random.NextFloat3(physicsSettings.SpawnMin, physicsSettings.SpawnMax);
                velocities[i] = float3.zero;
            }
        }

        private void OnDrawGizmos()
        {
            DrawSpawnAndKillGizmos();
            DrawBallGizmos();
        }

        /// <summary>
        /// Where balls come from and where they are taken away: the spawn box as a wire cuboid, the
        /// kill height as a faint red sheet under it. Configuration, so it draws in edit mode too.
        /// </summary>
        private void DrawSpawnAndKillGizmos()
        {
            float3 spawnMin = math.min(physicsSettings.SpawnMin, physicsSettings.SpawnMax);
            float3 spawnMax = math.max(physicsSettings.SpawnMin, physicsSettings.SpawnMax);
            float3 spawnSize = spawnMax - spawnMin;
            float3 spawnCentre = (spawnMin + spawnMax) * 0.5f;

            Gizmos.color = new Color(0.3f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(spawnCentre, spawnSize);

            // The kill height is an infinite plane; draw it with the spawn footprint plus a margin
            // so it reads as "below the action" without covering the whole scene view.
            const float killSheetPadding = 2f;
            float3 killCentre = new float3(spawnCentre.x, physicsSettings.KillHeight, spawnCentre.z);
            float3 killSize = new float3(spawnSize.x + killSheetPadding * 2f, 0f, spawnSize.z + killSheetPadding * 2f);

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.12f);
            Gizmos.DrawCube(killCentre, killSize);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(killCentre, killSize);
        }

        // ----- Phase 3 verification only; removed in Phase 4 when the real renderer exists. -----

        private void DrawBallGizmos()
        {
            if (!isRunning)
            {
                return;
            }

            physicsJob.Complete(); // gizmos may draw while the job is in flight; never read a live array
            Gizmos.color = new Color(0.3f, 0.6f, 1f);
            int shown = math.min(GizmoBallCount, ballCount);
            for (int i = 0; i < shown; i++)
            {
                Gizmos.DrawSphere(positions[i], physicsSettings.Radius);
            }
        }

        /// <summary>
        /// One pass over the arrays with the numbers the brief asks us to prove: nothing sinks,
        /// nothing floats, nothing buzzes once settled. Uses the field itself as the reference so
        /// the check does not depend on knowing where the plane is.
        /// </summary>
        [ContextMenu("Log Stats")]
        private void LogStats()
        {
            if (!isRunning)
            {
                Debug.Log("Simulation not running.");
                return;
            }

            physicsJob.Complete();

            const float sunkTolerance = 0.005f;      // deeper than this counts as sunk
            const float contactBand = 0.002f;        // within this of touching counts as resting on a surface
            const float settledSpeed = 0.01f;        // slower than this counts as settled

            int sunk = 0, inContact = 0, settled = 0, outsideVolume = 0;
            float minClearance = float.PositiveInfinity;
            float maxContactSpeed = 0f;

            for (int i = 0; i < ballCount; i++)
            {
                float3 p = positions[i];
                float speed = math.length(velocities[i]);
                float clearance = field.Sample(p).Distance - physicsSettings.Radius; // 0 = exactly touching

                minClearance = math.min(minClearance, clearance);
                if (clearance < -sunkTolerance) sunk++;
                if (speed < settledSpeed) settled++;
                if (math.abs(clearance) <= contactBand)
                {
                    inContact++;
                    maxContactSpeed = math.max(maxContactSpeed, speed);
                }
                if (math.any(p < field.Grid.BoundsMin) || math.any(p > field.Grid.BoundsMax)) outsideVolume++;
            }

            Debug.Log(
                $"Balls {ballCount:N0} | sunk (> {sunkTolerance * 1000f} mm into a surface): {sunk} | " +
                $"min clearance {minClearance * 1000f:F2} mm (0 = touching) | in contact: {inContact:N0}, " +
                $"max speed among them {maxContactSpeed * 100f:F2} cm/s | settled (< {settledSpeed * 100f} cm/s): {settled:N0} | " +
                $"outside field volume: {outsideVolume} | last job {LastJobMilliseconds:F2} ms", this);
        }
    }
}
