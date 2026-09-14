using Scripts.Runtime.Mono;
using UnityEditor;

namespace Scripts.Editor.CustomEditors
{
    /// <summary>
    /// Shows the plane thickness only when the primitive is a Plane. For a cylinder the field
    /// has no meaning, and an inspector should not offer a value that does nothing.
    /// </summary>
    [CustomEditor(typeof(SdfShapeBehaviour))]
    [CanEditMultipleObjects]
    internal sealed class SdfShapeBehaviourEditor : UnityEditor.Editor
    {
        private SerializedProperty primitive;
        private SerializedProperty planeThickness;

        private void OnEnable()
        {
            primitive = serializedObject.FindProperty("primitive");
            planeThickness = serializedObject.FindProperty("planeThickness");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(primitive);

            // With several objects selected and mixed primitives, keep the field visible so a
            // shared value can still be edited rather than silently hiding it.
            bool showThickness = primitive.hasMultipleDifferentValues
                                 || primitive.enumValueIndex == (int)SdfShapeBehaviour.Primitive.Plane;
            if (showThickness)
            {
                EditorGUILayout.PropertyField(planeThickness);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
