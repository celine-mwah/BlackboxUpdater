using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlackboxUpdater.Core
{
    public sealed class UpdateCheck
    {
        public Manifest Manifest;
        public bool Available;
        public List<ManifestFile> Changed = new List<ManifestFile>();
    }

    public sealed class UpdateClient
    {
        #region Constants & Fields
        private const long MaxManifestBytes = 1 << 20;
        private static readonly Regex VersionText = new Regex(@"^[0-9]+(\.[0-9]+){1,3}$", RegexOptions.Compiled);
        private readonly IFetcher _fetcher;
        private readonly string _baseUrl;
        private readonly List<P256PublicKey> _publicKeys;
        #endregion

        public UpdateClient(IFetcher fetcher, string baseUrl, P256PublicKey publicKey)
            : this(fetcher, baseUrl, new[] { publicKey }) { }

        public UpdateClient(IFetcher fetcher, string baseUrl, IEnumerable<P256PublicKey> publicKeys)
        {
            _fetcher = fetcher;
            _baseUrl = baseUrl.TrimEnd('/');
            _publicKeys = new List<P256PublicKey>(publicKeys);
        }

        #region Public API
        public UpdateCheck Check(Version installed, Func<string, string> localSha256)
        {
            var latest = Encoding.ASCII.GetString(_fetcher.Get(_baseUrl + "/latest", 64)).Trim();
            if (!VersionText.IsMatch(latest)) throw new UpdateException("server returned an invalid version");

            var manifestBytes = _fetcher.Get(_baseUrl + "/" + latest + "/manifest.txt", MaxManifestBytes);
            var sig = _fetcher.Get(_baseUrl + "/" + latest + "/manifest.sig", 64);

            if (!ManifestVerifier.Verify(manifestBytes, sig, _publicKeys)) throw new UpdateException("manifest signature is invalid");

            Manifest manifest;
            try { manifest = Manifest.Parse(manifestBytes); }
            catch (FormatException e) { throw new UpdateException("signed manifest is malformed: " + e.Message, e); }

            if (manifest.Version.ToString() != latest) throw new UpdateException("manifest version does not match latest");

            var check = new UpdateCheck { Manifest = manifest, Available = UpdatePlanner.IsNewer(manifest, installed) };
            if (check.Available) check.Changed = UpdatePlanner.FilesToDownload(manifest, localSha256);
            return check;
        }

        public void Stage(UpdateCheck check, string stageDir)
        {
            var filesDir = Path.Combine(stageDir, "files");
            try
            {
                Directory.CreateDirectory(filesDir);
                foreach (var f in check.Changed)
                {
                    var url = _baseUrl + "/" + check.Manifest.Version + "/files/" + string.Join("/", f.Path.Split('/').Select(Uri.EscapeDataString));
                    var bytes = _fetcher.Get(url, f.Size);
                    if (bytes.LongLength != f.Size) throw new UpdateException("size mismatch for " + f.Path);
                    if (Sha256Hex(bytes) != f.Sha256) throw new UpdateException("hash mismatch for " + f.Path);

                    var dest = Path.Combine(filesDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest + ".part", bytes);
                    File.Move(dest + ".part", dest);
                }
            }
            catch
            {
                try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }
        #endregion
    }
}
