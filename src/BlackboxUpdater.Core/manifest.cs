using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BlackboxUpdater.Core
{
    public sealed class ManifestFile
    {
        public string Path;
        public long Size;
        public string Sha256;
    }

    public sealed class Manifest
    {
        #region Constants & Fields
        public const string Magic = "BBUPD1";
        private const int MaxFiles = 4096;
        private static readonly Regex Hex64 = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);
        #endregion

        public Version Version;
        public IReadOnlyList<ManifestFile> Files;

        #region Public API
        public static Manifest Parse(byte[] bytes)
        {
            if (bytes == null) throw new FormatException("manifest is null");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (Exception) { throw new FormatException("manifest is not valid UTF-8"); }
            if (text.IndexOf('\r') >= 0) throw new FormatException("manifest must use LF line endings");
            if (!text.EndsWith("\n", StringComparison.Ordinal)) throw new FormatException("manifest must end with a newline");

            var lines = text.Substring(0, text.Length - 1).Split('\n');
            if (lines.Length < 2 || lines[0] != Magic) throw new FormatException("bad manifest header");

            if (!lines[1].StartsWith("version ", StringComparison.Ordinal)
                || !Version.TryParse(lines[1].Substring(8), out var version)
                || version.Major < 0 || version.Minor < 0)
                throw new FormatException("bad manifest version");

            var files = new List<ManifestFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 2; i < lines.Length; i++)
            {
                if (files.Count >= MaxFiles) throw new FormatException("too many files");
                var parts = lines[i].Split(new[] { ' ' }, 4);
                if (parts.Length != 4 || parts[0] != "file") throw new FormatException("bad file line " + (i + 1));
                if (!Hex64.IsMatch(parts[1])) throw new FormatException("bad sha256 on line " + (i + 1));
                if (!long.TryParse(parts[2], out var size) || size < 0) throw new FormatException("bad size on line " + (i + 1));
                if (!RelPath.IsValid(parts[3])) throw new FormatException("bad path on line " + (i + 1));
                if (!seen.Add(parts[3])) throw new FormatException("duplicate path on line " + (i + 1));
                files.Add(new ManifestFile { Path = parts[3], Size = size, Sha256 = parts[1] });
            }
            return new Manifest { Version = version, Files = files };
        }
        #endregion
    }
}
