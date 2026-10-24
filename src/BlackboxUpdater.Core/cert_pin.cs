using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BlackboxUpdater.Core
{
    public sealed class CertPin
    {
        #region Constants & Fields
        private static readonly Regex Hex64 = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);
        private static readonly char[] Separators = { ',', ';', ' ', '\t', '\r', '\n' };
        public const int MaxPins = 4;
        #endregion

        public readonly bool IsSpki;
        public readonly string Hash;

        private CertPin(bool isSpki, string hash) { IsSpki = isSpki; Hash = hash; }

        #region Public API
        public static bool TryParse(string text, out CertPin pin)
        {
            pin = null;
            if (text == null) return false;
            var t = text.Trim().ToLowerInvariant();
            var spki = t.StartsWith("spki:", StringComparison.Ordinal);
            if (spki) t = t.Substring(5);
            if (!Hex64.IsMatch(t)) return false;
            pin = new CertPin(spki, t);
            return true;
        }

        public static bool TryParseList(string text, out List<CertPin> pins)
        {
            pins = new List<CertPin>();
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParse(part, out var pin) || pins.Count >= MaxPins) { pins = new List<CertPin>(); return false; }
                pins.Add(pin);
            }
            return pins.Count > 0;
        }

        public bool Matches(byte[] certDer)
        {
            if (certDer == null || certDer.Length == 0) return false;
            try { return Sha256Hex(IsSpki ? X509Spki.Extract(certDer) : certDer) == Hash; }
            catch (FormatException) { return false; }
        }

        public static bool AnyMatches(IEnumerable<CertPin> pins, byte[] certDer)
        {
            if (pins == null) return false;
            foreach (var p in pins) if (p.Matches(certDer)) return true;
            return false;
        }

        public override string ToString() => (IsSpki ? "spki:" : string.Empty) + Hash;
        #endregion

        #region Internal Utils
        internal static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }
        #endregion
    }

    public static class X509Spki
    {
        public static byte[] Extract(byte[] cert)
        {
            if (cert == null) throw new FormatException("certificate is null");
            int certStart = Header(cert, 0, cert.Length, 0x30, out var certLen);
            int tbsStart = Header(cert, certStart, certStart + certLen, 0x30, out var tbsLen);
            int end = tbsStart + tbsLen;

            int pos = tbsStart;
            if (pos < end && cert[pos] == 0xA0) pos = Skip(cert, pos, end);
            for (var i = 0; i < 5; i++) pos = Skip(cert, pos, end);
            var next = Skip(cert, pos, end);
            if (cert[pos] != 0x30) throw new FormatException("subjectPublicKeyInfo is not a SEQUENCE");
            var spki = new byte[next - pos];
            Buffer.BlockCopy(cert, pos, spki, 0, spki.Length);
            return spki;
        }

        #region Internal Utils
        private static int Skip(byte[] b, int pos, int limit)
        {
            if (pos >= limit) throw new FormatException("truncated certificate");
            var contentStart = Header(b, pos, limit, b[pos], out var len);
            return contentStart + len;
        }

        private static int Header(byte[] b, int pos, int limit, int expectedTag, out int length)
        {
            if (pos + 2 > limit || b[pos] != expectedTag) throw new FormatException("unexpected ASN.1 tag");
            int first = b[pos + 1];
            int offset = pos + 2;
            if (first < 0x80) length = first;
            else
            {
                int n = first & 0x7F;
                if (n == 0 || n > 3) throw new FormatException("unsupported ASN.1 length");
                if (offset + n > limit) throw new FormatException("truncated ASN.1 length");
                length = 0;
                for (var i = 0; i < n; i++) length = (length << 8) | b[offset + i];
                offset += n;
            }
            if (length < 0 || offset + length > limit) throw new FormatException("ASN.1 element exceeds its container");
            return offset;
        }
        #endregion
    }
}
