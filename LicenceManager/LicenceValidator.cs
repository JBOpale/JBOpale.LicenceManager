using System;

namespace LicenceManager
{
    /// <summary>Resultat de <see cref="LicenceValidator.Validate(string, int, DateTime)"/>.</summary>
    public enum LicenceStatus
    {
        /// <summary>Signature valide, bon produit, non expiree.</summary>
        Valid,
        /// <summary>Texte illisible : caractere invalide, mauvaise longueur ou erreur de copie (CRC).</summary>
        Malformed,
        /// <summary>Cle d'une version de format que ce validateur ne connait pas.</summary>
        UnsupportedVersion,
        /// <summary>Signature incorrecte : cle alteree ou emise avec une autre paire de cles.</summary>
        InvalidSignature,
        /// <summary>Signature valide mais emise pour un autre produit.</summary>
        WrongProduct,
        /// <summary>Licence liee a un autre poste (ou poste courant non identifiable).</summary>
        WrongMachine,
        /// <summary>Signature valide mais date d'expiration depassee.</summary>
        Expired,
    }

    /// <summary>Resultat detaille d'une validation.</summary>
    public sealed class LicenceCheck
    {
        internal LicenceCheck(LicenceStatus status, LicenceData data)
        {
            Status = status;
            Data = data;
        }

        /// <summary>Statut de la validation.</summary>
        public LicenceStatus Status { get; }

        /// <summary>Vrai uniquement si <see cref="Status"/> vaut <see cref="LicenceStatus.Valid"/>.</summary>
        public bool IsValid => Status == LicenceStatus.Valid;

        /// <summary>Contenu de la cle quand la signature est valide (y compris expiree ou autre produit), null sinon.</summary>
        public LicenceData Data { get; }
    }

    /// <summary>Valide des cles de licence avec la cle publique du produit.</summary>
    public sealed class LicenceValidator
    {
        private readonly LicencePublicKey _publicKey;

        /// <summary>Cree un validateur pour la cle publique donnee.</summary>
        public LicenceValidator(LicencePublicKey publicKey)
        {
            _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        }

        /// <summary>Cree un validateur a partir de la cle publique en Base64.</summary>
        public LicenceValidator(string publicKeyBase64)
            : this(LicencePublicKey.FromBase64(publicKeyBase64))
        {
        }

        /// <summary>
        /// Valide la cle pour le produit donne, sur le poste courant, a la date courante
        /// (horloge locale UTC).
        /// </summary>
        public LicenceCheck Validate(string licenceKey, int expectedProductId) =>
            Validate(licenceKey, expectedProductId, DateTime.UtcNow);

        /// <summary>Valide la cle pour le produit donne, sur le poste courant, a la date <paramref name="utcNow"/>.</summary>
        public LicenceCheck Validate(string licenceKey, int expectedProductId, DateTime utcNow)
        {
            LicenceCheck check = Read(licenceKey);
            // Le poste n'est identifie que si la licence y est liee.
            string machine = check.Data?.MachineCode != null ? MachineCode.Current : null;
            return Validate(check, expectedProductId, utcNow, machine);
        }

        /// <summary>
        /// Valide la cle pour le produit donne, a la date <paramref name="utcNow"/>, en supposant
        /// que le poste courant a le code <paramref name="currentMachineCode"/> (null : inconnu).
        /// </summary>
        public LicenceCheck Validate(string licenceKey, int expectedProductId, DateTime utcNow, string currentMachineCode) =>
            Validate(Read(licenceKey), expectedProductId, utcNow, currentMachineCode);

        private static LicenceCheck Validate(LicenceCheck check, int expectedProductId, DateTime utcNow, string currentMachineCode)
        {
            LicenceData data = check.Data;
            if (data == null) return check;
            if (data.ProductId != expectedProductId) return new LicenceCheck(LicenceStatus.WrongProduct, data);
            if (data.MachineCode != null && !SameMachine(data.MachineCode, currentMachineCode))
                return new LicenceCheck(LicenceStatus.WrongMachine, data);
            if (data.IsExpiredAt(utcNow)) return new LicenceCheck(LicenceStatus.Expired, data);
            return check;
        }

        private static bool SameMachine(string licenceCode, string currentCode) =>
            currentCode != null && MachineCode.TryNormalize(currentCode, out string current) && current == licenceCode;

        /// <summary>
        /// Verifie le format et la signature sans controler le produit ni l'expiration
        /// (utile pour inspecter une cle quel que soit son produit).
        /// </summary>
        public LicenceCheck Read(string licenceKey)
        {
            if (!LicenceKeyCodec.TryDecode(licenceKey, out byte[] raw))
                return new LicenceCheck(LicenceStatus.Malformed, null);
            if (raw[0] != LicenceData.FormatVersion)
                return new LicenceCheck(LicenceStatus.UnsupportedVersion, null);

            int payloadLength = raw.Length - LicencePublicKey.SignatureSize;
            if (payloadLength < LicenceData.FixedSize
                || !LicenceData.TryParse(raw, 0, payloadLength, out LicenceData data))
                return new LicenceCheck(LicenceStatus.Malformed, null);

            byte[] signature = new byte[LicencePublicKey.SignatureSize];
            Buffer.BlockCopy(raw, payloadLength, signature, 0, signature.Length);
            if (!_publicKey.Verify(raw, 0, payloadLength, signature))
                return new LicenceCheck(LicenceStatus.InvalidSignature, null);

            return new LicenceCheck(LicenceStatus.Valid, data);
        }
    }
}
