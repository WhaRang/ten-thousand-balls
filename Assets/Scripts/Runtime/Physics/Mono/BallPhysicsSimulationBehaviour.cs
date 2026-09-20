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
    /// handle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallPhysicsSimulationBehaviour : MonoBehaviour
    {
        private const int JobBatchSize = 64;

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

        private SdfBakedFieldData _fieldData;
        private NativeArray<float3> _positions;
        private NativeArray<float3> _velocities;
        private JobHandle _physicsJob;
        private bool _isRunning;

        private float _timeDebt;
        private uint _frameSeed;
        
        private readonly Stopwatch _jobStopwatch = new();

        public int Count => ballCount;
        public float Radius => physicsSettings.Radius;

        public double LastJobMilliseconds { get; private set; }

        public int LastStepCount { get; private set; }

        public float FixedDeltaTime => physicsSettings.FixedDeltaTime;

        public SdfGridData FieldGrid => _fieldData.Grid;

        public NativeArray<float3> Positions => _positions;

        private void Start()
        {
            physicsSettings.Clamp();
            _fieldData = SdfBakedFieldLoader.Load(sdfAsset, Allocator.Persistent);
            
            AllocateNativeArrays(ballCount);
            SpawnAllBalls();
            
            _isRunning = true;
        }

        private void Update()
        {
            if (!_isRunning)
            {
                return;
            }

            physicsSettings.Clamp();

            _timeDebt += Time.deltaTime;
            int steps = math.min((int)(_timeDebt / physicsSettings.FixedDeltaTime), physicsSettings.MaxStepsPerFrame);
            _timeDebt = math.min(_timeDebt - steps * physicsSettings.FixedDeltaTime, physicsSettings.FixedDeltaTime);
            
            LastStepCount = steps;
            
            if (steps == 0)
            {
                return;
            }

            _frameSeed++;
            _jobStopwatch.Restart();
            
            _physicsJob = new BallPhysicsJob
            {
                SdfField = _fieldData,
                PhysicsSettings = physicsSettings,
                StepsThisFrame = steps,
                FrameSeed = _frameSeed,
                Positions = _positions,
                Velocities = _velocities,
            }.Schedule(ballCount, JobBatchSize);
        }

        private void LateUpdate()
        {
            if (!_isRunning)
            {
                return;
            }

            _physicsJob.Complete();
            _jobStopwatch.Stop();
            LastJobMilliseconds = _jobStopwatch.Elapsed.TotalMilliseconds;
        }

        private void OnDestroy()
        {
            _physicsJob.Complete();
            _isRunning = false;

            if (_positions.IsCreated) 
                _positions.Dispose();
            
            if (_velocities.IsCreated) 
                _velocities.Dispose();
            
            if (_fieldData.Distances.IsCreated) 
                _fieldData.Distances.Dispose();
        }

        public void Respawn()
        {
            _physicsJob.Complete();
            SpawnAllBalls();
        }

        public void Resize(int newCount)
        {
            _physicsJob.Complete();
            _positions.Dispose();
            _velocities.Dispose();
            
            ballCount = math.max(newCount, 1);
            AllocateNativeArrays(ballCount);
            SpawnAllBalls();
        }

        private void AllocateNativeArrays(int count)
        {
            _positions = new NativeArray<float3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _velocities = new NativeArray<float3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private void SpawnAllBalls()
        {
            var random = new Random(math.max(seed, 1u));
            for (int i = 0; i < ballCount; i++)
            {
                _positions[i] = random.NextFloat3(physicsSettings.SpawnMin, physicsSettings.SpawnMax);
                _velocities[i] = float3.zero;
            }
        }

        #region Gizmos and Editor
        
        private void OnDrawGizmos()
        {
            DrawSpawnAndKillGizmos();
        }

        private void DrawSpawnAndKillGizmos()
        {
            var spawnMin = math.min(physicsSettings.SpawnMin, physicsSettings.SpawnMax);
            var spawnMax = math.max(physicsSettings.SpawnMin, physicsSettings.SpawnMax);
            var spawnSize = spawnMax - spawnMin;
            var spawnCentre = (spawnMin + spawnMax) * 0.5f;

            Gizmos.color = new Color(0.3f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(spawnCentre, spawnSize);

            const float killSheetPadding = 2f;
            
            var killCentre = new float3(spawnCentre.x, physicsSettings.KillHeight, spawnCentre.z);
            var killSize = new float3(spawnSize.x + killSheetPadding * 2f, 0f, spawnSize.z + killSheetPadding * 2f);

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.12f);
            Gizmos.DrawCube(killCentre, killSize);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(killCentre, killSize);
        }

        [ContextMenu("Log Stats")]
        private void LogStats()
        {
            if (!_isRunning)
            {
                Debug.Log("Simulation not running.");
                return;
            }

            _physicsJob.Complete();

            const float sunkTolerance = 0.005f;      // deeper than this counts as sunk
            const float contactBand = 0.002f;        // within this of touching counts as resting on a surface
            const float settledSpeed = 0.01f;        // slower than this counts as settled

            int sunk = 0, inContact = 0, settled = 0, outsideVolume = 0;
            float minClearance = float.PositiveInfinity;
            float maxContactSpeed = 0f;

            for (int i = 0; i < ballCount; i++)
            {
                var p = _positions[i];
                float speed = math.length(_velocities[i]);
                float clearance = _fieldData.Sample(p).Distance - physicsSettings.Radius; // 0 = exactly touching

                minClearance = math.min(minClearance, clearance);
                if (clearance < -sunkTolerance) sunk++;
                if (speed < settledSpeed) settled++;
                if (math.abs(clearance) <= contactBand)
                {
                    inContact++;
                    maxContactSpeed = math.max(maxContactSpeed, speed);
                }
                if (math.any(p < _fieldData.Grid.BoundsMin) || math.any(p > _fieldData.Grid.BoundsMax)) outsideVolume++;
            }

            Debug.Log(
                $"Balls {ballCount:N0} | sunk (> {sunkTolerance * 1000f} mm into a surface): {sunk} | " +
                $"min clearance {minClearance * 1000f:F2} mm (0 = touching) | in contact: {inContact:N0}, " +
                $"max speed among them {maxContactSpeed * 100f:F2} cm/s | settled (< {settledSpeed * 100f} cm/s): {settled:N0} | " +
                $"outside field volume: {outsideVolume} | last job {LastJobMilliseconds:F2} ms", this);
        }
        
        #endregion
    }
}
