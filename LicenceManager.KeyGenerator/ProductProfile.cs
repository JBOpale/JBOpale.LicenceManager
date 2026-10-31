using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace LicenceManager.KeyGeneratorApp
{
    // Profil produit : identifiant, nom, modules et champs personnalises (facultatifs). Le format
    // de cle est commun a tous les produits (voir FORMAT.md) : le profil decide de la saisie des
    // Features et des informations propres au produit (fields).
    //  - Modules renseignes -> cases a cocher.
    //  - Pas de modules -> saisie du masque hexadecimal seule.
    //  - hasFeatures = false -> aucune zone Features (masque toujours a 0).
    public sealed class ProductProfile
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public List<ProductModule> Modules { get; set; }
        public List<ProductField> Fields { get; set; }

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasFeatures { get; set; } = true;

        [JsonIgnore] public bool HasModules => HasFeatures && Modules != null && Modules.Count > 0;
        [JsonIgnore] public bool HasFields => Fields != null && Fields.Count > 0;

        public override string ToString() => $"{Name} ({ProductId})";

        public string Validate()
        {
            if (ProductId < 0) return "ProductId negatif";
            if (string.IsNullOrWhiteSpace(Name)) return "nom manquant";
            if (Modules != null)
            {
                if (Modules.Any(m => m.Bit < 0 || m.Bit > 63)) return "bit de module hors 0..63";
                if (Modules.GroupBy(m => m.Bit).Any(g => g.Count() > 1)) return "bit de module en double";
                if (Modules.GroupBy(m => m.Name).Any(g => g.Count() > 1)) return "nom de module en double";
            }
            if (Fields != null)
            {
                if (Fields.Any(f => f == null || string.IsNullOrWhiteSpace(f.Name))) return "champ sans nom";
                if (Fields.Any(f => f.Id < 1 || f.Id > 255)) return "numero de champ hors 1..255";
                if (Fields.GroupBy(f => f.Id).Any(g => g.Count() > 1)) return "numero de champ en double";
                if (Fields.GroupBy(f => f.Name).Any(g => g.Count() > 1)) return "nom de champ en double";
                if (Fields.Any(f => !Enum.IsDefined(typeof(ProductFieldType), f.Type))) return "type de champ inconnu";
                int size = Fields.Sum(f => f.MinEncodedSize);
                if (size > LicenceFields.MaxTotalBytes)
                    return $"champs trop volumineux : {size} octets au minimum, {LicenceFields.MaxTotalBytes} au maximum";
            }
            return null;
        }

        // Produit d'exemple ecrit dans un products.json neuf : a remplacer par ses propres produits.
        public static ProductProfile Example() => new ProductProfile
        {
            ProductId = 1000,
            Name = "Exemple",
            Modules = new List<ProductModule> { new ProductModule("OPTION_A", 0), new ProductModule("OPTION_B", 1) },
        };
    }
}
