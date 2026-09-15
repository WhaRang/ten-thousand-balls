using System;
using System.Diagnostics;
using System.IO;
using Scripts.Editor.Jobs;
using Scripts.Runtime.Data;
using Scripts.Runtime.Mono;
using Scripts.Runtime.ScriptableObjects;
using Scripts.Runtime.Utils.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Properties;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Scripts.Editor.Utils
{
    /// <summary>
    /// The editor-time bake step: scene shapes in, committed <see cref="SdfGridTextureSO"/> asset out.
    /// Never runs at load or per frame; it is triggered from the <see cref="SdfBakeAreaBehaviour"/>
    /// inspector. Nothing tracks the scene: re-bake by hand after changing a shape or the settings.
    /// </summary>
    internal static class SdfBaker
    {
        private const string DefaultAssetPath = "Assets/Scripts/Baked/SceneSdf.asset";

        /// <summary>Texture3D cannot exceed this per axis, and the baker refuses earlier than that on memory.</summary>
        private const int MaxResolutionPerAxis = 2048;
        private const long MaxSampleBytes = 256L * 1024 * 1024;

        /// <summary>
        /// Work items per batch handed to a worker thread. Each vertex is a handful of
        /// instructions, so batches are large to keep scheduling overhead negligible while still
        /// leaving hundreds of batches for the cores to balance.
        /// </summary>
        private const int JobBatchSize = 4096;

        public static void Bake(SdfBakeAreaBehaviour areaBehaviour)
        {
            SdfShapeData[] shapes = CollectShapeData();
            if (shapes.Length == 0)
            {
                throw new InvalidOperationException("No SdfShape components in the open scenes; nothing to bake.");
            }

            SdfGridData gridData = areaBehaviour.GridData;
            ValidateGridSize(gridData);

            var stopwatch = Stopwatch.StartNew();
            NativeArray<float> distances = GenerateGridDistances(gridData, shapes);
            long jobMilliseconds = stopwatch.ElapsedMilliseconds;

            // From here on the only job of the try is to guarantee the native array is freed.
            try
            {
                Texture3D texture = CreateTexture(gridData, distances);
                SdfGridTextureSO gridTextureSo = GetOrCreateOutput(areaBehaviour);
                ReplaceBakedTexture(gridTextureSo, texture);
                
                gridTextureSo.AssignBake(gridData, texture);
                EditorUtility.SetDirty(gridTextureSo);
                AssetDatabase.SaveAssets();

                ReportBake(gridTextureSo, distances, shapes, jobMilliseconds, stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                distances.Dispose();
            }
        }

        /// <summary>Snapshots every shape in the open scenes.</summary>
        internal static SdfShapeData[] CollectShapeData()
        {
            SdfShapeBehaviour[] components = Object.FindObjectsByType<SdfShapeBehaviour>(FindObjectsSortMode.None);
            var data = new SdfShapeData[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                data[i] = components[i].ToData();
            }
            return data;
        }

        private static void ValidateGridSize(SdfGridData gridData)
        {
            if (math.any(gridData.Resolution > MaxResolutionPerAxis))
            {
                throw new InvalidOperationException(
                    $"Grid resolution {gridData.Resolution} exceeds the Texture3D limit of {MaxResolutionPerAxis} per axis. Increase the cell size.");
            }

            long bytes = (long)gridData.SampleCount * sizeof(float);
            if (bytes > MaxSampleBytes)
            {
                throw new InvalidOperationException(
                    $"Grid would need {bytes / (1024 * 1024)} MB; the baker refuses above {MaxSampleBytes / (1024 * 1024)} MB. Increase the cell size or shrink the bounds.");
            }
        }

        private static NativeArray<float> GenerateGridDistances(SdfGridData gridData, SdfShapeData[] shapes)
        {
            // TempJob: freed right after the job. Uninitialised: every slot is written by the job.
            var shapesNative = new NativeArray<SdfShapeData>(shapes, Allocator.TempJob);
            var distances = new NativeArray<float>(gridData.SampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                new SdfShapesBakeJob
                {
                    GridData = gridData,
                    Shapes = shapesNative,
                    Distances = distances,
                }.Schedule(gridData.SampleCount, JobBatchSize).Complete();
            }
            finally
            {
                shapesNative.Dispose();
            }

            return distances;
        }

        private static Texture3D CreateTexture(SdfGridData gridData, NativeArray<float> distances)
        {
            int3 res = gridData.Resolution;

            // RFloat keeps the exact baked values. No mip chain: the field is sampled at one
            // resolution and mips would only cost memory. Clamp and bilinear are irrelevant to
            // the CPU sampler but make the inspector preview and any debug shader behave.
            var texture = new Texture3D(res.x, res.y, res.z, TextureFormat.RFloat, mipChain: false)
            {
                name = "SdfTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // Same memory order as SdfGrid.Flatten, so the array goes in as-is.
            texture.SetPixelData(distances, mipLevel: 0);

            // makeNoLongerReadable: false keeps the CPU copy, which is what the runtime reads
            // into its NativeArray. Dropping it would leave only the GPU copy, useless to a job.
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static SdfGridTextureSO GetOrCreateOutput(SdfBakeAreaBehaviour areaBehaviour)
        {
            if (areaBehaviour.Output != null)
            {
                return areaBehaviour.Output;
            }

            var pathToDirectory = Path.GetDirectoryName(DefaultAssetPath);
            if (pathToDirectory == null)
            {
                throw new InvalidPathException("Path to directory cannot be null.");
            }

            Directory.CreateDirectory(pathToDirectory);
            var volume = ScriptableObject.CreateInstance<SdfGridTextureSO>();
            AssetDatabase.CreateAsset(volume, DefaultAssetPath);

            Undo.RecordObject(areaBehaviour, "Assign SDF output");
            areaBehaviour.SetOutput(volume);
            EditorUtility.SetDirty(areaBehaviour);
            EditorSceneManager.MarkSceneDirty(areaBehaviour.gameObject.scene);
            
            return volume;
        }

        /// <summary>
        /// Swaps the texture sub-asset inside the volume's asset file. The volume itself is the
        /// main asset and keeps its GUID, so scene references survive every re-bake.
        /// </summary>
        private static void ReplaceBakedTexture(SdfGridTextureSO gridTextureSo, Texture3D texture)
        {
            Texture3D previous = gridTextureSo.Texture;
            if (previous != null)
            {
                AssetDatabase.RemoveObjectFromAsset(previous);
                Object.DestroyImmediate(previous, allowDestroyingAssets: true);
            }

            AssetDatabase.AddObjectToAsset(texture, gridTextureSo);
        }

        /// <summary>
        /// Logs what was baked and checks the two signs that can be asserted about any scene:
        /// every shape's centre must read as inside, and the volume's corners must read as
        /// outside (they are past the fit margin). Reads the baked array, not the analytic
        /// functions, so it tests what will actually ship.
        /// </summary>
        private static void ReportBake(SdfGridTextureSO gridTextureSo, NativeArray<float> distances, SdfShapeData[] shapes, long jobMilliseconds, long totalMilliseconds)
        {
            SdfGridData gridData = gridTextureSo.GridData;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            
            foreach (var d in distances)
            {
                min = math.min(min, d);
                max = math.max(max, d);
            }

            long bytes = (long)distances.Length * sizeof(float);
            Debug.Log($"SDF baked: {gridData.Resolution.x}x{gridData.Resolution.y}x{gridData.Resolution.z} @ {gridData.CellSize} m, " +
                      $"{distances.Length:N0} samples ({bytes / (1024f * 1024f):F1} MB), " +
                      $"{shapes.Length} shape(s), job {jobMilliseconds} ms / total {totalMilliseconds} ms, range [{min:F3}, {max:F3}] m.", gridTextureSo);

            foreach (SdfShapeData shape in shapes)
            {
                float atCentre = NearestSample(gridData, distances, shape.Position);
                if (atCentre >= 0f)
                {
                    Debug.LogWarning($"SDF sign check: {shape.Kind} centre at {shape.Position} reads {atCentre:F3}, expected negative (inside).", gridTextureSo);
                }
            }

            for (int corner = 0; corner < 8; corner++)
            {
                bool3 useMax = new bool3((corner & 1) != 0, (corner & 2) != 0, (corner & 4) != 0);
                float3 cornerPoint = math.select(gridData.BoundsMin, gridData.BoundsMax, useMax);
               
                float atCorner = NearestSample(gridData, distances, cornerPoint);
                if (atCorner <= 0f)
                {
                    Debug.LogWarning($"SDF sign check: volume corner {cornerPoint} reads {atCorner:F3}, expected positive (outside). The bounds may be too tight.", gridTextureSo);
                }
            }
        }

        private static float NearestSample(SdfGridData gridData, NativeArray<float> distances, float3 worldPoint)
        {
            int3 sample = math.clamp((int3)math.round(gridData.WorldToGrid(worldPoint)), 0, gridData.Resolution - 1);
            return distances[gridData.Flatten(sample)];
        }
    }
}
