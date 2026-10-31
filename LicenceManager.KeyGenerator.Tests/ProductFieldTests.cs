using System;
using System.Linq;
using LicenceManager.KeyGeneratorApp;
using Xunit;

namespace LicenceManager.KeyGenerator.Tests
{
    public class ProductFieldTests
    {
        private static ProductField F(int id, ProductFieldType type, string name = null) =>
            new ProductField { Id = id, Name = name ?? "Champ" + id, Type = type };

        [Fact]
        public void Saisies_converties_selon_le_type()
        {
            var fields = new LicenceFields();
            string error;
            Assert.True(F(1, ProductFieldType.Int).TryApply(fields, " -42 ", out error), error);
            Assert.True(F(2, ProductFieldType.Bool).TryApply(fields, "true", out error), error);
            Assert.True(F(3, ProductFieldType.Date).TryApply(fields, "2028-06-30", out error), error);
            Assert.True(F(4, ProductFieldType.Text).TryApply(fields, "Pro", out error), error);
            Assert.True(F(5, ProductFieldType.Mask).TryApply(fields, "0xB", out error), error);

            Assert.Equal("-42", F(1, ProductFieldType.Int).Describe(fields));
            Assert.Equal("oui", F(2, ProductFieldType.Bool).Describe(fields));
            Assert.Equal("2028-06-30", F(3, ProductFieldType.Date).Describe(fields));
            Assert.Equal("\"Pro\"", F(4, ProductFieldType.Text).Describe(fields));
            Assert.Equal("0x000000000000000B", F(5, ProductFieldType.Mask).Describe(fields));
        }

        [Fact]
        public void Saisie_vide_n_inclut_pas_le_champ_sauf_bool()
        {
            var fields = new LicenceFields();
            foreach (var type in new[] { ProductFieldType.Int, ProductFieldType.Date, ProductFieldType.Text, ProductFieldType.Mask })
                Assert.True(F((int)type + 1, type).TryApply(fields, "", out _));
            Assert.True(F(9, ProductFieldType.Bool).TryApply(fields, "false", out _));

            Assert.Equal(new byte[] { 9 }, fields.Ids.ToArray());
            Assert.Equal("(absent)", F(1, ProductFieldType.Int).Describe(fields));
            Assert.Equal("non", F(9, ProductFieldType.Bool).Describe(fields));
        }

        [Theory]
        [InlineData(ProductFieldType.Int, "12a")]
        [InlineData(ProductFieldType.Int, "99999999999")]
        [InlineData(ProductFieldType.Date, "30/06/2028")]
        [InlineData(ProductFieldType.Date, "2190-01-01")]
        [InlineData(ProductFieldType.Mask, "0xZZ")]
        [InlineData(ProductFieldType.Text, "texte beaucoup trop long pour tenir dans 32 octets")]
        public void Saisie_invalide_donne_un_message_avec_le_nom(ProductFieldType type, string input)
        {
            Assert.False(F(1, type, "Edition").TryApply(new LicenceFields(), input, out string error));
            Assert.StartsWith("Edition", error);
        }

        [Fact]
        public void Type_incorrect_signale_a_la_relecture()
        {
            var fields = new LicenceFields();
            fields.SetText(1, "abc");
            Assert.Equal("(type incorrect)", F(1, ProductFieldType.Int).Describe(fields));
        }

        [Fact]
        public void Validation_du_profil()
        {
            ProductProfile P(params ProductField[] fields) => new ProductProfile { ProductId = 1, Name = "P", Fields = fields.ToList() };

            Assert.Null(P(F(1, ProductFieldType.Int), F(255, ProductFieldType.Text)).Validate());
            Assert.Contains("1..255", P(F(0, ProductFieldType.Int)).Validate());
            Assert.Contains("1..255", P(F(256, ProductFieldType.Int)).Validate());
            Assert.Contains("numero de champ en double", P(F(1, ProductFieldType.Int, "A"), F(1, ProductFieldType.Bool, "B")).Validate());
            Assert.Contains("nom de champ en double", P(F(1, ProductFieldType.Int, "A"), F(2, ProductFieldType.Bool, "A")).Validate());
            Assert.Contains("sans nom", P(F(1, ProductFieldType.Int, " ")).Validate());
            // 7 masques = 7 x 10 octets > 64
            Assert.Contains("trop volumineux", P(Enumerable.Range(1, 7).Select(i => F(i, ProductFieldType.Mask)).ToArray()).Validate());
        }

        [Fact]
        public void Profil_json_avec_champs_et_cle_aller_retour()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kg-fields-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string path = System.IO.Path.Combine(dir, "products.json");
                System.IO.File.WriteAllText(path, @"{ ""products"": [
                  { ""productId"": 2001, ""name"": ""Produit X"", ""fields"": [
                      { ""id"": 1, ""name"": ""Edition"", ""type"": ""text"" },
                      { ""id"": 2, ""name"": ""UtilisateursMax"", ""type"": ""int"" },
                      { ""id"": 3, ""name"": ""Support"", ""type"": ""date"" },
                      { ""id"": 4, ""name"": ""ModeDemo"", ""type"": ""bool"" } ] }
                ] }");

                ProductCatalog c = ProductCatalog.Load(path);
                Assert.Empty(c.Warnings);
                ProductProfile p = c.Profiles.Single();
                Assert.Equal(ProductFieldType.Date, p.Fields.Single(f => f.Id == 3).Type);

                var fields = new LicenceFields();
                Assert.True(p.Fields[0].TryApply(fields, "Pro", out _));
                Assert.True(p.Fields[1].TryApply(fields, "25", out _));

                var service = new LicenceKeyService { Profile = p };
                service.CreateNewKeyPair();
                string key = service.GenerateKey("ACME", 2001, null, 0, fields: fields);
                KeyInspection r = service.Inspect(key, c.Profiles, out string error);

                Assert.Null(error);
                Assert.Equal("\"Pro\"", p.Fields[0].Describe(r.Fields));
                Assert.Equal("25", p.Fields[1].Describe(r.Fields));
                Assert.Equal("(absent)", p.Fields[2].Describe(r.Fields));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
