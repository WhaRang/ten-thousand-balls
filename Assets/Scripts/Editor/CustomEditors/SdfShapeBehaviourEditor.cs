using Scripts.Runtime.Sdf.Mono;
using UnityEditor;

namespace Scripts.Editor.CustomEditors
{
    [CustomEditor(typeof(SdfShapeBehaviour))]
    [CanEditMultipleObjects]
    internal sealed class SdfShapeBehaviourEditor : UnityEditor.Editor
    {
        private SerializedProperty _primitiveProperty;
        private SerializedProperty _planeThicknessProperty;

        private void OnEnable()
        {
            _primitiveProperty = serializedObject.FindProperty("primitive");
            _planeThicknessProperty = serializedObject.FindProperty("planeThickness");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_primitiveProperty);

            bool showThickness = _primitiveProperty.hasMultipleDifferentValues
                                 || _primitiveProperty.enumValueIndex == (int)SdfShapeBehaviour.Primitive.Plane;
            if (showThickness)
            {
                EditorGUILayout.PropertyField(_planeThicknessProperty);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
