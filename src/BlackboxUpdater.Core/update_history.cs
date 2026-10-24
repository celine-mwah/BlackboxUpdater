using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BlackboxUpdater.Core
{
    public static class UpdateHistory
    {
        #region Constants & Fields
        public const string FileName = "history.log";
        private const int MaxBytes = 64 * 1024;
        private const int MaxLineChars = 500;
        #endregion

        #region Public API
        public static void Append(string workRoot, string message)
        {
            try
            {
                Directory.CreateDirectory(workRoot);
                var path = Path.Combine(workRoot, FileName);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) Trim(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + Sanitize(message) + "\n", new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        #endregion

        #region Internal Utils
        internal static string Sanitize(string message)
        {
            var s = new string((message ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
            return s.Length <= MaxLineChars ? s : s.Substring(0, MaxLineChars) + "...";
        }

        private static void Trim(string path)
        {
            var lines = File.ReadAllLines(path);
            File.WriteAllLines(path, lines.Skip(lines.Length / 2).ToArray(), new UTF8Encoding(false));
        }
        #endregion
    }

    public enum UpdateOutcome { None, Applied, RolledBack }

    public sealed class UpdateResult
    {
        public UpdateOutcome Outcome;
        public string Version = string.Empty;
        public string Detail = string.Empty;

        public static UpdateResult Parse(string line)
        {
            var r = new UpdateResult();
            if (string.IsNullOrWhiteSpace(line)) return r;
            var parts = line.Trim().Split(new[] { ' ' }, 3);
            if (parts[0] == "OK" && parts.Length >= 2 && System.Version.TryParse(parts[1], out _))
            {
                r.Outcome = UpdateOutcome.Applied;
                r.Version = parts[1];
            }
            else if (parts[0] == "FAILED")
            {
                r.Outcome = UpdateOutcome.RolledBack;
                r.Detail = parts.Length == 3 ? parts[2] : string.Empty;
            }
            return r;
        }
    }
}
