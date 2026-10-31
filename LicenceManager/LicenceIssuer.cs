using System;
using System.Security.Cryptography;

namespace LicenceManager
{
    /// <summary>Emet des cles de licence signees. Reserve a l'outil interne (detient la cle privee).</summary>
    public sealed class LicenceIssuer
    {
        private readonly LicenceSigningKey _key;

        /// <summary>Cree un emetteur a partir de la paire de cles de signature.</summary>
        public LicenceIssuer(LicenceSigningKey key)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
        }

        /// <summary>
        /// Signe <paramref name="data"/> et retourne la cle texte. Si <see cref="LicenceData.LicenceId"/>
        /// vaut 0, un identifiant aleatoire est tire et ecrit dans <paramref name="data"/>.
        /// </summary>
        public string Issue(LicenceData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.LicenceId == 0) data.LicenceId = NewLicenceId();

            byte[] payload = data.ToBytes();
            byte[] signature = _key.Sign(payload);

            byte[] raw = new byte[payload.Length + signature.Length];
            Buffer.BlockCopy(payload, 0, raw, 0, payload.Length);
            Buffer.BlockCopy(signature, 0, raw, payload.Length, signature.Length);
            return LicenceKeyCodec.Encode(raw);
        }

        private static ulong NewLicenceId()
        {
            byte[] b = new byte[8];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                ulong id;
                do
                {
                    rng.GetBytes(b);
                    id = BitConverter.ToUInt64(b, 0);
                } while (id == 0);
                return id;
            }
        }
    }
}
