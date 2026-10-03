using CRG.Core;
using CRG.Generation;
using CRG.Geometry;
using CRG.Runtime;
using DPF.Core.Building;
using DPF.Core.Numerics;
using DPF.Editor;
using DPF.Unity;
using NetcodeSample.Game;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace NetcodeSample.Editor
{
    /// <summary>
    /// Builds the sample's single level in one step: generates rooms with Connected Rooms Generator, bakes the
    /// Unity NavMesh, converts it to a deterministic DPF navmesh, and writes the base positions into
    /// <see cref="LevelDefinition"/>. Rebuilding replaces the previous level and its NavMesh assets.
    /// </summary>
    public static class LevelBuilder
    {
        private const string LevelFolder = "Assets/NetcodeSample/Level";
        private const string GenerationSettingsPath = LevelFolder + "/LevelGeneration.asset";
        private const string LevelDefinitionPath = LevelFolder + "/Level.asset";
        private const string NavMeshAssetPath = LevelFolder + "/DPF-NavMesh.asset";
        private const string FloorMaterialPath = LevelFolder + "/Floor.mat";
        private const string WallMaterialPath = LevelFolder + "/Wall.mat";
        private const string LevelRootName = "Generated Level";
        private const int DefaultSeed = 20261003;

        // How far a base may sit from the NavMesh before the build fails.
        private const float MaxBaseSnapDistance = 2f;

        [MenuItem("Netcode Sample/Build Level")]
        public static void BuildLevel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Build Level: exit Play Mode first.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("Build Level: save the scene first. The baked NavMesh is stored next to it.");
                return;
            }

            GenerationParametersAsset settings = LoadOrCreateGenerationSettings();
            GenerationParameters parameters = settings.parameters;
            if (!parameters.Validate(out string validationError))
            {
                Debug.LogError($"Build Level: invalid generation settings in {GenerationSettingsPath}: {validationError}", settings);
                return;
            }

            RemovePreviousLevel(scene);

            Material floor = LoadOrCreateMaterial(FloorMaterialPath, new Color(0.55f, 0.55f, 0.58f));
            Material wall = LoadOrCreateMaterial(WallMaterialPath, new Color(0.32f, 0.33f, 0.38f));
            GameObject level = LevelGeometryGenerator.GenerateComplete(parameters, floor, wall);
            if (level == null)
            {
                Debug.LogError("Build Level: Connected Rooms Generator produced no level. Try another seed.");
                return;
            }

            Undo.RegisterCreatedObjectUndo(level, "Build Level");
            CRGLevelData data = level.GetComponent<CRGLevelData>();

            if (!TryGetBasePosition(data.StartRoom, "red", out Vector3 redBase)
                || !TryGetBasePosition(data.EndRoom, "blue", out Vector3 blueBase)
                || !HasPath(redBase, blueBase))
            {
                return;
            }

            DPFNavMeshAsset navMesh = ConvertNavMesh(parameters.NavMeshAgentTypeID);
            if (navMesh == null)
            {
                return;
            }

            LevelDefinition definition = LoadOrCreate<LevelDefinition>(LevelDefinitionPath);
            definition.Set(navMesh, redBase.ToFP3(), blueBase.ToFP3(), data.Seed, data.Rooms.Count);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            DeleteUnusedBakes(scene, level.GetComponent<NavMeshSurface>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"Build Level: {data.Rooms.Count} rooms from seed {data.Seed}. " +
                $"Red base {redBase} (room {data.StartRoom.RoomID}), blue base {blueBase} (room {data.EndRoom.RoomID}, " +
                $"{data.EndRoom.DistanceFromStart} doors away). DPF navmesh checksum {navMesh.ChecksumText}.",
                definition);
        }

        private static GenerationParametersAsset LoadOrCreateGenerationSettings()
        {
            GenerationParametersAsset settings = AssetDatabase.LoadAssetAtPath<GenerationParametersAsset>(GenerationSettingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<GenerationParametersAsset>();
            settings.parameters = new GenerationParameters
            {
                GridType = GridType.Hexagon,
                CellSize = 10f,
                WallHeight = 3f,
                WallThickness = 0.5f,

                // 4 m doors: after the 0.5 m bake margin on each side, 3 m stay walkable, room for big cubes
                // (0.6 m radius) to pass each other.
                DoorWidthRatio = 0.4f,
                MinCellsPerRoom = 2,
                MaxCellsPerRoom = 4,
                TargetRoomCount = 7,

                // Long, sprawling layout so the start and end rooms (the two bases) are far apart.
                LayoutBias = 1f,
                LoopChance = 0.25f,
                AddMeshCollider = false,
                BakeNavMesh = true,
                CreateSpawnPoints = true,
                RandomSeed = DefaultSeed,
            };

            AssetDatabase.CreateAsset(settings, GenerationSettingsPath);
            return settings;
        }

        private static void RemovePreviousLevel(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == LevelRootName && root.GetComponent<CRGLevelData>() != null)
                {
                    Undo.DestroyObjectImmediate(root);
                }
            }
        }

        // CRG saves every bake as a new asset in <scene folder>/<scene name>/; keep only the current level's.
        private static void DeleteUnusedBakes(Scene scene, NavMeshSurface currentSurface)
        {
            string bakeFolder = $"{System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/')}/{scene.name}";
            if (!AssetDatabase.IsValidFolder(bakeFolder))
            {
                return;
            }

            NavMeshData current = currentSurface != null ? currentSurface.navMeshData : null;
            foreach (string guid in AssetDatabase.FindAssets("t:NavMeshData", new[] { bakeFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != current)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            // CRG names each new bake uniquely (" 1", " 2", ...); a fixed name keeps rebuilds a plain change in git.
            string currentPath = current != null ? AssetDatabase.GetAssetPath(current) : null;
            string stablePath = $"{bakeFolder}/CRG-NavMesh-{LevelRootName}.asset";
            if (!string.IsNullOrEmpty(currentPath) && currentPath != stablePath)
            {
                string error = AssetDatabase.MoveAsset(currentPath, stablePath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"Build Level: could not rename {currentPath} to {stablePath}: {error}");
                }
            }
        }

        private static bool TryGetBasePosition(CRGLevelData.RoomData room, string team, out Vector3 position)
        {
            position = default;
            if (room == null || room.SpawnPoint == null)
            {
                Debug.LogError($"Build Level: the generated level has no spawn point for the {team} base.");
                return false;
            }

            if (!NavMesh.SamplePosition(room.SpawnPoint.position, out NavMeshHit hit, MaxBaseSnapDistance, NavMesh.AllAreas))
            {
                Debug.LogError($"Build Level: the {team} base at {room.SpawnPoint.position} is not on the NavMesh.", room.SpawnPoint);
                return false;
            }

            position = hit.position;
            return true;
        }

        private static bool HasPath(Vector3 from, Vector3 to)
        {
            NavMeshPath path = new();
            if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                return true;
            }

            Debug.LogError($"Build Level: no complete NavMesh path between the bases ({path.status}). Check the door width.");
            return false;
        }

        private static DPFNavMeshAsset ConvertNavMesh(int agentTypeID)
        {
            DPFNavMeshConverterSettings converterSettings = new() { AgentTypeID = agentTypeID };
            byte[] blob = DPFNavMeshConverter.Convert(converterSettings, out NavMeshBuildReport report);
            if (blob == null)
            {
                Debug.LogError($"Build Level: DPF navmesh conversion failed.\n{report}");
                return null;
            }

            DPFNavMeshAsset navMesh = LoadOrCreate<DPFNavMeshAsset>(NavMeshAssetPath);
            DPFNavMeshConverter.Save(blob, report, agentTypeID, navMesh);

            if (!DPFLoadedNavMesh.TryLoad(navMesh, out DPFLoadedNavMesh loaded, out string loadError))
            {
                Debug.LogError($"Build Level: the converted DPF navmesh does not load: {loadError}", navMesh);
                return null;
            }

            loaded.Dispose();
            if (report.Warnings.Count > 0)
            {
                Debug.LogWarning($"Build Level: DPF navmesh converted with warnings.\n{report}", navMesh);
            }

            return navMesh;
        }

        private static Material LoadOrCreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static T LoadOrCreate<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
