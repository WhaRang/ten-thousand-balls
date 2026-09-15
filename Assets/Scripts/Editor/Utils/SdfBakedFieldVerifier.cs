using System;
using System.Text;
using Scripts.Runtime.Data;
using Scripts.Runtime.Mono;
using Scripts.Runtime.ScriptableObjects;
using Scripts.Runtime.Utils;
using Scripts.Runtime.Utils.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Scripts.Editor.Utils
{
    /// <summary>
    /// Measures how far the baked, interpolated field is from the analytic truth it was baked
    /// from, at random points inside the volume and outside it. Turns "the probe looks right"
    /// into numbers: interpolation error at the chosen cell size, sign agreement, gradient
    /// agreement, and whether the boundary extrapolation stays conservative.
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

        /// <summary>Analytic distances in [0, this] count as the contact band. A few ball radii.</summary>
        private const float ContactBandMetres = 0.2f;

        /// <summary>How far past the bounds outside points may lie, in metres.</summary>
        private const float OutsideReach = 3f;

        /// <summary>Fixed seed so two runs on the same asset print the same numbers.</summary>
        private const uint Seed = 0x5DF5EED;

        /// <summary>Step for the numerical gradient of the analytic field.</summary>
        private const float GradientStep = 1e-3f;

        /// <summary>Gradient disagreements above this are counted separately: they mark medial creases.</summary>
        private const float LargeAngleDegrees = 10f;

        /// <summary>How many sign mismatches to print with their location.</summary>
        private const int ReportedMismatches = 5;

        public static void Verify(SdfBakeAreaBehaviour area)
        {
            SdfGridTextureSO asset = area.Output;
            if (asset == null || !asset.IsBaked)
            {
                throw new InvalidOperationException("Nothing to verify: bake first.");
            }

            SdfShapeData[] shapes = SdfBaker.CollectShapeData();
            SdfBakedFieldData field = SdfBakedFieldLoader.Load(asset, Allocator.TempJob);
            try
            {
                var random = new Random(Seed);
                float3 min = field.Grid.BoundsMin;
                float3 max = field.Grid.BoundsMax;

                var all = new Population("all");
                var band = new Population("contact band");
                var mismatches = new StringBuilder();

                for (int i = 0; i < InsidePointCount; i++)
                {
                    float3 point = random.NextFloat3(min, max);
                    SdfWorldPointSampleData sampled = field.Sample(point);
                    float analytic = AnalyticDistance(shapes, point);
                    float angle = AngleDegrees(sampled.Gradient, AnalyticGradient(shapes, point));

                    all.Add(point, sampled.Distance, analytic, angle);
                    if (analytic >= 0f && analytic <= ContactBandMetres)
                    {
                        band.Add(point, sampled.Distance, analytic, angle);
                    }

                    if (math.sign(sampled.Distance) != math.sign(analytic) && all.SignMismatches <= ReportedMismatches)
                    {
                        mismatches.Append($"\n  sign mismatch at {point}: analytic {analytic * 1000f:F1} mm, sampled {sampled.Distance * 1000f:F1} mm");
                    }
                }

                OutsideStats outside = MeasureOutside(field, shapes, ref random, min, max);

                Debug.Log(
                    $"SDF verify @ {field.Grid.CellSize} m cells, {InsidePointCount:N0} points inside the volume.\n" +
                    all.Report() + "\n" +
                    band.Report() + "\n" +
                    $"Outside ({OutsidePointCount:N0} points up to {OutsideReach} m past the bounds): " +
                    $"sampled − analytic max {outside.MaxOvershoot * 1000f:+0.00;-0.00} mm " +
                    $"(must not exceed the inside error: the outside value is a lower bound), min {outside.MinOvershoot:F3} m." +
                    mismatches,
                    asset);
            }
            finally
            {
                field.Distances.Dispose();
            }
        }

        /// <summary>Running statistics over one set of points.</summary>
        private sealed class Population
        {
            private readonly string name;
            private int count;
            private double errorSum;
            private double angleSum;
            private int largeAngles;
            private float maxError;
            private float3 maxErrorPoint;
            private float maxErrorAnalytic;
            private float maxAngle;

            public int SignMismatches { get; private set; }

            public Population(string name)
            {
                this.name = name;
            }

            public void Add(float3 point, float sampled, float analytic, float angle)
            {
                count++;

                float error = math.abs(sampled - analytic);
                errorSum += error;
                if (error > maxError)
                {
                    maxError = error;
                    maxErrorPoint = point;
                    maxErrorAnalytic = analytic;
                }

                if (math.sign(sampled) != math.sign(analytic))
                {
                    SignMismatches++;
                }

                angleSum += angle;
                maxAngle = math.max(maxAngle, angle);
                if (angle > LargeAngleDegrees)
                {
                    largeAngles++;
                }
            }

            public string Report()
            {
                if (count == 0)
                {
                    return $"{name}: no points.";
                }

                return $"{name} ({count:N0} points): |error| max {maxError * 1000f:F2} mm at {maxErrorPoint} " +
                       $"(analytic {maxErrorAnalytic * 1000f:F1} mm), mean {errorSum / count * 1000.0:F3} mm; " +
                       $"sign mismatches {SignMismatches}; " +
                       $"gradient angle mean {angleSum / count:F2}°, max {maxAngle:F1}°, " +
                       $">{LargeAngleDegrees}° at {100f * largeAngles / count:F2}%.";
            }
        }

        private struct OutsideStats
        {
            /// <summary>Largest (sampled − analytic). Positive means the field claimed more room than exists.</summary>
            public float MaxOvershoot;
            public float MinOvershoot;
        }

        private static OutsideStats MeasureOutside(in SdfBakedFieldData field, SdfShapeData[] shapes, ref Random random, float3 min, float3 max)
        {
            var stats = new OutsideStats { MaxOvershoot = float.NegativeInfinity, MinOvershoot = float.PositiveInfinity };

            int counted = 0;
            while (counted < OutsidePointCount)
            {
                // Draw from the enlarged box and keep only points that are actually outside.
                float3 point = random.NextFloat3(min - OutsideReach, max + OutsideReach);
                if (math.all(point >= min & point <= max))
                {
                    continue;
                }

                float overshoot = field.Sample(point).Distance - AnalyticDistance(shapes, point);
                stats.MaxOvershoot = math.max(stats.MaxOvershoot, overshoot);
                stats.MinOvershoot = math.min(stats.MinOvershoot, overshoot);
                counted++;
            }

            return stats;
        }

        /// <summary>The same union the baker evaluates, at an arbitrary point: the ground truth.</summary>
        private static float AnalyticDistance(SdfShapeData[] shapes, float3 point)
        {
            float distance = float.PositiveInfinity;
            foreach (SdfShapeData shape in shapes)
            {
                distance = math.min(distance, shape.Distance(point));
            }
            return distance;
        }

        /// <summary>Central differences on the analytic union. Exact enough away from creases.</summary>
        private static float3 AnalyticGradient(SdfShapeData[] shapes, float3 point)
        {
            float3 dx = new float3(GradientStep, 0f, 0f);
            float3 dy = new float3(0f, GradientStep, 0f);
            float3 dz = new float3(0f, 0f, GradientStep);
            float3 gradient = new float3(
                AnalyticDistance(shapes, point + dx) - AnalyticDistance(shapes, point - dx),
                AnalyticDistance(shapes, point + dy) - AnalyticDistance(shapes, point - dy),
                AnalyticDistance(shapes, point + dz) - AnalyticDistance(shapes, point - dz)) / (2f * GradientStep);
            return math.normalizesafe(gradient, math.up());
        }

        private static float AngleDegrees(float3 a, float3 b)
        {
            return math.degrees(math.acos(math.clamp(math.dot(a, b), -1f, 1f)));
        }
    }
}
