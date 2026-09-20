using Scripts.Editor.Sdf.Debugging;
using Scripts.Editor.Sdf.Utils;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.Mono;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Scripts.Editor.CustomEditors
{
    [CustomEditor(typeof(SdfBakeAreaBehaviour))]
    internal sealed class SdfBakeAreaBehaviourEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var sdfAreaBehaviour = (SdfBakeAreaBehaviour)target;
            var grid = sdfAreaBehaviour.GridData;

            EditorGUILayout.Space();
            
            DrawGridReadout(grid);
            DrawBakeState(sdfAreaBehaviour.Output, grid);
            DrawButtons(sdfAreaBehaviour);
        }

        private static void DrawGridReadout(SdfGridData grid)
        {
            float megabytes = grid.SampleCount * sizeof(float) / (1024f * 1024f);
            EditorGUILayout.LabelField("Grid",
                $"{grid.Resolution.x} x {grid.Resolution.y} x {grid.Resolution.z} = {grid.SampleCount:N0} samples, {megabytes:F1} MB");
        }

        private static void DrawBakeState(SdfGridTextureSO output, SdfGridData current)
        {
            if (output == null || !output.IsBaked)
            {
                EditorGUILayout.HelpBox("Not baked yet. Press Bake to create the field asset.", MessageType.Info);
            }
            else if (!SameGrid(output.GridData, current))
            {
                EditorGUILayout.HelpBox("Bounds or cell size changed since the last bake. Press Bake again.", MessageType.Warning);
            }
        }

        private static void DrawButtons(SdfBakeAreaBehaviour sdfAreaBehaviour)
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fit To Shapes"))
                {
                    Undo.RecordObject(sdfAreaBehaviour, "Fit SDF bake area");
                    sdfAreaBehaviour.FitToShapes(SdfBaker.CollectShapeData());
                    EditorUtility.SetDirty(sdfAreaBehaviour);
                }

                if (GUILayout.Button("Bake"))
                {
                    SdfBaker.Bake(sdfAreaBehaviour);
                }

                bool canVerify = sdfAreaBehaviour.Output != null && sdfAreaBehaviour.Output.IsBaked;
                using (new EditorGUI.DisabledScope(!canVerify))
                {
                    if (GUILayout.Button("Verify Field"))
                    {
                        SdfBakedFieldVerifier.Verify(sdfAreaBehaviour);
                    }
                }
            }
        }

        private static bool SameGrid(SdfGridData first, SdfGridData second)
        {
            return math.all(first.BoundsMin == second.BoundsMin)
                && Mathf.Approximately(first.CellSize, second.CellSize)
                && math.all(first.Resolution == second.Resolution);
        }
    }
}
