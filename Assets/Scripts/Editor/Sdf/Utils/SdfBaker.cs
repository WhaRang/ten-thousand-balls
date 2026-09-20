using System;
using System.Diagnostics;
using System.IO;
using Scripts.Editor.Sdf.Jobs;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.Mono;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Scripts.Runtime.Sdf.Utils.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Properties;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Scripts.Editor.Sdf.Utils
{
    /// <summary>
    /// The editor-time bake step: scene shapes in, committed <see cref="SdfGridTextureSO"/> asset out.
    /// </summary>
    internal static class SdfBaker
    {
        private const string DefaultAssetPath = "Assets/ScriptableObjects/Baked/SceneSdf.asset";
        private const string DefaultSdfTextureName = "SdfTexture";

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
            var shapesData = CollectShapeData();
            if (shapesData.Length == 0)
            {
                throw new InvalidOperationException("No SdfShape components in the open scenes; nothing to bake.");
            }

            var gridData = areaBehaviour.GridData;
            ValidateGridSize(gridData);

            var stopwatch = Stopwatch.StartNew();
            var distances = GenerateGridDistances(gridData, shapesData);
            long jobMilliseconds = stopwatch.ElapsedMilliseconds;

            try
            {
                var texture = CreateTexture(gridData, distances);
                var gridTextureSo = GetOrCreateOutput(areaBehaviour);
                ReplaceBakedTexture(gridTextureSo, texture);
                
                gridTextureSo.AssignBake(gridData, texture);
                EditorUtility.SetDirty(gridTextureSo);
                AssetDatabase.SaveAssets();

                ReportBake(gridTextureSo, distances, shapesData, jobMilliseconds, stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                distances.Dispose();
            }
        }

        internal static SdfShapeData[] CollectShapeData()
        {
            var sdfShapeComponents = Object.FindObjectsByType<SdfShapeBehaviour>(FindObjectsSortMode.None);
            var shapeData = new SdfShapeData[sdfShapeComponents.Length];
            for (int i = 0; i < sdfShapeComponents.Length; i++)
            {
                shapeData[i] = sdfShapeComponents[i].ToData();
            }
            return shapeData;
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

        private static NativeArray<float> GenerateGridDistances(SdfGridData gridData, SdfShapeData[] shapesData)
        {
            var shapesDataNative = new NativeArray<SdfShapeData>(shapesData, Allocator.TempJob);
            var distancesNative = new NativeArray<float>(gridData.SampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                new SdfShapesBakeJob
                {
                    GridData = gridData,
                    Shapes = shapesDataNative,
                    Distances = distancesNative,
                }.Schedule(gridData.SampleCount, JobBatchSize).Complete();
            }
            finally
            {
                shapesDataNative.Dispose();
            }

            return distancesNative;
        }

        private static Texture3D CreateTexture(SdfGridData gridData, NativeArray<float> distances)
        {
            var gridResolution = gridData.Resolution;

            var newTexture = new Texture3D(gridResolution.x, gridResolution.y, gridResolution.z, TextureFormat.RFloat, mipChain: false)
            {
                name = DefaultSdfTextureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // Same memory order as SdfGrid.Flatten, so the array goes in as-is.
            newTexture.SetPixelData(distances, mipLevel: 0);
            newTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            
            return newTexture;
        }

        private static SdfGridTextureSO GetOrCreateOutput(SdfBakeAreaBehaviour areaBehaviour)
        {
            if (areaBehaviour.Output != null)
            {
                return areaBehaviour.Output;
            }

            string pathToDirectory = Path.GetDirectoryName(DefaultAssetPath);
            if (pathToDirectory == null)
            {
                throw new InvalidPathException("Path to directory cannot be null.");
            }

            Directory.CreateDirectory(pathToDirectory);
            
            var sdfGridTexture = ScriptableObject.CreateInstance<SdfGridTextureSO>();
            AssetDatabase.CreateAsset(sdfGridTexture, DefaultAssetPath);

            Undo.RecordObject(areaBehaviour, "Assign SDF output");
            areaBehaviour.SetOutput(sdfGridTexture);
            
            EditorUtility.SetDirty(areaBehaviour);
            EditorSceneManager.MarkSceneDirty(areaBehaviour.gameObject.scene);
            
            return sdfGridTexture;
        }

        /// <summary>
        /// Swaps the texture sub-asset inside the volume's asset file. The volume itself is the
        /// main asset and keeps its GUID, so scene references survive every re-bake.
        /// </summary>
        private static void ReplaceBakedTexture(SdfGridTextureSO gridTextureSo, Texture3D texture)
        {
            var previous = gridTextureSo.Texture;
            if (previous != null)
            {
                AssetDatabase.RemoveObjectFromAsset(previous);
                Object.DestroyImmediate(previous, allowDestroyingAssets: true);
            }

            AssetDatabase.AddObjectToAsset(texture, gridTextureSo);
        }

        private static void ReportBake(SdfGridTextureSO gridTextureSo, NativeArray<float> distances, SdfShapeData[] shapesData, long jobMilliseconds, long totalMilliseconds)
        {
            var gridData = gridTextureSo.GridData;
            float minDistance = float.PositiveInfinity;
            float maxDistance = float.NegativeInfinity;
            
            foreach (float d in distances)
            {
                minDistance = math.min(minDistance, d);
                maxDistance = math.max(maxDistance, d);
            }

            long bytes = (long)distances.Length * sizeof(float);
            Debug.Log($"SDF baked: {gridData.Resolution.x}x{gridData.Resolution.y}x{gridData.Resolution.z} @ {gridData.CellSize} m, " +
                      $"{distances.Length:N0} samples ({bytes / (1024f * 1024f):F1} MB), " +
                      $"{shapesData.Length} shape(s), job {jobMilliseconds} ms / total {totalMilliseconds} ms, range [{minDistance:F3}, {maxDistance:F3}] m.", gridTextureSo);

            foreach (var shapeData in shapesData)
            {
                float atCentre = NearestSample(gridData, distances, shapeData.Position);
                if (atCentre >= 0f)
                {
                    Debug.LogWarning($"SDF sign check: {shapeData.Kind} centre at {shapeData.Position} reads {atCentre:F3}, expected negative (inside).", gridTextureSo);
                }
            }

            for (int corner = 0; corner < 8; corner++)
            {
                var useMax = new bool3((corner & 1) != 0, (corner & 2) != 0, (corner & 4) != 0);
                var cornerPoint = math.select(gridData.BoundsMin, gridData.BoundsMax, useMax);
               
                float atCorner = NearestSample(gridData, distances, cornerPoint);
                if (atCorner <= 0f)
                {
                    Debug.LogWarning($"SDF sign check: volume corner {cornerPoint} reads {atCorner:F3}, expected positive (outside). The bounds may be too tight.", gridTextureSo);
                }
            }
        }

        private static float NearestSample(SdfGridData gridData, NativeArray<float> distances, float3 worldPoint)
        {
            var sample = math.clamp((int3)math.round(gridData.WorldToGrid(worldPoint)), 0, gridData.Resolution - 1);
            return distances[gridData.Flatten(sample)];
        }
    }
}
