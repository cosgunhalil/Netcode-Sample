using System;
using System.IO;
using NetcodeSample.Determinism;
using NetcodeSample.Simulation;
using Tickwise;
using UnityEngine;

namespace NetcodeSample.Game
{
    /// <summary>Where recordings go, and starting a recorder safely when the native library may be missing.</summary>
    public static class TickwiseSupport
    {
        /// <summary>
        /// <c>persistentDataPath/tickwise</c>. A ParrelSync clone shares it with the original editor, so recording
        /// names must tell peers apart.
        /// </summary>
        public static string RecordingsFolder => Path.Combine(Application.persistentDataPath, "tickwise");

        /// <summary>A new recording path: <c>&lt;prefix&gt;-&lt;yyyyMMdd-HHmmss&gt;.rec</c>.</summary>
        public static string CreateRecordingPath(string prefix)
        {
            return Path.Combine(RecordingsFolder, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.rec");
        }

        /// <summary>
        /// Starts recording, or returns null with an explanation when the Tickwise native library isn't usable
        /// (it's built locally; see the README).
        /// </summary>
        public static MatchRecorder TryStartRecording(string path, GameSimulation simulation, UnityEngine.Object context)
        {
            try
            {
                TickwiseNative.EnsureCompatible();
                return MatchRecorder.Create(path, simulation, Application.platform.ToString());
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is TickwiseException)
            {
                Debug.LogError(
                    $"Tickwise recording is off: {exception.Message}\nBuild the native library with tools/build-tickwise.ps1 (Unity closed); see the README.",
                    context);
                return null;
            }
        }
    }
}
