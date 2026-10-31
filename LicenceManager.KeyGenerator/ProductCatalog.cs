using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LicenceManager.KeyGeneratorApp
{
    // Liste des profils produit, lue dans products.json (voir PRODUCTS.md). Au premier lancement
    // (fichier absent ou sans produit), le fichier est initialise avec un produit d'exemple.
    public sealed class ProductCatalog
    {
        private sealed class FileModel
        {
            public List<ProductProfile> Products { get; set; } = new List<ProductProfile>();
        }

        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public List<ProductProfile> Profiles { get; } = new List<ProductProfile>();

        // Problemes rencontres au chargement (profils ignores, fichier illisible...).
        public List<string> Warnings { get; } = new List<string>();

        public static string DefaultFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LicenceManager", "KeyGenerator", "products.json");

        public static ProductCatalog Load(string path)
        {
            var catalog = new ProductCatalog();

            FileModel model = null;
            if (File.Exists(path))
            {
                try
                {
                    model = JsonConvert.DeserializeObject<FileModel>(File.ReadAllText(path)) ?? new FileModel();
                }
                catch (Exception ex)
                {
                    catalog.Warnings.Add("products.json illisible (ignore, fichier non modifie) : " + ex.Message);
                    catalog.Profiles.Add(ProductProfile.Example());
                    return catalog;
                }
            }

            if (model == null || model.Products == null || model.Products.Count == 0)
            {
                model = new FileModel { Products = { ProductProfile.Example() } };
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, JsonConvert.SerializeObject(model, WriteSettings) + "\r\n", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    catalog.Warnings.Add("Impossible d'ecrire " + path + " : " + ex.Message);
                }
            }

            foreach (ProductProfile p in model.Products)
            {
                string label = p == null ? "(vide)" : $"{p.Name} ({p.ProductId})";
                string err = p == null ? "entree vide" : p.Validate();
                if (err == null && catalog.Profiles.Any(x => x.ProductId == p.ProductId))
                    err = "ProductId deja utilise par un autre profil";
                if (err != null)
                {
                    catalog.Warnings.Add($"Profil {label} ignore : {err}");
                    continue;
                }
                catalog.Profiles.Add(p);
            }

            if (catalog.Profiles.Count == 0)
            {
                catalog.Warnings.Add("Aucun profil valide : produit d'exemple utilise (fichier non modifie).");
                catalog.Profiles.Add(ProductProfile.Example());
            }
            return catalog;
        }
    }
}
