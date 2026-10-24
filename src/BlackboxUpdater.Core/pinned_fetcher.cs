using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace BlackboxUpdater.Core
{
    public sealed class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
        public UpdateException(string message, Exception inner) : base(message, inner) { }
    }

    public interface IFetcher
    {
        byte[] Get(string url, long maxBytes);
    }

    public sealed class PinnedHttpFetcher : IFetcher
    {
        #region Constants & Fields
        private readonly List<CertPin> _pins;
        private readonly int _timeoutMs;
        #endregion

        public PinnedHttpFetcher(IEnumerable<CertPin> pins, int timeoutMs = 20000)
        {
            _pins = new List<CertPin>(pins ?? new CertPin[0]);
            _timeoutMs = timeoutMs;
        }

        public PinnedHttpFetcher(string pinText, int timeoutMs = 20000)
            : this(CertPin.TryParseList(pinText, out var parsed) ? parsed : new List<CertPin>(), timeoutMs) { }

        #region Public API
        public static bool PinMatches(string pinHex, byte[] rawCert)
        {
            if (string.IsNullOrEmpty(pinHex) || rawCert == null) return false;
            using (var sha = SHA256.Create())
            {
                var hex = BitConverter.ToString(sha.ComputeHash(rawCert)).Replace("-", string.Empty).ToLowerInvariant();
                return hex == pinHex;
            }
        }

        public byte[] Get(string url, long maxBytes)
        {
            if (url == null || !url.StartsWith("https://", StringComparison.Ordinal))
                throw new UpdateException("only https urls are allowed");
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.AllowAutoRedirect = false;
                req.Timeout = _timeoutMs;
                req.ReadWriteTimeout = _timeoutMs;
                req.UserAgent = "BlackboxUpdater/1";
                req.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
                    cert != null && CertPin.AnyMatches(_pins, cert.GetRawCertData());

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) throw new UpdateException("HTTP " + (int)resp.StatusCode + " for " + url);
                    if (resp.ContentLength > maxBytes) throw new UpdateException("response too large for " + url);
                    using (var body = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[81920];
                        int n;
                        while ((n = body.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (ms.Length + n > maxBytes) throw new UpdateException("response too large for " + url);
                            ms.Write(buf, 0, n);
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch (UpdateException) { throw; }
            catch (WebException e)
            {
                var status = (e.Response as HttpWebResponse)?.StatusCode;
                throw new UpdateException(status.HasValue ? "HTTP " + (int)status.Value + " for " + url : "request failed: " + e.Message, e);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is NotSupportedException || e is System.Security.Authentication.AuthenticationException)
            {
                throw new UpdateException("request failed: " + e.Message, e);
            }
        }
        #endregion
    }
}
