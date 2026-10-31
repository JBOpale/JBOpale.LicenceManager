using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LicenceManager.KeyGeneratorApp
{
    // Emet et verifie des cles de licence au format LicenceManager v1 (voir FORMAT.md).
    // La paire de cles de signature est unique et partagee par tous les profils produit ; le
    // profil ne decide que du ProductId et de la presence des Features.
    public class LicenceKeyService
    {
        private LicenceSigningKey _key;
        private ProductProfile _profile = ProductProfile.Example();

        public bool IsLoaded => _key != null;
        public string LoadedPath { get; private set; }

        public string PublicKeyBase64 => _key?.PublicKey.ToBase64();
        public string Fingerprint => _key?.PublicKey.Fingerprint;

        public ProductProfile Profile
        {
            get => _profile;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                string err = value.Validate();
                if (err != null) throw new ArgumentException("Profil invalide : " + err);
                _profile = value;
            }
        }

        public int ClientMaxBytes => LicenceData.ClientMaxBytes;

        public static string DefaultKeyFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LicenceManager", "KeyGenerator", "licence-signing-key.json");

        // --- Paire de cles ----------------------------------------------------------------

        // Nouvelle paire en memoire : a enregistrer ensuite (SaveKeyFile) et a sauvegarder
        // (ExportBackup), sinon elle est perdue a la fermeture.
        public void CreateNewKeyPair()
        {
            _key = LicenceSigningKey.Create();
            LoadedPath = null;
        }

        // Fichier de travail, protege par DPAPI (compte Windows courant).
        public void SaveKeyFile(string path)
        {
            RequireLoaded();
            SigningKeyFile.SaveWithDpapi(path, _key);
            LoadedPath = path;
        }

        // Sauvegarde transportable, protegee par phrase de passe.
        public void ExportBackup(string path, string passphrase)
        {
            RequireLoaded();
            SigningKeyFile.SaveWithPassphrase(path, _key, passphrase);
        }

        // askPassphrase n'est appele que pour une sauvegarde protegee par phrase de passe ;
        // s'il renvoie null (annulation), rien n'est charge et la methode renvoie false.
        public bool LoadKeyFile(string path, Func<string> askPassphrase)
        {
            string passphrase = null;
            if (SigningKeyFile.ReadProtection(path) == SigningKeyFile.Passphrase)
            {
                passphrase = askPassphrase?.Invoke();
                if (passphrase == null) return false;
            }
            _key = SigningKeyFile.Load(path, passphrase);
            LoadedPath = path;
            return true;
        }

        // --- Emission ------------------------------------------------------------------

        // features est ignore (0) si le profil n'a pas de Features. machineCode : code poste
        // transmis par le client pour lier la licence a son poste, null pour tout poste.
        public string GenerateKey(string client, int productId, DateTime? expirationDateUtc, ulong features,
            ushort seats = 0, string machineCode = null, LicenceFields fields = null)
        {
            RequireLoaded();
            if (client == null) client = string.Empty;
            if (Encoding.UTF8.GetByteCount(client) > ClientMaxBytes)
                throw new ArgumentException($"Le nom client depasse {ClientMaxBytes} octets une fois encode en UTF-8 (tronquer le texte).");

            return new LicenceIssuer(_key).Issue(new LicenceData
            {
                ProductId = productId,
                Client = client,
                Expiration = expirationDateUtc?.Date,
                Features = _profile.HasFeatures ? features : 0,
                Seats = seats,
                MachineCode = machineCode,
                Fields = fields ?? new LicenceFields(),
            });
        }

        // Revalide la cle qu'on vient de generer avec le validateur de la bibliotheque (meme
        // code que les produits .NET) : garde-fou a l'usage, pas seulement en test.
        public bool SelfCheck(string licenceKey, out string details)
        {
            RequireLoaded();
            LicenceCheck check = new LicenceValidator(_key.PublicKey).Read(licenceKey);
            if (check.Data == null)
            {
                details = "Clé refusée : " + check.Status;
                return false;
            }

            LicenceData d = check.Data;
            details = $"Signature OK - Expiration={(d.Expiration.HasValue ? d.Expiration.Value.ToString("yyyy-MM-dd") : "jamais")} - ProductId={d.ProductId}";
            if (_profile.HasFeatures)
                details += $" - Features={FeatureMask.FormatHex(d.Features)}";
            details += " - Poste=" + (d.MachineCode ?? "tous");
            return true;
        }

        // Teste une cle : signature valide avec la paire chargee et ProductId connu d'un des
        // profils. Retourne null et un message dans error sinon. Ne modifie pas le profil courant.
        public KeyInspection Inspect(string licenceKey, IEnumerable<ProductProfile> profiles, out string error)
        {
            RequireLoaded();
            if (string.IsNullOrWhiteSpace(licenceKey))
            {
                error = "Saisissez une clé à tester.";
                return null;
            }

            LicenceCheck check = new LicenceValidator(_key.PublicKey).Read(licenceKey);
            switch (check.Status)
            {
                case LicenceStatus.Malformed:
                    error = "Clé illisible : caractère invalide, clé incomplète ou erreur de copie.";
                    return null;
                case LicenceStatus.UnsupportedVersion:
                    error = "Clé d'une version de format inconnue de cet outil.";
                    return null;
                case LicenceStatus.InvalidSignature:
                    error = "Clé invalide : signature incorrecte (clé modifiée, ou émise avec une autre paire de clés).";
                    return null;
            }

            LicenceData d = check.Data;
            ProductProfile p = profiles.FirstOrDefault(x => x.ProductId == d.ProductId);
            if (p == null)
            {
                error = $"Signature valide mais ProductId {d.ProductId} inconnu : aucun profil produit ne correspond.";
                return null;
            }

            error = null;
            return new KeyInspection
            {
                Profile = p,
                ProductId = d.ProductId,
                Client = d.Client,
                Expiration = d.Expiration,
                Expired = d.IsExpiredAt(DateTime.UtcNow),
                Features = p.HasFeatures ? d.Features : (ulong?)null,
                LicenceId = d.LicenceId,
                Seats = d.Seats,
                MachineCode = d.MachineCode,
                Fields = d.Fields,
            };
        }

        private void RequireLoaded()
        {
            if (_key == null)
                throw new InvalidOperationException("Aucune paire de cles chargee. Generez-en une nouvelle ou chargez un fichier existant.");
        }
    }
}
