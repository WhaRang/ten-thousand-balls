using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Editor.Sdf.Debugging
{
    /// <summary>Running statistics over one set of points.</summary>
    internal sealed class PointsPopulation
    {
        //Gradient disagreements above this are counted separately: they mark medial creases.
        private const float LargeAngleDegrees = 10f;
        
        private readonly string _name;
        private int _pointsCount;
        private double _errorSum;
        private double _angleSum;
        private int _largeAngles;
        private float _maxError;
        private float3 _maxErrorPoint;
        private float _maxErrorAnalytic;
        private float _maxAngle;

        public int SignMismatches { get; private set; }

        public PointsPopulation(string name)
        {
            _name = name;
        }

        public void Add(float3 point, float sampled, float analytic, float angle)
        {
            _pointsCount++;

            float error = math.abs(sampled - analytic);
            _errorSum += error;
            
            if (error > _maxError)
            {
                _maxError = error;
                _maxErrorPoint = point;
                _maxErrorAnalytic = analytic;
            }

            if (!Mathf.Approximately(math.sign(sampled), math.sign(analytic)))
            {
                SignMismatches++;
            }

            _angleSum += angle;
            _maxAngle = math.max(_maxAngle, angle);
            if (angle > LargeAngleDegrees)
            {
                _largeAngles++;
            }
        }

        public string Report()
        {
            if (_pointsCount == 0)
            {
                return $"{_name}: no points.";
            }

            return $"{_name} ({_pointsCount:N0} points): |error| max {_maxError * 1000f:F2} mm at {_maxErrorPoint} " +
                   $"(analytic {_maxErrorAnalytic * 1000f:F1} mm), mean {_errorSum / _pointsCount * 1000.0:F3} mm; " +
                   $"sign mismatches {SignMismatches}; " +
                   $"gradient angle mean {_angleSum / _pointsCount:F2}°, max {_maxAngle:F1}°, " +
                   $">{LargeAngleDegrees}° at {100f * _largeAngles / _pointsCount:F2}%.";
        }
    }
}