using System;
using System.Security.Cryptography;

namespace LicenceManager
{
    /// <summary>
    /// Paire de cles ECDSA P-256 servant a signer les licences. La cle privee ne doit jamais
    /// quitter l'outil d'emission : seule <see cref="PublicKey"/> est embarquee dans les produits.
    /// </summary>
    public sealed class LicenceSigningKey
    {
        /// <summary>Taille de l'export de <see cref="ExportPrivateKey"/> : D, X puis Y (32 octets chacun).</summary>
        public const int PrivateKeySize = 96;

        private readonly byte[] _d;

        private LicenceSigningKey(byte[] d, LicencePublicKey publicKey)
        {
            _d = d;
            PublicKey = publicKey;
        }

        /// <summary>Cle publique a embarquer dans les produits pour valider les licences.</summary>
        public LicencePublicKey PublicKey { get; }

        /// <summary>Genere une nouvelle paire de cles.</summary>
        public static LicenceSigningKey Create()
        {
            using (ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                ECParameters p = ecdsa.ExportParameters(includePrivateParameters: true);
                return new LicenceSigningKey(PadLeft(p.D), new LicencePublicKey(PadLeft(p.Q.X), PadLeft(p.Q.Y)));
            }
        }

        /// <summary>Exporte la cle privee en clair (96 octets). A proteger avant tout stockage.</summary>
        public byte[] ExportPrivateKey()
        {
            byte[] b = new byte[PrivateKeySize];
            Buffer.BlockCopy(_d, 0, b, 0, 32);
            Buffer.BlockCopy(PublicKey.ToBytes(), 0, b, 32, 64);
            return b;
        }

        /// <summary>Recharge une cle exportee par <see cref="ExportPrivateKey"/> et verifie sa coherence.</summary>
        public static LicenceSigningKey ImportPrivateKey(byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length != PrivateKeySize)
                throw new ArgumentException($"Cle privee invalide : {PrivateKeySize} octets attendus.");

            byte[] d = new byte[32], x = new byte[32], y = new byte[32];
            Buffer.BlockCopy(privateKey, 0, d, 0, 32);
            Buffer.BlockCopy(privateKey, 32, x, 0, 32);
            Buffer.BlockCopy(privateKey, 64, y, 0, 32);
            var key = new LicenceSigningKey(d, new LicencePublicKey(x, y));

            // Une cle privee qui ne correspond pas a la cle publique produirait des licences
            // que les produits rejetteraient : on le detecte des le chargement.
            byte[] probe = new byte[] { 0x4C, 0x4D };
            if (!key.PublicKey.Verify(probe, key.Sign(probe)))
                throw new CryptographicException("La cle privee ne correspond pas a la cle publique.");
            return key;
        }

        internal byte[] Sign(byte[] data)
        {
            byte[] q = PublicKey.ToBytes();
            var p = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = (byte[])_d.Clone(),
                Q = new ECPoint { X = Slice(q, 0), Y = Slice(q, 32) },
            };
            using (ECDsa ecdsa = ECDsa.Create(p))
                return ecdsa.SignData(data, HashAlgorithmName.SHA256); // r || s, 64 octets
        }

        // ExportParameters peut renvoyer des coordonnees sans leurs zeros de tete.
        private static byte[] PadLeft(byte[] value)
        {
            if (value.Length == 32) return value;
            if (value.Length > 32) throw new CryptographicException("Coordonnee P-256 trop longue.");
            byte[] b = new byte[32];
            Buffer.BlockCopy(value, 0, b, 32 - value.Length, value.Length);
            return b;
        }

        private static byte[] Slice(byte[] b, int offset)
        {
            byte[] r = new byte[32];
            Buffer.BlockCopy(b, offset, r, 0, 32);
            return r;
        }
    }
}
