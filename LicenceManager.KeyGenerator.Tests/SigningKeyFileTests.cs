using System;
using System.IO;
using LicenceManager.KeyGeneratorApp;
using Xunit;

namespace LicenceManager.KeyGenerator.Tests
{
    // Paires de cles EPHEMERES ecrites dans un dossier temporaire (jamais la cle reelle).
    public class SigningKeyFileTests : IDisposable
    {
        private const string Passphrase = "phrase de passe de test";
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kg-keys-" + Guid.NewGuid().ToString("N"));

        public SigningKeyFileTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, true);

        private string PathOf(string name) => Path.Combine(_dir, name);

        [Fact]
        public void Sauvegarde_par_phrase_de_passe_aller_retour()
        {
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();
            string key = service.GenerateKey("ACME", 1000, null, 0x3);
            service.ExportBackup(PathOf("backup.json"), Passphrase);

            var restored = new LicenceKeyService();
            Assert.True(restored.LoadKeyFile(PathOf("backup.json"), () => Passphrase));

            Assert.Equal(service.PublicKeyBase64, restored.PublicKeyBase64);
            Assert.Equal(service.Fingerprint, restored.Fingerprint);
            Assert.NotNull(restored.Inspect(key, new[] { ProductProfile.Example() }, out _));
            Assert.True(restored.SelfCheck(restored.GenerateKey("X", 1000, null, 0), out string details), details);
        }

        [Fact]
        public void Fichier_ne_contient_pas_la_cle_privee_en_clair()
        {
            LicenceSigningKey key = LicenceSigningKey.Create();
            SigningKeyFile.SaveWithPassphrase(PathOf("backup.json"), key, Passphrase);

            string text = File.ReadAllText(PathOf("backup.json"));
            Assert.DoesNotContain(Convert.ToBase64String(key.ExportPrivateKey()), text);
            Assert.Contains(key.PublicKey.ToBase64(), text);
            Assert.Contains(key.PublicKey.Fingerprint, text);
        }

        [Fact]
        public void Mauvaise_phrase_de_passe_ou_annulation()
        {
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();
            service.ExportBackup(PathOf("backup.json"), Passphrase);

            var other = new LicenceKeyService();
            var ex = Assert.Throws<InvalidOperationException>(() => other.LoadKeyFile(PathOf("backup.json"), () => "mauvaise phrase de passe"));
            Assert.Contains("incorrecte", ex.Message);
            Assert.False(other.IsLoaded);

            Assert.False(other.LoadKeyFile(PathOf("backup.json"), () => null));
            Assert.False(other.IsLoaded);
        }

        [Fact]
        public void Fichier_modifie_est_refuse()
        {
            LicenceSigningKey key = LicenceSigningKey.Create();
            SigningKeyFile.SaveWithPassphrase(PathOf("backup.json"), key, Passphrase);
            string other = LicenceSigningKey.Create().PublicKey.ToBase64();
            File.WriteAllText(PathOf("backup.json"), File.ReadAllText(PathOf("backup.json")).Replace(key.PublicKey.ToBase64(), other));

            Assert.Throws<InvalidOperationException>(() => SigningKeyFile.Load(PathOf("backup.json"), Passphrase));
        }

        [Fact]
        public void Phrase_de_passe_trop_courte_est_refusee()
        {
            Assert.Throws<ArgumentException>(() =>
                SigningKeyFile.SaveWithPassphrase(PathOf("b.json"), LicenceSigningKey.Create(), "court"));
        }

        [Fact]
        public void Fichier_d_un_autre_format_est_refuse_avec_un_message_clair()
        {
            File.WriteAllText(PathOf("other.xml"), "<KeyTemplate><PrivateKey>abc</PrivateKey></KeyTemplate>");

            var ex = Assert.Throws<InvalidOperationException>(() => new LicenceKeyService().LoadKeyFile(PathOf("other.xml"), () => Passphrase));
            Assert.Contains(SigningKeyFile.FormatName, ex.Message);
        }

        [Fact]
        public void Fichier_de_travail_DPAPI_aller_retour()
        {
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();
            service.SaveKeyFile(PathOf("work.json"));

            Assert.Equal(SigningKeyFile.Dpapi, SigningKeyFile.ReadProtection(PathOf("work.json")));
            var reloaded = new LicenceKeyService();
            Assert.True(reloaded.LoadKeyFile(PathOf("work.json"), () => throw new InvalidOperationException("pas de phrase de passe attendue")));
            Assert.Equal(service.PublicKeyBase64, reloaded.PublicKeyBase64);
            Assert.Equal(PathOf("work.json"), reloaded.LoadedPath);
        }

        [Fact]
        public void Licence_liee_a_un_poste_est_relue_avec_son_code()
        {
            const string poste = "WSZ6-G7W2-KQCT-CD3Q"; // code valide quelconque (vecteur de FORMAT.md section 5)
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();

            string bound = service.GenerateKey("X", 1000, null, 0, machineCode: poste.ToLowerInvariant());
            KeyInspection r = service.Inspect(bound, new[] { ProductProfile.Example() }, out string error);
            Assert.Null(error);
            Assert.Equal(poste, r.MachineCode);
            Assert.True(service.SelfCheck(bound, out string details));
            Assert.Contains("Poste=" + poste, details);

            KeyInspection free = service.Inspect(service.GenerateKey("X", 1000, null, 0), new[] { ProductProfile.Example() }, out _);
            Assert.Null(free.MachineCode);

            string typo = (poste[0] == 'A' ? "B" : "A") + poste.Substring(1);
            Assert.Throws<ArgumentException>(() => service.GenerateKey("X", 1000, null, 0, machineCode: typo));
        }

        [Fact]
        public void Inspect_signale_un_produit_inconnu()
        {
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();
            string key = service.GenerateKey("X", 4242, null, 0);

            Assert.Null(service.Inspect(key, new[] { ProductProfile.Example() }, out string error));
            Assert.Contains("4242", error);
        }
    }
}
