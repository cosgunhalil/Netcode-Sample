using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting.Tugboat;
using NetcodeSample.Game;
using NetcodeSample.Game.Local;
using NetcodeSample.Game.Network;
using NetcodeSample.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetcodeSample.Editor
{
    /// <summary>
    /// Adds a FishNet Network Manager (Tugboat transport) and a configured <see cref="NetworkMatchRunner"/> to the open
    /// scene, and deactivates the local-match runner: the scene runs one mode at a time.
    /// </summary>
    public static class NetworkMatchSetup
    {
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";
        private const string GameRulesPath = "Assets/NetcodeSample/Settings/GameRules.asset";
        private const string DefaultPrefabObjectsPath = "Assets/DefaultPrefabObjects.asset";
        private const string NetworkManagerName = "Network Manager";
        private const string RunnerName = "Network Match";

        [MenuItem("Netcode Sample/Set Up Network Match Scene")]
        public static void SetUp()
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            GameRulesAsset rules = AssetDatabase.LoadAssetAtPath<GameRulesAsset>(GameRulesPath);
            if (level == null || rules == null)
            {
                Debug.LogError($"Set Up Network Match Scene needs {LevelDefinitionPath} and {GameRulesPath}. Run Build Level and Set Up Local Match Scene first.");
                return;
            }

            NetworkManager networkManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (networkManager == null)
            {
                GameObject managerObject = new(NetworkManagerName);
                Undo.RegisterCreatedObjectUndo(managerObject, "Set Up Network Match Scene");
                managerObject.AddComponent<Tugboat>();
                networkManager = managerObject.AddComponent<NetworkManager>();
            }

            networkManager.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
            EditorUtility.SetDirty(networkManager);

            NetworkMatchRunner runner = Object.FindAnyObjectByType<NetworkMatchRunner>(FindObjectsInactive.Include);
            if (runner == null)
            {
                GameObject runnerObject = new(RunnerName);
                Undo.RegisterCreatedObjectUndo(runnerObject, "Set Up Network Match Scene");
                runner = runnerObject.AddComponent<NetworkMatchRunner>();
            }

            SerializedObject serialized = new(runner);
            serialized.FindProperty("_networkManager").objectReferenceValue = networkManager;
            serialized.FindProperty("_level").objectReferenceValue = level;
            serialized.FindProperty("_rules").objectReferenceValue = rules;
            serialized.FindProperty("_presentation").objectReferenceValue = PresentationAssets.LoadOrCreate();
            MatchUI ui = MatchUIBuilder.FindOrBuild();
            serialized.FindProperty("_ui").objectReferenceValue = ui;
            serialized.ApplyModifiedProperties();

            SetActive(networkManager.gameObject, true);
            SetActive(runner.gameObject, true);
            SetActive(ui.gameObject, true);
            LocalMatchRunner localRunner = Object.FindAnyObjectByType<LocalMatchRunner>(FindObjectsInactive.Include);
            if (localRunner != null)
            {
                SetActive(localRunner.gameObject, false);
            }

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = runner;
            Debug.Log(
                "Set Up Network Match Scene: press Play and Host or Join. With ParrelSync (ParrelSync > Clones Manager), " +
                "the original editor hosts and the clone joins automatically.",
                runner);
        }

        /// <summary>Activates or deactivates a scene object with undo.</summary>
        internal static void SetActive(GameObject gameObject, bool active)
        {
            if (gameObject.activeSelf != active)
            {
                Undo.RecordObject(gameObject, "Switch match mode");
                gameObject.SetActive(active);
            }
        }
    }
}
