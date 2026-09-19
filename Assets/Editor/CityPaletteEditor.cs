using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CityPalette))]
public sealed class CityPaletteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("One palette for the whole generated city. Changes update shared materials during Play. Do not change the stable swatch order.",MessageType.Info);
        var colors=serializedObject.FindProperty("Colors");
        for(int i=0;i<colors.arraySize;i++)EditorGUILayout.PropertyField(colors.GetArrayElementAtIndex(i),new GUIContent(((CityColor)i).ToString()));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Smoothness"));serializedObject.ApplyModifiedProperties();
    }
}
