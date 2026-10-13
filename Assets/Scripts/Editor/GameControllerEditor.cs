using JewelryMiniFactory.Gameplay;
using UnityEditor;
using UnityEngine;

namespace JewelryMiniFactory.Editor
{
    [CustomEditor(typeof(GameController))]
    public sealed class GameControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (!GUILayout.Button("Restart Progress")) return;
            if (!EditorUtility.DisplayDialog("Restart Progress",
                "Reset JewelryMiniFactory progress, buildings and active Boost? This cannot be undone.",
                "Restart", "Cancel")) return;
            ((GameController)target).RestartProgress();
        }
    }
}
