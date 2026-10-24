using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BlackboxUpdater.Core
{
    public static class ApplyLauncher
    {
        #region Constants & Fields
        public const string ScriptName = "apply_update.ps1";
        public const string ResultName = "last_result.txt";
        private const string ScriptResource = "BlackboxUpdater.Core.apply_update.ps1";
        #endregion

        #region Public API
        public static string StageDir(string workRoot, Version v) => Path.Combine(workRoot, v.ToString());
        public static string BackupDir(string workRoot) => Path.Combine(workRoot, "backup");

        public static void EnsureOutside(string workRoot, string scanRoot)
        {
            var work = Path.GetFullPath(workRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var scan = Path.GetFullPath(scanRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (work.StartsWith(scan, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("work folder " + workRoot + " is inside the plugin folder " + scanRoot);
        }

        public static void WritePlan(string stageDir, Version version, IEnumerable<string> paths)
        {
            var ordered = paths.Where(p => p != "version.txt").OrderBy(p => p, StringComparer.Ordinal).ToList();
            if (paths.Contains("version.txt")) ordered.Add("version.txt");
            var text = version + "\n" + string.Join("\n", ordered) + "\n";
            File.WriteAllText(Path.Combine(stageDir, "plan.txt"), text, new UTF8Encoding(false));
        }

        public static string BuildArguments(string scriptPath, int gamePid, long gameStartTicks, string installDir, string stageDir, string backupDir)
        {
            return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(scriptPath)
                + " -GamePid " + gamePid + " -GameStartTicks " + gameStartTicks
                + " -InstallDir " + Quote(installDir) + " -StageDir " + Quote(stageDir) + " -BackupDir " + Quote(backupDir);
        }

        public static void Launch(string installDir, string workRoot, Version version, int gamePid, long gameStartTicks)
        {
            var stageDir = StageDir(workRoot, version);
            var script = Path.Combine(workRoot, ScriptName);
            using (var res = typeof(ApplyLauncher).Assembly.GetManifestResourceStream(ScriptResource))
            {
                if (res == null) throw new UpdateException("apply script resource is missing");
                using (var reader = new StreamReader(res, Encoding.UTF8))
                    File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
            }

            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var psi = new ProcessStartInfo(powershell, BuildArguments(script, gamePid, gameStartTicks, installDir, stageDir, BackupDir(workRoot)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi)?.Dispose();
        }

        public static string ReadResult(string workRoot)
        {
            var p = Path.Combine(workRoot, ResultName);
            return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
        }
        #endregion

        #region Internal Utils
        private static string Quote(string path) => "\"" + path.TrimEnd('\\', '/') + "\"";
        #endregion
    }
}
