using System;
using System.Collections.Generic;

namespace BlackboxUpdater.Core
{
    public static class UpdatePlanner
    {
        #region Public API
        public static bool IsNewer(Manifest manifest, Version installed)
        {
            return manifest != null && (installed == null || manifest.Version > installed);
        }

        public static List<ManifestFile> FilesToDownload(Manifest manifest, Func<string, string> localSha256)
        {
            var plan = new List<ManifestFile>();
            foreach (var f in manifest.Files)
                if (localSha256(f.Path) != f.Sha256) plan.Add(f);
            return plan;
        }
        #endregion
    }
}
