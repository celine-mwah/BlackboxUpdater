using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BlackboxUpdater.Core;
using UnityEngine;

namespace BlackboxUpdater.Plugin
{
    [BepInPlugin("com.blackbox.updater", "Blackbox Updater", "1.0.0")]
    public sealed class UpdaterPlugin : BaseUnityPlugin
    {
        #region Constants & Fields
        private const float ToastSeconds = 15f;
        private ConfigEntry<bool> _enabled;
        private TrustSettings _trust;
        private string _installDir;
        private string _workRoot;
        private Thread _worker;
        private volatile string _pendingToast;
        private string _toast;
        private float _toastUntil;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Check for Blackbox updates on launch. Updates install after you close the game.");
            if (!_enabled.Value) return;

            _trust = TrustSettings.From(typeof(UpdaterPlugin).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                .Cast<AssemblyMetadataAttribute>()
                .Select(a => new KeyValuePair<string, string>(a.Key, a.Value)));
            if (!_trust.IsConfigured)
            {
                Logger.LogWarning("[Updater] This build has no trust settings (server url, certificate pin, signing key); the updater is inactive.");
                return;
            }

            _installDir = Path.Combine(Paths.PluginPath, "Blackbox");
            if (!Directory.Exists(_installDir))
            {
                Logger.LogWarning("[Updater] Blackbox folder not found at " + _installDir + "; nothing to update.");
                return;
            }

            _workRoot = Path.Combine(Paths.BepInExRootPath, "blackbox_updater");
            try { ApplyLauncher.EnsureOutside(_workRoot, Paths.PluginPath); }
            catch (UpdateException e)
            {
                Logger.LogError("[Updater] " + e.Message + "; the updater is inactive.");
                return;
            }

            ReportPreviousResult();
            Hist("launch: updater started");
            _worker = new Thread(Run) { IsBackground = true, Name = "BlackboxUpdater" };
            _worker.Start();
        }

        private void Update()
        {
            var pending = _pendingToast;
            if (pending != null)
            {
                _pendingToast = null;
                _toast = pending;
                _toastUntil = Time.realtimeSinceStartup + ToastSeconds;
            }
        }

        private void OnGUI()
        {
            if (_toast == null) return;
            if (Time.realtimeSinceStartup > _toastUntil) { _toast = null; return; }
            const float w = 520f;
            GUI.Box(new Rect((Screen.width - w) / 2f, 12f, w, 34f), _toast);
        }

        private void OnDestroy()
        {
            _worker = null;
            _toast = null;
            _pendingToast = null;
        }
        #endregion

        #region Worker
        private void Run()
        {
            try
            {
                if (!P256Ecdsa.SelfTest())
                {
                    Logger.LogError("[Updater] Signature self-test failed on this runtime; updates are disabled.");
                    return;
                }

                var client = new UpdateClient(
                    new PinnedHttpFetcher(_trust.Pins),
                    _trust.ServerBaseUrl,
                    _trust.PublicKeys.Select(ManifestVerifier.PublicKeyFromHex).ToList());

                var installed = ReadInstalledVersion();
                var check = client.Check(installed, LocalSha256);
                Hist("check: installed=" + (installed?.ToString() ?? "unknown") + " latest=" + check.Manifest.Version
                    + " available=" + check.Available + (check.Available ? " files=" + check.Changed.Count : string.Empty));
                if (!check.Available) return;

                var version = check.Manifest.Version;
                var stage = ApplyLauncher.StageDir(_workRoot, version);
                if (Directory.Exists(stage)) Directory.Delete(stage, true);

                client.Stage(check, stage);
                ApplyLauncher.WritePlan(stage, version, check.Changed.Select(f => f.Path));
                ApplyLauncher.Launch(_installDir, _workRoot, version, CurrentPid(), CurrentStartTicks());

                Hist("staged " + version + ": install scheduled for game exit");
                Logger.LogMessage("[Updater] Blackbox " + version + " downloaded. It installs when you close the game.");
                _pendingToast = "Blackbox " + version + " downloaded. It will install when you close the game.";
            }
            catch (UpdateException e)
            {
                var detail = e.Message + (e.InnerException != null ? " (" + e.InnerException.Message + ")" : string.Empty);
                Hist("check failed: " + detail);
                Logger.LogWarning("[Updater] Update check failed: " + detail);
            }
            catch (Exception e)
            {
                Hist("unexpected error: " + e.GetType().Name + ": " + e.Message);
                Logger.LogError("[Updater] Unexpected error: " + e);
            }
        }
        #endregion

        #region Internal Utils
        private Version ReadInstalledVersion()
        {
            var path = Path.Combine(_installDir, "version.txt");
            try { return File.Exists(path) && Version.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : null; }
            catch (IOException) { return null; }
        }

        private string LocalSha256(string rel)
        {
            var full = Path.GetFullPath(Path.Combine(_installDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(_installDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            try { return File.Exists(full) ? UpdateClient.Sha256Hex(File.ReadAllBytes(full)) : null; }
            catch (IOException) { return null; }
        }

        private void ReportPreviousResult()
        {
            try
            {
                var raw = ApplyLauncher.ReadResult(_workRoot);
                if (raw == null) return;
                var result = UpdateResult.Parse(raw);
                if (result.Outcome == UpdateOutcome.Applied)
                {
                    Hist("previous update applied: Blackbox " + result.Version);
                    Logger.LogMessage("[Updater] Blackbox was updated to " + result.Version + ".");
                    _pendingToast = "Blackbox was updated to " + result.Version + ".";
                }
                else if (result.Outcome == UpdateOutcome.RolledBack)
                {
                    Hist("previous update FAILED and was rolled back: " + result.Detail);
                    Logger.LogWarning("[Updater] The last update did not install and was rolled back: " + result.Detail);
                    _pendingToast = "The last Blackbox update failed and was rolled back. See BepInEx\\blackbox_updater\\history.log";
                }
                File.Delete(Path.Combine(_workRoot, ApplyLauncher.ResultName));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void Hist(string message) => UpdateHistory.Append(_workRoot, message);

        private static int CurrentPid()
        {
            using (var p = Process.GetCurrentProcess()) return p.Id;
        }

        private static long CurrentStartTicks()
        {
            try { using (var p = Process.GetCurrentProcess()) return p.StartTime.ToFileTimeUtc(); }
            catch (Exception) { return 0; }
        }
        #endregion
    }
}
