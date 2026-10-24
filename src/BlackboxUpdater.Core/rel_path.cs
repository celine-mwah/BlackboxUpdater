using System;
using System.Text.RegularExpressions;

namespace BlackboxUpdater.Core
{
    public static class RelPath
    {
        #region Constants & Fields
        private static readonly Regex Allowed = new Regex(@"^[A-Za-z0-9_./ -]+$", RegexOptions.Compiled);
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };
        #endregion

        #region Public API
        public static bool IsValid(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 240) return false;
            if (!Allowed.IsMatch(path)) return false;
            if (path[0] == '/' || path.Contains("//") || path.Contains(":")) return false;

            foreach (var seg in path.Split('/'))
            {
                if (seg.Length == 0 || seg == "." || seg == "..") return false;
                if (seg[seg.Length - 1] == '.' || seg[seg.Length - 1] == ' ' || seg[0] == ' ') return false;
                var dot = seg.IndexOf('.');
                var stem = dot < 0 ? seg : seg.Substring(0, dot);
                if (Array.IndexOf(ReservedNames, stem.ToUpperInvariant()) >= 0) return false;
            }
            return true;
        }
        #endregion
    }
}
