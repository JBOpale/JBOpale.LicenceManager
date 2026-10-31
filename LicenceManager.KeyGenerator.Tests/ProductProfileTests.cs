using System;
using System.IO;
using System.Linq;
using LicenceManager.KeyGeneratorApp;
using Xunit;

namespace LicenceManager.KeyGenerator.Tests
{
    public class ProductProfileTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kg-tests-" + Guid.NewGuid().ToString("N"));

        public ProductProfileTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, true);

        private ProductCatalog LoadJson(string json)
        {
            string path = Path.Combine(_dir, "products.json");
            File.WriteAllText(path, json);
            return ProductCatalog.Load(path);
        }

        [Fact]
        public void Fichier_absent_est_initialise_avec_l_exemple_et_relu_a_l_identique()
        {
            string path = Path.Combine(_dir, "absent.json");

            ProductCatalog first = ProductCatalog.Load(path);
            Assert.True(File.Exists(path), "le fichier doit etre cree");
            Assert.DoesNotContain("hasFeatures", File.ReadAllText(path)); // valeur par defaut non ecrite

            ProductCatalog second = ProductCatalog.Load(path);
            Assert.Empty(second.Warnings);
            foreach (ProductCatalog c in new[] { first, second })
            {
                ProductProfile p = Assert.Single(c.Profiles);
                Assert.Equal(ProductProfile.Example().ProductId, p.ProductId);
                Assert.True(p.HasModules);
                Assert.Equal(new[] { "OPTION_A:0", "OPTION_B:1" }, p.Modules.Select(m => m.Name + ":" + m.Bit));
            }
        }

        [Fact]
        public void Profil_json_hexa_seul_et_sans_features()
        {
            ProductCatalog c = LoadJson(@"{ ""products"": [
              { ""productId"": 2000, ""name"": ""Simple"" },
              { ""productId"": 2001, ""name"": ""Mini"", ""hasFeatures"": false }
            ] }");

            Assert.Empty(c.Warnings);
            ProductProfile simple = c.Profiles.Single(p => p.ProductId == 2000);
            Assert.True(simple.HasFeatures);
            Assert.False(simple.HasModules);
            ProductProfile mini = c.Profiles.Single(p => p.ProductId == 2001);
            Assert.False(mini.HasFeatures);
            Assert.False(mini.HasModules);
        }

        [Fact]
        public void Profils_invalides_sont_ignores_avec_avertissement()
        {
            ProductCatalog c = LoadJson(@"{ ""products"": [
              { ""productId"": 3002, ""name"": ""Valide"" },
              { ""productId"": 3002, ""name"": ""Doublon"" },
              { ""productId"": 3000, ""name"": """" },
              { ""productId"": 3001, ""name"": ""Bits en double"", ""modules"": [ { ""name"": ""A"", ""bit"": 1 }, { ""name"": ""B"", ""bit"": 1 } ] }
            ] }");

            Assert.Single(c.Profiles);
            Assert.Equal(3, c.Warnings.Count);
        }

        [Fact]
        public void Fichier_illisible_laisse_seulement_l_exemple()
        {
            ProductCatalog c = LoadJson("{ pas du json");

            Assert.Single(c.Profiles);
            Assert.Single(c.Warnings);
        }

        [Fact]
        public void Inspect_relit_une_cle_et_reconnait_le_profil()
        {
            ProductCatalog c = LoadJson(@"{ ""products"": [ { ""productId"": 2000, ""name"": ""Simple"" } ] }");
            c.Profiles.Insert(0, ProductProfile.Example());
            var service = new LicenceKeyService();
            service.CreateNewKeyPair(); // paire ephemere en memoire

            service.Profile = c.Profiles[1];
            string key = service.GenerateKey("ACME", 2000, new DateTime(2099, 1, 1), 0x7, seats: 5);
            KeyInspection r = service.Inspect(key, c.Profiles, out string error);

            Assert.Null(error);
            Assert.Equal(2000, r.ProductId);
            Assert.Equal("ACME", r.Client);
            Assert.Equal(0x7UL, r.Features);
            Assert.Equal(2099, r.Expiration.Value.Year);
            Assert.False(r.Expired);
            Assert.Equal((ushort)5, r.Seats);
            Assert.NotEqual(0UL, r.LicenceId);

            // Meme service, autre produit : le profil courant n'est pas modifie par Inspect.
            service.Profile = c.Profiles[0];
            string other = service.GenerateKey("X", 1000, null, FeatureMask.AllFeatures);
            service.Profile = c.Profiles[1];
            KeyInspection r2 = service.Inspect(other, c.Profiles, out error);
            Assert.Equal(1000, r2.ProductId);
            Assert.Null(r2.Expiration);
            Assert.Equal(FeatureMask.AllFeatures, r2.Features);
            Assert.Equal(2000, service.Profile.ProductId);
        }

        [Fact]
        public void Inspect_rejette_une_cle_alteree_ou_d_une_autre_paire()
        {
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();
            var profiles = new[] { ProductProfile.Example() };
            string key = service.GenerateKey("X", 1000, null, 1);

            char[] chars = key.ToCharArray();
            int i = chars.Length / 2;
            chars[i] = chars[i] == 'A' ? 'B' : 'A';
            Assert.Null(service.Inspect(new string(chars), profiles, out string e1));
            Assert.NotNull(e1);

            var other = new LicenceKeyService();
            other.CreateNewKeyPair();
            Assert.Null(other.Inspect(key, profiles, out string e2));
            Assert.NotNull(e2);

            Assert.Null(service.Inspect("  ", profiles, out string e3));
        }

        [Fact]
        public void Cles_aller_retour_par_profil_avec_la_meme_paire()
        {
            ProductCatalog c = LoadJson(@"{ ""products"": [
              { ""productId"": 2000, ""name"": ""Simple"" },
              { ""productId"": 2001, ""name"": ""Mini"", ""hasFeatures"": false }
            ] }");

            // Paire de cles EPHEMERE en memoire (jamais la cle de signature reelle).
            var service = new LicenceKeyService();
            service.CreateNewKeyPair();

            foreach (ProductProfile p in c.Profiles)
            {
                service.Profile = p;
                string key = service.GenerateKey("Client", p.ProductId, null, FeatureMask.AllFeatures);

                Assert.True(service.SelfCheck(key, out string details), p.Name + " : " + details);
                Assert.Contains("ProductId=" + p.ProductId, details);
                KeyInspection r = service.Inspect(key, c.Profiles, out string error);
                Assert.Null(error);
                // Profil sans Features : le masque saisi est ignore (0 dans la cle).
                Assert.Equal(p.HasFeatures ? FeatureMask.AllFeatures : (ulong?)null, r.Features);
            }
        }

        [Theory]
        [InlineData(0x0UL)]
        [InlineData(0xBUL)]
        [InlineData(0xFFFFFFFFFFFFFFFFUL)]
        public void Masque_hexadecimal_aller_retour_non_signe(ulong mask)
        {
            Assert.True(FeatureMask.TryParseHex(FeatureMask.FormatHex(mask), out ulong parsed));
            Assert.Equal(mask, parsed);
            Assert.Equal(mask.ToString(), FeatureMask.FormatDecimal(mask));
        }

        [Theory]
        [InlineData("")]
        [InlineData("0x")]
        [InlineData("0xZZ")]
        [InlineData("0x1FFFFFFFFFFFFFFFF")] // 17 chiffres : depasse 64 bits
        [InlineData("-1")]
        public void Hexadecimal_invalide_est_rejete(string text)
        {
            Assert.False(FeatureMask.TryParseHex(text, out _));
        }
    }
}
