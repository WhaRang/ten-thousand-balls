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
    public sealed class SdfProbeBehaviour : MonoBehaviour
    {
        private const float GradientArrowLength = 0.3f;

        [SerializeField]
        private SdfGridTextureSO field;

        
        [SerializeField]
        [Min(0.001f)]
        private float markerRadius = 0.05f;

        private SdfBakedFieldData loaded;
        private bool isLoaded;

        private void OnEnable()
        {
            Load();
        }

        private void OnDisable()
        {
            Unload();
        }

        /// <summary>
        /// The loaded copy is a snapshot. After a re-bake, or after changing the asset in the
        /// inspector, reload it.
        /// </summary>
        [ContextMenu("Reload Field")]
        private void Load()
        {
            Unload();
            if (field == null || !field.IsBaked)
            {
                return;
            }

            loaded = SdfBakedFieldLoader.Load(field, Allocator.Persistent);
            isLoaded = true;
        }

        private void Unload()
        {
            if (!isLoaded)
            {
                return;
            }

            loaded.Distances.Dispose();
            isLoaded = false;
        }

        private void OnValidate()
        {
            // Field assigned or swapped in the inspector. OnValidate can also run before OnEnable
            // during scene load; Load() unloads first, so the order does not matter.
            if (isActiveAndEnabled)
            {
                Load();
            }
        }

        private void OnDrawGizmos()
        {
            float3 point = transform.position;

            if (!isLoaded)
            {
                Gizmos.color = Color.grey;
                Gizmos.DrawWireSphere(point, markerRadius);
                return;
            }

            SdfWorldPointSampleData sample = loaded.Sample(point);

            // Green outside, red inside: the sign is the first thing the brief says it checks.
            Gizmos.color = sample.Distance >= 0f ? Color.green : Color.red;
            Gizmos.DrawSphere(point, markerRadius);

            // Distance shrinks fastest against the gradient, so the nearest surface point is
            // Distance metres that way. Inside a solid Distance is negative and the same formula
            // walks outward to the surface. The cube should sit exactly on the geometry.
            float3 nearestSurface = point - sample.Gradient * sample.Distance;
            Gizmos.DrawLine(point, nearestSurface);
            Gizmos.DrawWireCube(nearestSurface, Vector3.one * markerRadius);

            // Gradient direction at a fixed length, so it stays visible when the distance is tiny.
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(point, point + sample.Gradient * GradientArrowLength);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(point + new float3(0f, markerRadius * 2f, 0f),
                $"d = {sample.Distance:F3} m\nn = ({sample.Gradient.x:F2}, {sample.Gradient.y:F2}, {sample.Gradient.z:F2})");
#endif
        }
    }
}
