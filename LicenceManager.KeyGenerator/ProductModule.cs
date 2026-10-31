using System;
using System.Globalization;

namespace LicenceManager.KeyGeneratorApp
{
    // Module d'un produit : un bit du masque Features. Ne jamais reaffecter un bit deja utilise
    // par des cles emises ; seulement en ajouter.
    public sealed class ProductModule
    {
        public ProductModule(string name, int bit)
        {
            Name = name;
            Bit = bit;
        }

        public string Name { get; }
        public int Bit { get; }
        [Newtonsoft.Json.JsonIgnore] public ulong Mask => 1UL << Bit;
    }

    // Saisie et affichage du masque Features (uint64 non signe).
    public static class FeatureMask
    {
        // "Toutes options" : ~0, couvre aussi les modules futurs.
        public const ulong AllFeatures = 0xFFFFFFFFFFFFFFFFUL;

        // Saisie hexadecimale (avec ou sans prefixe 0x).
        public static bool TryParseHex(string text, out ulong features)
        {
            features = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            if (t.Length == 0 || t.Length > 16) return false;
            return ulong.TryParse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out features);
        }

        public static string FormatHex(ulong features) => "0x" + features.ToString("X16", CultureInfo.InvariantCulture);

        public static string FormatDecimal(ulong features) => features.ToString(CultureInfo.InvariantCulture);
    }
}
