using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace LicenceManager.KeyGeneratorApp
{
    // Fichier de la paire de cles de signature (JSON). La cle privee n'y est jamais en clair :
    //  - "dpapi-user"  : chiffree par Windows (DPAPI) pour le compte courant. Fichier de travail,
    //                    lisible uniquement par cet utilisateur sur ce poste.
    //  - "passphrase"  : PBKDF2-SHA256 (600 000 iterations) -> AES-256-CBC + HMAC-SHA256
    //                    (chiffrer puis authentifier). Sauvegarde transportable.
    // La cle publique et son empreinte sont en clair pour pouvoir les afficher sans dechiffrer.
    public static class SigningKeyFile
    {
        public const string FormatName = "LicenceManager.SigningKey/1";
        public const string Dpapi = "dpapi-user";
        public const string Passphrase = "passphrase";
        public const int MinPassphraseLength = 12;

        private const int Iterations = 600_000;
        private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes(FormatName);

        private sealed class Model
        {
            public string Format { get; set; }
            public string Protection { get; set; }
            public string PublicKey { get; set; }
            public string Fingerprint { get; set; }
            public string Salt { get; set; }
            public int? Iterations { get; set; }
            public string Iv { get; set; }
            public string PrivateKey { get; set; }
            public string Mac { get; set; }
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static void SaveWithDpapi(string path, LicenceSigningKey key)
        {
            byte[] plain = key.ExportPrivateKey();
            try
            {
                Write(path, new Model
                {
                    Protection = Dpapi,
                    PrivateKey = Convert.ToBase64String(ProtectedData.Protect(plain, DpapiEntropy, DataProtectionScope.CurrentUser)),
                }, key);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public static void SaveWithPassphrase(string path, LicenceSigningKey key, string passphrase)
        {
            if (passphrase == null || passphrase.Length < MinPassphraseLength)
                throw new ArgumentException($"La phrase de passe doit contenir au moins {MinPassphraseLength} caractères.");

            byte[] salt = RandomBytes(16), iv = RandomBytes(16);
            byte[] plain = key.ExportPrivateKey();
            try
            {
                DeriveKeys(passphrase, salt, Iterations, out byte[] encKey, out byte[] macKey);
                byte[] cipher;
                using (Aes aes = CreateAes(encKey, iv))
                using (ICryptoTransform enc = aes.CreateEncryptor())
                    cipher = enc.TransformFinalBlock(plain, 0, plain.Length);

                Write(path, new Model
                {
                    Protection = Passphrase,
                    Salt = Convert.ToBase64String(salt),
                    Iterations = Iterations,
                    Iv = Convert.ToBase64String(iv),
                    PrivateKey = Convert.ToBase64String(cipher),
                    Mac = Convert.ToBase64String(ComputeMac(macKey, iv, cipher)),
                }, key);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        // Protection du fichier ("dpapi-user" ou "passphrase"), pour savoir s'il faut demander
        // une phrase de passe avant Load.
        public static string ReadProtection(string path) => Read(path).Protection;

        public static LicenceSigningKey Load(string path, string passphrase = null)
        {
            Model m = Read(path);
            byte[] plain;
            switch (m.Protection)
            {
                case Dpapi:
                    try
                    {
                        plain = ProtectedData.Unprotect(Convert.FromBase64String(m.PrivateKey), DpapiEntropy, DataProtectionScope.CurrentUser);
                    }
                    catch (CryptographicException)
                    {
                        throw new InvalidOperationException(
                            "Ce fichier a été protégé par un autre compte Windows ou sur un autre poste. " +
                            "Chargez plutôt la sauvegarde protégée par phrase de passe.");
                    }
                    break;

                case Passphrase:
                    if (string.IsNullOrEmpty(passphrase))
                        throw new InvalidOperationException("Phrase de passe requise.");
                    byte[] salt = Convert.FromBase64String(m.Salt), iv = Convert.FromBase64String(m.Iv);
                    byte[] cipher = Convert.FromBase64String(m.PrivateKey);
                    DeriveKeys(passphrase, salt, m.Iterations ?? Iterations, out byte[] encKey, out byte[] macKey);
                    if (!FixedTimeEquals(ComputeMac(macKey, iv, cipher), Convert.FromBase64String(m.Mac)))
                        throw new InvalidOperationException("Phrase de passe incorrecte (ou fichier modifié).");
                    using (Aes aes = CreateAes(encKey, iv))
                    using (ICryptoTransform dec = aes.CreateDecryptor())
                        plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    break;

                default:
                    throw new InvalidOperationException("Protection de clé inconnue : " + m.Protection);
            }

            try
            {
                LicenceSigningKey key = LicenceSigningKey.ImportPrivateKey(plain);
                if (key.PublicKey.ToBase64() != m.PublicKey)
                    throw new InvalidOperationException("La clé publique du fichier ne correspond pas à la clé privée.");
                return key;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        private static void Write(string path, Model m, LicenceSigningKey key)
        {
            m.Format = FormatName;
            m.PublicKey = key.PublicKey.ToBase64();
            m.Fingerprint = key.PublicKey.Fingerprint;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(m, Settings) + "\r\n", new UTF8Encoding(false));
        }

        private static Model Read(string path)
        {
            Model m;
            try
            {
                m = JsonConvert.DeserializeObject<Model>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                m = null;
            }
            if (m == null || m.Format != FormatName || string.IsNullOrEmpty(m.PrivateKey))
                throw new InvalidOperationException(
                    "Ce fichier n'est pas une paire de clés LicenceManager (format " + FormatName + ").");
            return m;
        }

        private static void DeriveKeys(string passphrase, byte[] salt, int iterations, out byte[] encKey, out byte[] macKey)
        {
            using (var kdf = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256))
            {
                encKey = kdf.GetBytes(32);
                macKey = kdf.GetBytes(32);
            }
        }

        private static Aes CreateAes(byte[] key, byte[] iv)
        {
            Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            return aes;
        }

        private static byte[] ComputeMac(byte[] macKey, byte[] iv, byte[] cipher)
        {
            using (var hmac = new HMACSHA256(macKey))
            {
                byte[] data = new byte[iv.Length + cipher.Length];
                Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
                Buffer.BlockCopy(cipher, 0, data, iv.Length, cipher.Length);
                return hmac.ComputeHash(data);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] RandomBytes(int n)
        {
            byte[] b = new byte[n];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }
    }
}
