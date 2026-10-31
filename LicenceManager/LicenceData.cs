using System;
using System.Text;

namespace LicenceManager
{
    /// <summary>
    /// Contenu signe d'une cle de licence (format v1). Voir FORMAT.md pour la disposition exacte
    /// des octets, que le validateur C++ reproduit a l'identique.
    /// </summary>
    public sealed class LicenceData
    {
        /// <summary>Version du format ecrite dans le premier octet.</summary>
        public const byte FormatVersion = 1;

        /// <summary>Taille maximale du nom client, en octets UTF-8.</summary>
        public const int ClientMaxBytes = 40;

        // version(1) productId(4) expiration(2) features(8) licenceId(8) seats(2) machine(8)
        // clientLength(1), puis client(n), fieldsLength(1) et champs personnalises(m).
        internal const int ClientOffset = 34;
        internal const int FixedSize = ClientOffset + 1;

        private static readonly DateTime Epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Identifiant du produit.</summary>
        public int ProductId { get; set; }

        /// <summary>Nom du client, au plus <see cref="ClientMaxBytes"/> octets en UTF-8.</summary>
        public string Client { get; set; } = string.Empty;

        /// <summary>
        /// Dernier jour de validite (UTC, la partie heure est ignoree), ou null si la licence
        /// n'expire jamais. La licence reste valide toute la journee indiquee.
        /// </summary>
        public DateTime? Expiration { get; set; }

        /// <summary>Masque de modules : la signification de chaque bit appartient au produit.</summary>
        public ulong Features { get; set; }

        /// <summary>Identifiant unique de la licence (tire au hasard a l'emission).</summary>
        public ulong LicenceId { get; set; }

        /// <summary>Nombre de postes autorises, 0 si non limite.</summary>
        public ushort Seats { get; set; }

        /// <summary>
        /// Code du poste auquel la licence est liee (voir <see cref="LicenceManager.MachineCode"/>),
        /// ou null si la licence est utilisable sur tout poste.
        /// </summary>
        public string MachineCode { get; set; }

        /// <summary>Champs personnalises du produit (vide par defaut).</summary>
        public LicenceFields Fields { get; set; } = new LicenceFields();

        /// <summary>
        /// Vrai si la licence est expiree a la date donnee (comparaison au jour pres, en UTC).
        /// </summary>
        public bool IsExpiredAt(DateTime utcNow) =>
            Expiration.HasValue && utcNow.Date > Expiration.Value.Date;

        internal byte[] ToBytes()
        {
            byte[] client = Encoding.UTF8.GetBytes(Client ?? string.Empty);
            if (client.Length > ClientMaxBytes)
                throw new ArgumentException($"Le nom client depasse {ClientMaxBytes} octets en UTF-8.");

            byte[] machine = new byte[LicenceManager.MachineCode.Size];
            if (!string.IsNullOrWhiteSpace(MachineCode) && !LicenceManager.MachineCode.TryParse(MachineCode, out machine))
                throw new ArgumentException("Code poste invalide (erreur de copie ?).");

            LicenceFields fields = Fields ?? new LicenceFields();
            byte[] b = new byte[FixedSize + client.Length + fields.EncodedSize];
            b[0] = FormatVersion;
            WriteLe(b, 1, (uint)ProductId, 4);
            WriteLe(b, 5, EncodeExpiration(Expiration), 2);
            WriteLe(b, 7, Features, 8);
            WriteLe(b, 15, LicenceId, 8);
            WriteLe(b, 23, Seats, 2);
            Buffer.BlockCopy(machine, 0, b, 25, machine.Length);
            b[33] = (byte)client.Length;
            Buffer.BlockCopy(client, 0, b, ClientOffset, client.Length);
            fields.WriteTo(b, ClientOffset + client.Length);
            return b;
        }

        // Le controle de version est fait par l'appelant (pour distinguer "version inconnue" de
        // "cle mal formee") ; ici on ne verifie que la coherence des longueurs.
        internal static bool TryParse(byte[] b, int offset, int count, out LicenceData data)
        {
            data = null;
            if (count < FixedSize) return false;
            int clientLength = b[offset + 33];
            if (clientLength > ClientMaxBytes || count < FixedSize + clientLength) return false;
            int fieldsLength = b[offset + ClientOffset + clientLength];
            if (count != FixedSize + clientLength + fieldsLength
                || !LicenceFields.TryParse(b, offset + FixedSize + clientLength, fieldsLength, out LicenceFields fields))
                return false;

            ushort days = (ushort)ReadLe(b, offset + 5, 2);
            byte[] machine = new byte[LicenceManager.MachineCode.Size];
            Buffer.BlockCopy(b, offset + 25, machine, 0, machine.Length);
            data = new LicenceData
            {
                ProductId = (int)(uint)ReadLe(b, offset + 1, 4),
                Expiration = days == 0 ? (DateTime?)null : Epoch.AddDays(days),
                Features = ReadLe(b, offset + 7, 8),
                LicenceId = ReadLe(b, offset + 15, 8),
                Seats = (ushort)ReadLe(b, offset + 23, 2),
                MachineCode = ReadLe(machine, 0, machine.Length) == 0 ? null : LicenceManager.MachineCode.Format(machine),
                Client = Encoding.UTF8.GetString(b, offset + ClientOffset, clientLength),
                Fields = fields,
            };
            return true;
        }

        // Jours depuis le 01/01/2000 ; 0 est reserve a "jamais".
        private static ushort EncodeExpiration(DateTime? expiration)
        {
            if (!expiration.HasValue) return 0;
            double days = (expiration.Value.Date - Epoch).TotalDays;
            if (days < 1 || days > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(Expiration), "Date d'expiration hors de la plage 02/01/2000 - 2179.");
            return (ushort)days;
        }

        private static void WriteLe(byte[] b, int offset, ulong value, int size)
        {
            for (int i = 0; i < size; i++) b[offset + i] = (byte)(value >> (8 * i));
        }

        private static ulong ReadLe(byte[] b, int offset, int size)
        {
            ulong v = 0;
            for (int i = 0; i < size; i++) v |= (ulong)b[offset + i] << (8 * i);
            return v;
        }
    }
}
