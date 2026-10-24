using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BlackboxUpdater.Core
{
    public static class ManifestVerifier
    {
        #region Public API
        public static bool Verify(byte[] manifest, byte[] signature, P256PublicKey publicKey)
        {
            return publicKey != null && Verify(manifest, signature, new[] { publicKey });
        }

        public static bool Verify(byte[] manifest, byte[] signature, IEnumerable<P256PublicKey> publicKeys)
        {
            if (manifest == null || signature == null || signature.Length != 64 || publicKeys == null) return false;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(manifest);
                foreach (var key in publicKeys)
                    if (key != null && P256Ecdsa.Verify(key, hash, signature)) return true;
            }
            return false;
        }

        public static P256PublicKey PublicKeyFromHex(string hex)
        {
            if (hex == null || hex.Length != 128) throw new FormatException("public key must be 128 hex chars");
            var x = new byte[32];
            var y = new byte[32];
            try
            {
                for (var i = 0; i < 32; i++)
                {
                    x[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    y[i] = Convert.ToByte(hex.Substring(64 + i * 2, 2), 16);
                }
            }
            catch (FormatException) { throw new FormatException("public key is not valid hex"); }
            return P256PublicKey.FromCoordinates(x, y);
        }
        #endregion
    }
}
