using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace LicenceManager.KeyGeneratorApp
{
    public enum ProductFieldType
    {
        Int,
        Bool,
        Date,
        Text,
        Mask,
    }

    // Champ personnalise d'un produit (FORMAT.md section 6) : le numero est stocke dans la cle,
    // le nom et le type ne servent qu'a la saisie et a l'affichage. Comme pour les modules, ne
    // jamais reaffecter un numero deja utilise.
    public sealed class ProductField
    {
        public int Id { get; set; }
        public string Name { get; set; }

        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public ProductFieldType Type { get; set; }

        public override string ToString() => $"{Name} ({Id})";

        // Taille minimale dans la cle (texte vide compris), pour verifier qu'un profil tient
        // dans les 64 octets.
        [JsonIgnore]
        public int MinEncodedSize => 2 + (Type == ProductFieldType.Int ? 4 : Type == ProductFieldType.Bool ? 1
            : Type == ProductFieldType.Date ? 2 : Type == ProductFieldType.Mask ? 8 : 0);

        // Format de saisie : int "123", bool "true"/"false", date "yyyy-MM-dd", text libre,
        // mask hexadecimal (avec ou sans 0x). Une saisie vide n'ecrit rien : champ absent de la cle
        // (sauf bool, toujours ecrit).
        public bool TryApply(LicenceFields fields, string input, out string error)
        {
            error = null;
            byte id = (byte)Id;
            string text = (input ?? string.Empty).Trim();
            if (text.Length == 0 && Type != ProductFieldType.Bool && Type != ProductFieldType.Text)
            {
                fields.Remove(id);
                return true;
            }

            switch (Type)
            {
                case ProductFieldType.Int:
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                        return Fail($"{Name} : entier attendu", out error);
                    fields.SetInt(id, n);
                    return true;

                case ProductFieldType.Bool:
                    fields.SetBool(id, string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));
                    return true;

                case ProductFieldType.Date:
                    if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                        return Fail($"{Name} : date attendue (aaaa-mm-jj)", out error);
                    try { fields.SetDate(id, d); }
                    catch (ArgumentOutOfRangeException) { return Fail($"{Name} : date hors de la plage 2000 - 2179", out error); }
                    return true;

                case ProductFieldType.Text:
                    if (input == null || input.Length == 0)
                    {
                        fields.Remove(id);
                        return true;
                    }
                    try { fields.SetText(id, input); }
                    catch (ArgumentException) { return Fail($"{Name} : {LicenceFields.TextMaxBytes} octets UTF-8 au maximum", out error); }
                    return true;

                case ProductFieldType.Mask:
                    if (!FeatureMask.TryParseHex(text, out ulong mask))
                        return Fail($"{Name} : masque hexadecimal 64 bits attendu", out error);
                    fields.SetMask(id, mask);
                    return true;
            }
            return Fail($"{Name} : type inconnu", out error);
        }

        // Valeur lisible pour l'onglet de test ; "(absent)" ou "(type incorrect)" sinon.
        public string Describe(LicenceFields fields)
        {
            byte id = (byte)Id;
            if (!fields.Contains(id)) return "(absent)";
            switch (Type)
            {
                case ProductFieldType.Int:
                    if (fields.TryGetInt(id, out int n)) return n.ToString(CultureInfo.InvariantCulture);
                    break;
                case ProductFieldType.Bool:
                    if (fields.TryGetBool(id, out bool b)) return b ? "oui" : "non";
                    break;
                case ProductFieldType.Date:
                    if (fields.TryGetDate(id, out DateTime d)) return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    break;
                case ProductFieldType.Text:
                    if (fields.TryGetText(id, out string t)) return "\"" + t + "\"";
                    break;
                case ProductFieldType.Mask:
                    if (fields.TryGetMask(id, out ulong m)) return FeatureMask.FormatHex(m);
                    break;
            }
            return "(type incorrect)";
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
