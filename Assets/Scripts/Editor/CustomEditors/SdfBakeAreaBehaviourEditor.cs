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
    /// <summary>
    /// Inspector for the bake area: the default fields, a readout of the grid those fields
    /// describe, and the three buttons that make up the whole bake workflow: fit, bake, verify.
    /// </summary>
    [CustomEditor(typeof(SdfBakeAreaBehaviour))]
    internal sealed class SdfBakeAreaBehaviourEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var area = (SdfBakeAreaBehaviour)target;
            SdfGridData grid = area.GridData;

            EditorGUILayout.Space();
            DrawGridReadout(grid);
            DrawBakeState(area.Output, grid);
            DrawButtons(area);
        }

        /// <summary>Shows what the current settings will produce before anything is baked.</summary>
        private static void DrawGridReadout(SdfGridData grid)
        {
            float megabytes = grid.SampleCount * sizeof(float) / (1024f * 1024f);
            EditorGUILayout.LabelField("Grid",
                $"{grid.Resolution.x} x {grid.Resolution.y} x {grid.Resolution.z} = {grid.SampleCount:N0} samples, {megabytes:F1} MB");
        }

        /// <summary>
        /// The one staleness check that costs nothing: the asset remembers the grid it was baked
        /// with, so a changed bounds or cell size is visible without tracking anything else.
        /// Moving a shape is not detected; the README says to re-bake after any scene change.
        /// </summary>
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

        private static void DrawButtons(SdfBakeAreaBehaviour area)
        {
            // Baking rewrites a project asset; doing that while the game runs is never intended.
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fit To Shapes"))
                {
                    Undo.RecordObject(area, "Fit SDF bake area");
                    area.FitToShapes(SdfBaker.CollectShapeData());
                    EditorUtility.SetDirty(area);
                }

                if (GUILayout.Button("Bake"))
                {
                    SdfBaker.Bake(area);
                }

                // Reads the asset and the scene, writes nothing: safe whenever there is a bake.
                bool canVerify = area.Output != null && area.Output.IsBaked;
                using (new EditorGUI.DisabledScope(!canVerify))
                {
                    if (GUILayout.Button("Verify Field"))
                    {
                        SdfBakedFieldVerifier.Verify(area);
                    }
                }
            }
        }

        private static bool SameGrid(SdfGridData a, SdfGridData b)
        {
            return math.all(a.BoundsMin == b.BoundsMin)
                && a.CellSize == b.CellSize
                && math.all(a.Resolution == b.Resolution);
        }
    }
}
