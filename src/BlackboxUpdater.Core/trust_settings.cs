using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BlackboxUpdater.Core
{
    public sealed class TrustSettings
    {
        #region Constants & Fields
        public const string UrlKey = "ServerBaseUrl";
        public const string PinKey = "CertPinSha256Hex";
        public const string PublicKeyKey = "PublicKeyHex";
        public const int MaxKeys = 4;
        private static readonly Regex Hex128 = new Regex("^[0-9a-f]{128}$", RegexOptions.Compiled);
        private static readonly char[] Separators = { ',', ';', ' ', '\t', '\r', '\n' };
        #endregion

        public string ServerBaseUrl = string.Empty;
        public string CertPinSha256Hex = string.Empty;
        public string PublicKeyHex = string.Empty;
        public List<CertPin> Pins = new List<CertPin>();
        public List<string> PublicKeys = new List<string>();

        #region Public API
        public bool IsConfigured =>
            ServerBaseUrl.StartsWith("https://") && ServerBaseUrl.Length > "https://".Length
            && Pins.Count > 0 && PublicKeys.Count > 0;

        public static TrustSettings From(IEnumerable<KeyValuePair<string, string>> metadata)
        {
            var t = new TrustSettings();
            foreach (var kv in metadata)
            {
                var v = (kv.Value ?? string.Empty).Trim();
                if (kv.Key == UrlKey) t.ServerBaseUrl = v.TrimEnd('/');
                else if (kv.Key == PinKey) t.CertPinSha256Hex = v.ToLowerInvariant();
                else if (kv.Key == PublicKeyKey) t.PublicKeyHex = v.ToLowerInvariant();
            }

            if (CertPin.TryParseList(t.CertPinSha256Hex, out var pins)) t.Pins = pins;
            t.PublicKeys = ParseKeys(t.PublicKeyHex);
            return t;
        }
        #endregion

        #region Internal Utils
        private static List<string> ParseKeys(string text)
        {
            var keys = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return keys;
            foreach (var part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Hex128.IsMatch(part) || keys.Count >= MaxKeys || keys.Contains(part)) return new List<string>();
                keys.Add(part);
            }
            return keys;
        }
        #endregion
    }
}
