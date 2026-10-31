using System;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace LicenceManager.Tests
{
    public class LicenceKeyTests
    {
        // Une seule paire ephemere pour toute la classe (jamais la cle de signature reelle).
        private static readonly LicenceSigningKey Key = LicenceSigningKey.Create();
        private static readonly LicenceIssuer Issuer = new LicenceIssuer(Key);
        private static readonly LicenceValidator Validator = new LicenceValidator(Key.PublicKey);
        private static readonly DateTime Today = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        private static LicenceData Sample() => new LicenceData
        {
            ProductId = 1000,
            Client = "Société Dupont & Fils",
            Expiration = new DateTime(2027, 12, 31),
            Features = 0x8000_0000_00FF_FFFFUL,
            Seats = 3,
        };

        [Fact]
        public void Aller_retour_de_tous_les_champs()
        {
            LicenceData data = Sample();
            string key = Issuer.Issue(data);

            LicenceCheck check = Validator.Validate(key, 1000, Today);

            Assert.Equal(LicenceStatus.Valid, check.Status);
            Assert.True(check.IsValid);
            Assert.Equal(1000, check.Data.ProductId);
            Assert.Equal("Société Dupont & Fils", check.Data.Client);
            Assert.Equal(new DateTime(2027, 12, 31), check.Data.Expiration);
            Assert.Equal(0x8000_0000_00FF_FFFFUL, check.Data.Features);
            Assert.Equal((ushort)3, check.Data.Seats);
            Assert.NotEqual(0UL, check.Data.LicenceId);
            Assert.Equal(data.LicenceId, check.Data.LicenceId);
        }

        [Fact]
        public void Licence_sans_expiration_et_valeurs_extremes()
        {
            string key = Issuer.Issue(new LicenceData
            {
                ProductId = int.MaxValue,
                Client = "",
                Expiration = null,
                Features = ulong.MaxValue,
                LicenceId = ulong.MaxValue,
                Seats = ushort.MaxValue,
            });

            LicenceCheck check = Validator.Validate(key, int.MaxValue, new DateTime(2179, 1, 1));

            Assert.True(check.IsValid);
            Assert.Null(check.Data.Expiration);
            Assert.Equal("", check.Data.Client);
            Assert.Equal(ulong.MaxValue, check.Data.Features);
            Assert.Equal(ulong.MaxValue, check.Data.LicenceId);
            Assert.Equal(ushort.MaxValue, check.Data.Seats);
        }

        [Fact]
        public void Valide_jusqu_au_dernier_jour_inclus()
        {
            string key = Issuer.Issue(new LicenceData { ProductId = 1, Expiration = new DateTime(2026, 10, 30) });

            Assert.Equal(LicenceStatus.Valid, Validator.Validate(key, 1, new DateTime(2026, 10, 30, 23, 59, 59)).Status);
            LicenceCheck expired = Validator.Validate(key, 1, new DateTime(2026, 10, 31));
            Assert.Equal(LicenceStatus.Expired, expired.Status);
            Assert.NotNull(expired.Data);
        }

        [Fact]
        public void Autre_produit_est_refuse()
        {
            string key = Issuer.Issue(Sample());

            LicenceCheck check = Validator.Validate(key, 2000, Today);

            Assert.Equal(LicenceStatus.WrongProduct, check.Status);
            Assert.Equal(1000, check.Data.ProductId);
        }

        [Fact]
        public void Toute_modification_d_un_caractere_est_refusee()
        {
            string key = Issuer.Issue(Sample());

            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] == '-') continue;
                char replacement = key[i] == 'A' ? 'B' : 'A';
                string altered = key.Substring(0, i) + replacement + key.Substring(i + 1);
                LicenceStatus status = Validator.Validate(altered, 1000, Today).Status;
                Assert.True(status == LicenceStatus.Malformed || status == LicenceStatus.InvalidSignature,
                    $"position {i} : {status}");
            }
        }

        [Fact]
        public void Champ_modifie_avec_crc_recalcule_est_refuse_par_la_signature()
        {
            // Simule un faussaire qui connait le format : il change le masque et recalcule le CRC.
            Assert.True(LicenceKeyCodec.TryDecode(Issuer.Issue(Sample()), out byte[] raw));
            raw[7] ^= 0x01;

            Assert.Equal(LicenceStatus.InvalidSignature, Validator.Validate(LicenceKeyCodec.Encode(raw), 1000, Today).Status);
        }

        [Fact]
        public void Cle_emise_avec_une_autre_paire_est_refusee()
        {
            string key = new LicenceIssuer(LicenceSigningKey.Create()).Issue(Sample());

            LicenceCheck check = Validator.Validate(key, 1000, Today);

            Assert.Equal(LicenceStatus.InvalidSignature, check.Status);
            Assert.Null(check.Data);
        }

        [Fact]
        public void Saisie_tolerante_minuscules_espaces_et_caracteres_ambigus()
        {
            string key = Issuer.Issue(Sample());
            string typed = "  " + key.Replace("-", " ").Replace('0', 'O').Replace('1', 'l').ToLowerInvariant() + "\r\n";

            Assert.True(Validator.Validate(typed, 1000, Today).IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ABC")]
        [InlineData("UUUU-UUUU")]
        public void Texte_illisible_est_mal_forme(string text)
        {
            Assert.Equal(LicenceStatus.Malformed, Validator.Validate(text, 1000, Today).Status);
            Assert.Equal(LicenceStatus.Malformed, Validator.Validate(null, 1000, Today).Status);
        }

        [Fact]
        public void Cle_tronquee_ou_rallongee_est_mal_formee()
        {
            string key = Issuer.Issue(Sample());

            Assert.Equal(LicenceStatus.Malformed, Validator.Validate(key.Substring(0, key.Length - 1), 1000, Today).Status);
            Assert.Equal(LicenceStatus.Malformed, Validator.Validate(key + "0", 1000, Today).Status);
        }

        [Fact]
        public void Version_de_format_inconnue_est_signalee()
        {
            Assert.True(LicenceKeyCodec.TryDecode(Issuer.Issue(Sample()), out byte[] raw));
            raw[0] = 2;

            Assert.Equal(LicenceStatus.UnsupportedVersion, Validator.Validate(LicenceKeyCodec.Encode(raw), 1000, Today).Status);
        }

        [Fact]
        public void Longueur_client_incoherente_est_mal_formee()
        {
            Assert.True(LicenceKeyCodec.TryDecode(Issuer.Issue(Sample()), out byte[] raw));
            raw[33] += 1;

            Assert.Equal(LicenceStatus.Malformed, Validator.Validate(LicenceKeyCodec.Encode(raw), 1000, Today).Status);
        }

        [Fact]
        public void Nom_client_limite_a_40_octets_utf8()
        {
            string forty = new string('é', 20); // 2 octets chacun
            Assert.True(Validator.Validate(Issuer.Issue(new LicenceData { ProductId = 1, Client = forty }), 1, Today).IsValid);

            Assert.Throws<ArgumentException>(() => Issuer.Issue(new LicenceData { ProductId = 1, Client = forty + "x" }));
        }

        [Fact]
        public void Date_hors_plage_est_refusee_a_l_emission()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Issuer.Issue(new LicenceData { Expiration = new DateTime(2000, 1, 1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Issuer.Issue(new LicenceData { Expiration = new DateTime(2180, 1, 1) }));
        }

        [Fact]
        public void Identifiants_de_licence_uniques()
        {
            var ids = Enumerable.Range(0, 50).Select(_ =>
            {
                var d = Sample();
                Issuer.Issue(d);
                return d.LicenceId;
            }).ToList();

            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.DoesNotContain(0UL, ids);
        }

        [Fact]
        public void Format_de_la_cle_groupes_de_6_et_longueur()
        {
            string key = Issuer.Issue(new LicenceData { ProductId = 1000, Client = new string('x', 20) });
            string[] groups = key.Split('-');

            Assert.All(groups.Take(groups.Length - 1), g => Assert.Equal(6, g.Length));
            // 35 + 20 octets de donnees + 64 de signature + 2 de CRC = 121 octets = 968 bits -> 194 caracteres
            Assert.Equal(194, key.Replace("-", "").Length);
        }

        [Fact]
        public void Cle_publique_base64_aller_retour_et_empreinte()
        {
            string b64 = Key.PublicKey.ToBase64();
            LicencePublicKey reloaded = LicencePublicKey.FromBase64(b64);

            Assert.Equal(88, b64.Length);
            Assert.Equal(b64, reloaded.ToBase64());
            Assert.Equal(Key.PublicKey.Fingerprint, reloaded.Fingerprint);
            Assert.Equal(16, reloaded.Fingerprint.Length);
            Assert.True(new LicenceValidator(b64).Validate(Issuer.Issue(Sample()), 1000, Today).IsValid);
        }

        [Fact]
        public void Cle_publique_invalide_est_refusee()
        {
            Assert.Throws<ArgumentException>(() => LicencePublicKey.FromBase64("pas du base64 !"));
            Assert.Throws<ArgumentException>(() => LicencePublicKey.FromBase64(Convert.ToBase64String(new byte[63])));
            byte[] offCurve = new byte[64];
            offCurve[31] = 1;
            offCurve[63] = 1;
            Assert.Throws<ArgumentException>(() => LicencePublicKey.FromBase64(Convert.ToBase64String(offCurve)));
        }

        [Fact]
        public void Cle_privee_export_import()
        {
            byte[] exported = Key.ExportPrivateKey();
            LicenceSigningKey reloaded = LicenceSigningKey.ImportPrivateKey(exported);

            Assert.Equal(Key.PublicKey.ToBase64(), reloaded.PublicKey.ToBase64());
            Assert.True(Validator.Validate(new LicenceIssuer(reloaded).Issue(Sample()), 1000, Today).IsValid);
        }

        [Fact]
        public void Cle_privee_incoherente_est_refusee()
        {
            byte[] mixed = Key.ExportPrivateKey();
            Buffer.BlockCopy(LicenceSigningKey.Create().ExportPrivateKey(), 32, mixed, 32, 64);

            Assert.ThrowsAny<CryptographicException>(() => LicenceSigningKey.ImportPrivateKey(mixed));
            Assert.Throws<ArgumentException>(() => LicenceSigningKey.ImportPrivateKey(new byte[10]));
        }

        [Fact]
        public void Crc16_ccitt_false_valeur_de_reference()
        {
            byte[] check = System.Text.Encoding.ASCII.GetBytes("123456789");
            Assert.Equal(0x29B1, LicenceKeyCodec.Crc16(check, check.Length));
        }
    }
}
