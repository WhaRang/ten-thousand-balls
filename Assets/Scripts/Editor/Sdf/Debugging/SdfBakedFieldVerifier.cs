using System;
using System.Text;
using Scripts.Editor.Sdf.Utils;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.Mono;
using Scripts.Runtime.Sdf.Utils;
using Scripts.Runtime.Sdf.Utils.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Scripts.Editor.Sdf.Debugging
{
    /// <summary>
    /// Measures how far the baked, interpolated field is from the analytic truth it was baked
    /// from, at random points inside the volume and outside it. 
    ///
    /// Two populations are reported. "All" covers the whole volume, including deep inside the
    /// solids, where creases on the medial axes make trilinear interpolation worst and where no
    /// ball can ever be. "Contact band" covers only points just outside a surface, which is where
    /// every contact decision is made; those are the numbers that matter for the simulation.
    /// </summary>
    internal static class SdfBakedFieldVerifier
    {
        private const int InsidePointCount = 100_000;
        private const int OutsidePointCount = 10_000;

        // Analytic distances in [0, this] count as the contact band.
        private const float ContactBandMetres = 0.2f;

        // How far past the bounds outside points may lie, in metres.
        private const float OutsideReach = 3f;

        // Fixed seed so two runs on the same asset print the same numbers.
        private const uint Seed = 0x5DF5EED;

        // Step for the numerical gradient of the analytic field.
        private const float GradientStep = 1e-3f;

        // How many sign mismatches to print with their location.
        private const int ReportedMismatches = 5;

        public static void Verify(SdfBakeAreaBehaviour area)
        {
            var gridTextureSo = area.Output;
            if (gridTextureSo == null || !gridTextureSo.IsBaked)
            {
                throw new InvalidOperationException("Nothing to verify: bake first.");
            }

            var shapesData = SdfBaker.CollectShapeData();
            var fieldData = SdfBakedFieldLoader.Load(gridTextureSo, Allocator.TempJob);
            
            try
            {
                var random = new Random(Seed);
                var gridBoundsMin = fieldData.Grid.BoundsMin;
                var gridBoundsMax = fieldData.Grid.BoundsMax;

                var populationAll = new PointsPopulation("all");
                var populationBand = new PointsPopulation("contact band");
                var mismatchesSb = new StringBuilder();

                for (int i = 0; i < InsidePointCount; i++)
                {
                    var randomPoint = random.NextFloat3(gridBoundsMin, gridBoundsMax);
                    var randomPointSampledData = fieldData.Sample(randomPoint);
                    
                    float shapesPointAnalyticDistance = AnalyticDistance(shapesData, randomPoint);
                    float shapesPointAngleDegrees = AngleDegrees(randomPointSampledData.Gradient, AnalyticGradient(shapesData, randomPoint));

                    populationAll.Add(randomPoint, randomPointSampledData.Distance, shapesPointAnalyticDistance, shapesPointAngleDegrees);
                    if (shapesPointAnalyticDistance >= 0f && shapesPointAnalyticDistance <= ContactBandMetres)
                    {
                        populationBand.Add(randomPoint, randomPointSampledData.Distance, shapesPointAnalyticDistance, shapesPointAngleDegrees);
                    }

                    if (!Mathf.Approximately(math.sign(randomPointSampledData.Distance), math.sign(shapesPointAnalyticDistance)) 
                        && populationAll.SignMismatches <= ReportedMismatches)
                    {
                        mismatchesSb.Append($"\n  sign mismatch at {randomPoint}: analytic {shapesPointAnalyticDistance * 1000f:F1} mm, sampled {randomPointSampledData.Distance * 1000f:F1} mm");
                    }
                }

                var outsideStats = MeasureOutside(fieldData, shapesData, ref random, gridBoundsMin, gridBoundsMax);

                Debug.Log(
                    $"SDF verify @ {fieldData.Grid.CellSize} m cells, {InsidePointCount:N0} points inside the volume.\n" +
                    populationAll.Report() + "\n" +
                    populationBand.Report() + "\n" +
                    $"Outside ({OutsidePointCount:N0} points up to {OutsideReach} m past the bounds): " +
                    $"sampled − analytic max {outsideStats.MaxOvershoot * 1000f:+0.00;-0.00} mm " +
                    $"(must not exceed the inside error: the outside value is a lower bound), min {outsideStats.MinOvershoot:F3} m." +
                    mismatchesSb,
                    gridTextureSo);
            }
            finally
            {
                fieldData.Distances.Dispose();
            }
        }

        private static OutsideStats MeasureOutside(in SdfBakedFieldData fieldData, SdfShapeData[] shapesData, ref Random random, float3 minBound, float3 maxBound)
        {
            var outsideStats = new OutsideStats { MaxOvershoot = float.NegativeInfinity, MinOvershoot = float.PositiveInfinity };

            int countedPoints = 0;
            while (countedPoints < OutsidePointCount)
            {
                // Draw from the enlarged box and keep only points that are actually outside.
                var randomPoint = random.NextFloat3(minBound - OutsideReach, maxBound + OutsideReach);
                if (math.all(randomPoint >= minBound & randomPoint <= maxBound))
                {
                    continue;
                }

                float overshoot = fieldData.Sample(randomPoint).Distance - AnalyticDistance(shapesData, randomPoint);
                outsideStats.MaxOvershoot = math.max(outsideStats.MaxOvershoot, overshoot);
                outsideStats.MinOvershoot = math.min(outsideStats.MinOvershoot, overshoot);
                
                countedPoints++;
            }

            return outsideStats;
        }

        private static float AnalyticDistance(SdfShapeData[] shapesData, float3 point)
        {
            float distance = float.PositiveInfinity;
            foreach (var shapeData in shapesData)
            {
                distance = math.min(distance, shapeData.Distance(point));
            }
            
            return distance;
        }

        private static float3 AnalyticGradient(SdfShapeData[] shapesData, float3 point)
        {
            var dx = new float3(GradientStep, 0f, 0f);
            var dy = new float3(0f, GradientStep, 0f);
            var dz = new float3(0f, 0f, GradientStep);
            
            var gradient = new float3(
                AnalyticDistance(shapesData, point + dx) - AnalyticDistance(shapesData, point - dx),
                AnalyticDistance(shapesData, point + dy) - AnalyticDistance(shapesData, point - dy),
                AnalyticDistance(shapesData, point + dz) - AnalyticDistance(shapesData, point - dz)) / (2f * GradientStep);
            
            return math.normalizesafe(gradient, math.up());
        }

        private static float AngleDegrees(float3 a, float3 b)
        {
            return math.degrees(math.acos(math.clamp(math.dot(a, b), -1f, 1f)));
        }
    }
}
