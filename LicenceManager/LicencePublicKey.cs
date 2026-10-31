using System;
using System.Security.Cryptography;

namespace LicenceManager
{
    /// <summary>
    /// Cle publique ECDSA P-256 de verification : coordonnees X puis Y (32 octets chacune, big-endian),
    /// echangee en Base64 (88 caracteres). C'est la seule donnee de configuration des validateurs.
    /// </summary>
    public sealed class LicencePublicKey
    {
        /// <summary>Signature ECDSA P-256 au format r || s.</summary>
        public const int SignatureSize = 64;

        private readonly byte[] _x;
        private readonly byte[] _y;

        internal LicencePublicKey(byte[] x, byte[] y)
        {
            _x = x;
            _y = y;
        }

        /// <summary>Lit une cle publique exportee par <see cref="ToBase64"/>.</summary>
        public static LicencePublicKey FromBase64(string base64)
        {
            byte[] b;
            try { b = Convert.FromBase64String((base64 ?? string.Empty).Trim()); }
            catch (FormatException) { throw new ArgumentException("Cle publique : Base64 invalide."); }
            if (b.Length != 64) throw new ArgumentException("Cle publique : 64 octets attendus (X puis Y).");

            byte[] x = new byte[32], y = new byte[32];
            Buffer.BlockCopy(b, 0, x, 0, 32);
            Buffer.BlockCopy(b, 32, y, 0, 32);
            var key = new LicencePublicKey(x, y);
            using (key.CreateEcdsa()) { } // rejette un point hors de la courbe
            return key;
        }

        /// <summary>Exporte la cle (X puis Y) en Base64.</summary>
        public string ToBase64() => Convert.ToBase64String(ToBytes());

        /// <summary>
        /// Empreinte courte (16 caracteres hexadecimaux du SHA-256 de la cle) pour verifier d'un
        /// coup d'oeil qu'un produit et l'outil d'emission utilisent la meme paire.
        /// </summary>
        public string Fingerprint
        {
            get
            {
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(ToBytes()), 0, 8).Replace("-", "");
            }
        }

        internal byte[] ToBytes()
        {
            byte[] b = new byte[64];
            Buffer.BlockCopy(_x, 0, b, 0, 32);
            Buffer.BlockCopy(_y, 0, b, 32, 32);
            return b;
        }

        internal bool Verify(byte[] data, byte[] signature) => Verify(data, 0, data.Length, signature);

        internal bool Verify(byte[] data, int offset, int count, byte[] signature)
        {
            if (signature == null || signature.Length != SignatureSize) return false;
            using (ECDsa ecdsa = CreateEcdsa())
                return ecdsa.VerifyData(data, offset, count, signature, HashAlgorithmName.SHA256);
        }

        private ECDsa CreateEcdsa()
        {
            try
            {
                return ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = (byte[])_x.Clone(), Y = (byte[])_y.Clone() },
                });
            }
            // Windows (CNG) signale un point hors courbe par PlatformNotSupportedException,
            // OpenSSL par CryptographicException.
            catch (Exception ex) when (ex is CryptographicException || ex is PlatformNotSupportedException)
            {
                throw new ArgumentException("Cle publique : le point n'est pas sur la courbe P-256.");
            }
        }
    }
}
