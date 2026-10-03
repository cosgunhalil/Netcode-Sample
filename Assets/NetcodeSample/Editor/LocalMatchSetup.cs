using NetcodeSample.Game;
using FishNet.Managing;
using NetcodeSample.Game.Local;
using NetcodeSample.Game.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetcodeSample.Editor
{
    /// <summary>Adds a configured <see cref="LocalMatchRunner"/> to the open scene, creating the game rules asset if needed.</summary>
    public static class LocalMatchSetup
    {
        private const string SettingsFolder = "Assets/NetcodeSample/Settings";
        private const string GameRulesPath = SettingsFolder + "/GameRules.asset";
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";
        private const string RunnerName = "Local Match";

        [MenuItem("Netcode Sample/Set Up Local Match Scene")]
        public static void SetUp()
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            if (level == null)
            {
                Debug.LogError($"Set Up Local Match Scene: {LevelDefinitionPath} not found. Run Netcode Sample > Build Level first.");
                return;
            }

            GameRulesAsset rules = LoadOrCreateRules();
            Scene scene = SceneManager.GetActiveScene();

            LocalMatchRunner runner = Object.FindAnyObjectByType<LocalMatchRunner>(FindObjectsInactive.Include);
            if (runner == null)
            {
                GameObject gameObject = new(RunnerName);
                Undo.RegisterCreatedObjectUndo(gameObject, "Set Up Local Match Scene");
                runner = gameObject.AddComponent<LocalMatchRunner>();
            }

            if (runner.gameObject.name != RunnerName)
            {
                Undo.RecordObject(runner.gameObject, "Set Up Local Match Scene");
                runner.gameObject.name = RunnerName;
            }

            SerializedObject serialized = new(runner);
            serialized.FindProperty("_level").objectReferenceValue = level;
            serialized.FindProperty("_rules").objectReferenceValue = rules;
            serialized.FindProperty("_presentation").objectReferenceValue = PresentationAssets.LoadOrCreate();
            serialized.ApplyModifiedProperties();

            // The scene runs one mode at a time.
            NetworkMatchSetup.SetActive(runner.gameObject, true);
            NetworkMatchRunner networkRunner = Object.FindAnyObjectByType<NetworkMatchRunner>(FindObjectsInactive.Include);
            if (networkRunner != null)
            {
                NetworkMatchSetup.SetActive(networkRunner.gameObject, false);
            }

            NetworkManager networkManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (networkManager != null)
            {
                NetworkMatchSetup.SetActive(networkManager.gameObject, false);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = runner;
            Debug.Log("Set Up Local Match Scene: pick a Mode on the Local Match object (HotSeat, SyncTest, Loopback) and press Play. Space spawns a red big cube, Enter a blue one.", runner);
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
