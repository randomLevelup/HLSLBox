using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Interx))]
public class InterxEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var interx = (Interx)target;
        EditorGUILayout.Space();
        if (GUILayout.Button("Reset Edges"))
        {
            interx.ResetEdges();
            // Mark scene dirty so changes persist in editor
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(interx);
            }
#endif
        }
    }
}
