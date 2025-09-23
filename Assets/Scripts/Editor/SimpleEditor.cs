using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Simple))]
public class SimpleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var simple = (Simple)target;
        EditorGUILayout.Space();
        if (GUILayout.Button("Re-Convexify"))
        {
            simple.ReConvexify();
            // Mark scene dirty so changes persist in editor
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(simple);
            }
#endif
        }
    }
}
