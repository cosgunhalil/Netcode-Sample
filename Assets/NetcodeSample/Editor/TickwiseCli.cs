using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace NetcodeSample.Editor
{
    /// <summary>Runs the <c>tickwise</c> command line tool (from PATH, or cargo's bin folder).</summary>
    public static class TickwiseCli
    {
        /// <summary>Exit code 0 means identical, 1 different, 2 an error. Returns false when the tool isn't installed.</summary>
        public static bool TryRun(string arguments, out int exitCode, out string output)
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
                    output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    return true;
                }
                catch (Win32Exception)
                {
                    // Not found under this name; try the next.
                }
            }

            exitCode = 2;
            output = "The tickwise CLI isn't installed. Run tools/build-tickwise.ps1 -InstallCli (Unity closed).";
            return false;
        }

        /// <summary>Quotes a path for the command line.</summary>
        public static string Quote(string path) => $"\"{path}\"";
    }
}
