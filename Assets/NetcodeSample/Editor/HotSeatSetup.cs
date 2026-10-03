using NetcodeSample.Game;
using NetcodeSample.Game.HotSeat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetcodeSample.Editor
{
    /// <summary>Adds a configured <see cref="HotSeatRunner"/> to the open scene, creating the game rules asset if needed.</summary>
    public static class HotSeatSetup
    {
        private const string SettingsFolder = "Assets/NetcodeSample/Settings";
        private const string GameRulesPath = SettingsFolder + "/GameRules.asset";
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";
        private const string RunnerName = "Hot-Seat";

        [MenuItem("Netcode Sample/Set Up Hot-Seat Scene")]
        public static void SetUp()
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            if (level == null)
            {
                Debug.LogError($"Set Up Hot-Seat Scene: {LevelDefinitionPath} not found. Run Netcode Sample > Build Level first.");
                return;
            }

            GameRulesAsset rules = LoadOrCreateRules();
            Scene scene = SceneManager.GetActiveScene();

            HotSeatRunner runner = Object.FindAnyObjectByType<HotSeatRunner>();
            if (runner == null)
            {
                GameObject gameObject = new(RunnerName);
                Undo.RegisterCreatedObjectUndo(gameObject, "Set Up Hot-Seat Scene");
                runner = gameObject.AddComponent<HotSeatRunner>();
            }

            SerializedObject serialized = new(runner);
            serialized.FindProperty("_level").objectReferenceValue = level;
            serialized.FindProperty("_rules").objectReferenceValue = rules;
            serialized.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = runner;
            Debug.Log("Set Up Hot-Seat Scene: press Play. Space spawns a red big cube, Enter a blue one.", runner);
        }

        private static GameRulesAsset LoadOrCreateRules()
        {
            GameRulesAsset rules = AssetDatabase.LoadAssetAtPath<GameRulesAsset>(GameRulesPath);
            if (rules != null)
            {
                return rules;
            }

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets/NetcodeSample", "Settings");
            }

            rules = ScriptableObject.CreateInstance<GameRulesAsset>();
            AssetDatabase.CreateAsset(rules, GameRulesPath);
            AssetDatabase.SaveAssets();
            return rules;
        }
    }
}
