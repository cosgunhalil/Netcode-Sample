using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using NetcodeSample.Determinism;
using NetcodeSample.Game;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NetcodeSample.Editor
{
    /// <summary>Tickwise workflows from the editor: self-check a recording, compare two, open the folder.</summary>
    public static class TickwiseMenu
    {
        private const string MenuRoot = "Netcode Sample/Tickwise/";
        private const string GameRulesPath = "Assets/NetcodeSample/Settings/GameRules.asset";
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";

        // Must match MatchRecorder's dump interval: diff needs a dump in both files at the same tick.
        private const long DumpInterval = 150;

        [MenuItem(MenuRoot + "Self-Check Recording...")]
        public static void SelfCheckRecording()
        {
            string path = EditorUtility.OpenFilePanel("Self-check a Tickwise recording", EnsureFolder(), "rec");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            GameRulesAsset rules = AssetDatabase.LoadAssetAtPath<GameRulesAsset>(GameRulesPath);
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            if (rules == null || level == null)
            {
                Debug.LogError($"Self-check needs {GameRulesPath} and {LevelDefinitionPath}, the ones the recording was made with.");
                return;
            }

            string replayPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".replay.rec");
            SelfCheckResult result;
            try
            {
                EditorUtility.DisplayProgressBar("Tickwise self-check", $"Replaying {Path.GetFileName(path)}...", 0.5f);
                result = SelfCheck.Run(path, rules.ToRules(), level.CreateLevelData(), new BurstNavigationWorldFactory(), replayPath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (result.IsDeterministic)
            {
                Debug.Log($"Tickwise self-check: all {result.TicksReplayed} ticks of {Path.GetFileName(path)} replayed identically. The simulation is deterministic for this session.");
            }
            else
            {
                Debug.LogError($"Tickwise self-check: {Path.GetFileName(path)} diverged at tick {result.FirstMismatchTick}: replaying its inputs gave a different state. The simulation is NOT deterministic.");
            }

            RunCli($"compare \"{path}\" \"{replayPath}\"");
            if (!result.IsDeterministic)
            {
                long dumpTick = ((result.FirstMismatchTick + DumpInterval - 1) / DumpInterval) * DumpInterval;
                RunCli($"diff \"{path}\" \"{replayPath}\" --at {dumpTick} --no-color");
            }
        }

        [MenuItem(MenuRoot + "Compare Two Recordings...")]
        public static void CompareRecordings()
        {
            string first = EditorUtility.OpenFilePanel("First recording", EnsureFolder(), "rec");
            if (string.IsNullOrEmpty(first))
            {
                return;
            }

            string second = EditorUtility.OpenFilePanel("Second recording", Path.GetDirectoryName(first), "rec");
            if (!string.IsNullOrEmpty(second))
            {
                RunCli($"compare \"{first}\" \"{second}\"");
            }
        }

        [MenuItem(MenuRoot + "Open Recordings Folder")]
        public static void OpenRecordingsFolder()
        {
            EditorUtility.RevealInFinder(EnsureFolder());
        }

        private static string EnsureFolder()
        {
            Directory.CreateDirectory(TickwiseSupport.RecordingsFolder);
            return TickwiseSupport.RecordingsFolder;
        }

        // Runs the tickwise CLI and logs its output; exit code 0 = identical, 1 = difference, 2 = error.
        private static void RunCli(string arguments)
        {
            string cargoCli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "tickwise.exe");
            foreach (string executable in new[] { "tickwise", cargoCli })
            {
                ProcessStartInfo start = new(executable, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                start.EnvironmentVariables["NO_COLOR"] = "1";

                try
                {
                    using Process process = Process.Start(start);
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    string message = $"tickwise {arguments}\n(exit code {process.ExitCode})\n{output}";
                    if (process.ExitCode == 0)
                    {
                        Debug.Log(message);
                    }
                    else
                    {
                        Debug.LogWarning(message);
                    }

                    return;
                }
                catch (Win32Exception)
                {
                    // Not found under this name; try the next.
                }
            }

            Debug.LogWarning($"The tickwise CLI isn't installed (run tools/build-tickwise.ps1 -InstallCli). Run it yourself:\ntickwise {arguments}");
        }
    }
}
