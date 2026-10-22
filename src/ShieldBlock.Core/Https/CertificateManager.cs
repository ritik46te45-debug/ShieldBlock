using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Https
{
    public class CertificateManager : IDisposable
    {
        private const string CaSubject = "CN=ShieldBlock Filtering Root CA, O=ShieldBlock Privacy, OU=Network Security";
        private readonly string _certsDirectory;
        private readonly string _caPfxPath;
        private X509Certificate2? _rootCaCert;
        private readonly ConcurrentDictionary<string, X509Certificate2> _leafCertCache = new(StringComparer.OrdinalIgnoreCase);

        public bool IsCaInstalled => CheckIfCaInstalled();

        public CertificateManager(string? certsDirectory = null)
        {
            _certsDirectory = certsDirectory ?? Path.Combine(Constants.ProgramDataDir, "certs");
            Directory.CreateDirectory(_certsDirectory);
            _caPfxPath = Path.Combine(_certsDirectory, "root_ca.pfx");
        }

        public X509Certificate2 GetOrCreateRootCa()
        {
            if (_rootCaCert != null) return _rootCaCert;

            if (File.Exists(_caPfxPath))
            {
                try
                {
                    _rootCaCert = new X509Certificate2(_caPfxPath, "ShieldBlock-Safe-Entropy", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                    return _rootCaCert;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CertificateManager] Failed to load existing CA: {ex.Message}. Regenerating...");
                }
            }

            // Generate new Root CA
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(CaSubject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 1, critical: true));
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
            req.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = DateTimeOffset.UtcNow.AddYears(5);

            var cert = req.CreateSelfSigned(notBefore, notAfter);

            // Export to PFX
            byte[] pfxBytes = cert.Export(X509ContentType.Pfx, "ShieldBlock-Safe-Entropy");
            File.WriteAllBytes(_caPfxPath, pfxBytes);

            _rootCaCert = new X509Certificate2(pfxBytes, "ShieldBlock-Safe-Entropy", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            return _rootCaCert;
        }

        public bool InstallRootCertificate()
        {
            try
            {
                var ca = GetOrCreateRootCa();
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);

                // Check if already in store
                var existing = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, CaSubject, false);
                if (existing.Count > 0)
                {
                    return true;
                }

                // Add public cert only to store
                using var publicCert = new X509Certificate2(ca.RawData);
                store.Add(publicCert);
                store.Close();

                Console.WriteLine("[CertificateManager] Root CA successfully installed in CurrentUser\\Root store.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CertificateManager] Failed to install Root CA: {ex.Message}");
                return false;
            }
        }

        public bool UninstallRootCertificate()
        {
            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);

                var matches = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, CaSubject, false);
                foreach (var cert in matches)
                {
                    store.Remove(cert);
                    cert.Dispose();
                }

                store.Close();

                // Clear leaf cert cache
                foreach (var kvp in _leafCertCache)
                {
                    kvp.Value.Dispose();
                }
                _leafCertCache.Clear();

                Console.WriteLine("[CertificateManager] Root CA completely removed from certificate store.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CertificateManager] Failed to remove Root CA: {ex.Message}");
                return false;
            }
        }

        public bool CheckIfCaInstalled()
        {
            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var matches = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, CaSubject, false);
                bool found = matches.Count > 0;
                store.Close();
                return found;
            }
            catch
            {
                return false;
            }
        }

        public X509Certificate2 GetOrCreateLeafCertificate(string domain)
        {
            if (_leafCertCache.TryGetValue(domain, out var cached))
            {
                return cached;
            }

            var rootCa = GetOrCreateRootCa();
            using var rsa = RSA.Create(2048);

            var req = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(domain);
            if (!domain.StartsWith("*."))
            {
                sanBuilder.AddDnsName("*." + domain);
            }

            req.CertificateExtensions.Add(sanBuilder.Build());
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Server Authentication

            var epoch = DateTimeOffset.UtcNow;
            byte[] serialNumber = new byte[8];
            RandomNumberGenerator.Fill(serialNumber);

            using var leafWithKey = req.Create(rootCa, epoch.AddDays(-1), epoch.AddDays(90), serialNumber);
            var leafWithPrivateKey = leafWithKey.CopyWithPrivateKey(rsa);

            _leafCertCache[domain] = leafWithPrivateKey;
            return leafWithPrivateKey;
        }

        public void Dispose()
        {
            _rootCaCert?.Dispose();
            foreach (var cert in _leafCertCache.Values)
            {
                cert.Dispose();
            }
            _leafCertCache.Clear();
        }
    }
}
