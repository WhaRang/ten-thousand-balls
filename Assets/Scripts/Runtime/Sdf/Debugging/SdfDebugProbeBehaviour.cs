using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Scripts.Runtime.Sdf.Utils;
using Scripts.Runtime.Sdf.Utils.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Debugging
{
    /// <summary>
    /// Debug tool: drag this object around the scene view and it draws what the sampler returns at
    /// its position. A sphere coloured by sign, a line from the point to where the field says the
    /// nearest surface is, and an arrow along the gradient. Works outside the volume too, which is
    /// how the boundary extrapolation is checked by eye.
    ///
    /// ExecuteAlways so that OnEnable/OnDisable run in edit mode and the field is loaded and
    /// disposed there, where the tool is actually used.
    /// </summary>
    [ExecuteAlways]
    public sealed class SdfDebugProbeBehaviour : MonoBehaviour
    {
        private const float GradientArrowLength = 0.3f;

        [SerializeField]
        private SdfGridTextureSO field;
        
        [SerializeField]
        [Min(0.001f)]
        private float markerRadius = 0.05f;

        private SdfBakedFieldData _bakedFieldData;
        private bool _isLoaded;

        private void OnEnable()
        {
            Load();
        }

        private void OnDisable()
        {
            Unload();
        }

        [ContextMenu("Reload Field")]
        private void Load()
        {
            Unload();
            if (field == null || !field.IsBaked)
            {
                return;
            }

            _bakedFieldData = SdfBakedFieldLoader.Load(field, Allocator.Persistent);
            _isLoaded = true;
        }

        private void Unload()
        {
            if (!_isLoaded)
            {
                return;
            }

            _bakedFieldData.Distances.Dispose();
            _isLoaded = false;
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                Load();
            }
        }

        #region Gizmos
        
        private void OnDrawGizmos()
        {
            float3 point = transform.position;

            if (!_isLoaded)
            {
                Gizmos.color = Color.grey;
                Gizmos.DrawWireSphere(point, markerRadius);
                return;
            }

            var sample = _bakedFieldData.Sample(point);

            Gizmos.color = sample.Distance >= 0f ? Color.green : Color.red;
            Gizmos.DrawSphere(point, markerRadius);

            var nearestSurface = point - sample.Gradient * sample.Distance;
            Gizmos.DrawLine(point, nearestSurface);
            Gizmos.DrawWireCube(nearestSurface, Vector3.one * markerRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(point, point + sample.Gradient * GradientArrowLength);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(point + new float3(0f, markerRadius * 2f, 0f),
                $"d = {sample.Distance:F3} m\nn = ({sample.Gradient.x:F2}, {sample.Gradient.y:F2}, {sample.Gradient.z:F2})");
#endif
        }

        #endregion
    }
}
