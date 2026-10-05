using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using wechat_miniapp_base.Models;

namespace SnowmeetApi.Services.Payments
{
    public static class WepayPlatformCertificates
    {
        private sealed class StoredCertificate
        {
            public string serial_no { get; set; }
            public string certificate { get; set; }
        }

        // DB certificates are authoritative once configured. Disk fallback is only for
        // merchants that have not yet migrated; never use an unknown serial as a file path.
        public static InMemoryCertificateManager CreateManager(
            WepayKey key, string requiredSerial = null, string legacyDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(key);
            string serial = requiredSerial == null ? null : NormalizeSerial(requiredSerial);
            var manager = new InMemoryCertificateManager();
            bool configured = !string.IsNullOrWhiteSpace(key.platform_certificates);
            if (configured)
            {
                var entries = JsonConvert.DeserializeObject<List<StoredCertificate>>(key.platform_certificates)
                    ?? throw new InvalidOperationException("Missing WeChat platform certificate list.");
                if (entries.Count == 0)
                    throw new InvalidOperationException("Empty WeChat platform certificate list.");
                foreach (var item in entries)
                {
                    if (item == null)
                        throw new InvalidOperationException("Invalid WeChat platform certificate entry.");
                    Add(manager, item.serial_no, item.certificate);
                }
            }
            else if (!string.IsNullOrWhiteSpace(key.cert))
            {
                var entry = new CertificateEntry("RSA", key.cert.Trim());
                manager.AddEntry(entry);
            }

            if (serial != null && !manager.AllEntries().Any(e =>
                string.Equals(e.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)))
            {
                if (configured)
                    throw new InvalidOperationException("Unknown WeChat platform certificate serial.");
                string directory = legacyDirectory ?? Path.Combine(Environment.CurrentDirectory, "WepayCertificate");
                Add(manager, serial, File.ReadAllText(Path.Combine(directory, serial + ".pem")));
            }
            return manager;
        }

        private static void Add(InMemoryCertificateManager manager, string serial, string pem)
        {
            serial = NormalizeSerial(serial);
            if (string.IsNullOrWhiteSpace(pem))
                throw new InvalidOperationException("Missing WeChat platform certificate PEM.");
            var entry = new CertificateEntry("RSA", pem.Trim());
            if (!string.Equals(serial, entry.SerialNumber, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("WeChat platform certificate serial does not match PEM.");
            if (manager.AllEntries().Any(e =>
                string.Equals(e.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)
                && e.Certificate != entry.Certificate))
                throw new InvalidOperationException("Conflicting WeChat platform certificates.");
            manager.AddEntry(entry);
        }

        private static string NormalizeSerial(string value)
        {
            string serial = value?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(serial) || serial.Length > 64 || serial.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidOperationException("Invalid WeChat platform certificate serial.");
            return serial;
        }
    }
}
