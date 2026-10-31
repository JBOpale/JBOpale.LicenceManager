using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LicenceManager;

// Genere LicenceManagerCpp/test/test_vectors.h : des cles emises par la VRAIE bibliotheque .NET
// avec une paire de cles JETABLE (jamais la cle de production), que le test C++ doit lire a
// l'identique. Chaque vecteur est d'abord revalide ici par LicenceValidator (.NET) : le fichier
// n'est ecrit que si .NET et le statut attendu concordent.
//
//   dotnet run --project LicenceManagerCpp/tools/vectorgen -- LicenceManagerCpp/test/test_vectors.h
//   dotnet run --project LicenceManagerCpp/tools/vectorgen -- --machine-code   (code du poste courant)
internal static class Program
{
    private sealed class Vector
    {
        public string Name;
        public string Key;
        public int ProductId;
        public DateTime Now;
        public string CurrentMachine; // code du poste suppose courant ("" = non identifiable)
        public LicenceStatus Status;
        public LicenceData Data; // contenu attendu si la signature est valide
    }

    private static readonly DateTime Today = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    // Echantillon SMBIOS : structure de type 0 avec chaines, puis type 1 (UUID), puis fin.
    private static readonly byte[] SmbiosSample = BuildSmbiosSample();
    private const string MachineGuidSample = " 4C4C4544-0042-3510-8051-B4C04F4E4D32 ";

    private static byte[] BuildSmbiosSample()
    {
        var table = new List<byte> { 0, 0x18, 0, 0 };
        table.AddRange(new byte[0x18 - 4]);
        table.AddRange(Encoding.ASCII.GetBytes("Vendor\0Version\0\0"));
        var system = new byte[0x1B];
        system[0] = 1;
        system[1] = 0x1B;
        for (int i = 0; i < 16; i++) system[8 + i] = (byte)(0xA0 + i);
        table.AddRange(system);
        table.AddRange(new byte[] { 0, 0, 127, 4, 0, 0, 0, 0 });

        var raw = new List<byte> { 0, 3, 4, 0 };
        raw.AddRange(BitConverter.GetBytes((uint)table.Count));
        raw.AddRange(table);
        return raw.ToArray();
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--machine-code")
        {
            Console.WriteLine(MachineCode.Current ?? "");
            return 0;
        }
        string output = args.Length > 0 ? args[0] : "test_vectors.h";

        LicenceSigningKey key = LicenceSigningKey.Create();
        LicenceSigningKey otherKey = LicenceSigningKey.Create();
        var issuer = new LicenceIssuer(key);
        var vectors = new List<Vector>();

        var full = new LicenceData
        {
            ProductId = 1000,
            Client = "Société Dupont & Fils \"Ñ\"",
            Expiration = new DateTime(2027, 12, 31),
            Features = 0x000000000000000BUL,
            Seats = 3,
        };
        string fullKey = issuer.Issue(full);
        Add(vectors, "complete_valide", fullKey, 1000, Today, LicenceStatus.Valid, full);
        Add(vectors, "dernier_jour_inclus", fullKey, 1000, new DateTime(2027, 12, 31, 23, 59, 59, DateTimeKind.Utc), LicenceStatus.Valid, full);
        Add(vectors, "lendemain_expiree", fullKey, 1000, new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc), LicenceStatus.Expired, full);
        Add(vectors, "autre_produit", fullKey, 2000, Today, LicenceStatus.WrongProduct, full);
        Add(vectors, "saisie_tolerante",
            "  " + fullKey.Replace("-", " ").Replace('0', 'O').Replace('1', 'l').ToLowerInvariant() + "\r\n",
            1000, Today, LicenceStatus.Valid, full);

        var extreme = new LicenceData
        {
            ProductId = int.MaxValue,
            Client = "",
            Expiration = null,
            Features = ulong.MaxValue,
            LicenceId = ulong.MaxValue,
            Seats = ushort.MaxValue,
        };
        Add(vectors, "sans_expiration_valeurs_max", issuer.Issue(extreme), int.MaxValue, new DateTime(2179, 1, 1, 0, 0, 0, DateTimeKind.Utc), LicenceStatus.Valid, extreme);

        var negative = new LicenceData { ProductId = -5, Client = new string('x', LicenceData.ClientMaxBytes), Features = 1UL << 63 };
        Add(vectors, "productid_negatif_client_40_octets", issuer.Issue(negative), -5, Today, LicenceStatus.Valid, negative);

        // Faussaire qui connait le format : masque modifie, CRC recalcule.
        LicenceKeyCodec.TryDecode(fullKey, out byte[] raw);
        raw[7] ^= 0x01;
        Add(vectors, "features_modifie_crc_recalcule", LicenceKeyCodec.Encode(raw), 1000, Today, LicenceStatus.InvalidSignature, null);

        LicenceKeyCodec.TryDecode(fullKey, out raw);
        raw[0] = 2;
        Add(vectors, "version_inconnue", LicenceKeyCodec.Encode(raw), 1000, Today, LicenceStatus.UnsupportedVersion, null);

        LicenceKeyCodec.TryDecode(fullKey, out raw);
        raw[33] += 1;
        Add(vectors, "longueur_client_incoherente", LicenceKeyCodec.Encode(raw), 1000, Today, LicenceStatus.Malformed, null);

        int middle = fullKey.Length / 2;
        if (fullKey[middle] == '-') middle++;
        string typo = fullKey.Substring(0, middle) + (fullKey[middle] == 'A' ? 'B' : 'A') + fullKey.Substring(middle + 1);
        Add(vectors, "faute_de_frappe", typo, 1000, Today, LicenceStatus.Malformed, null);

        Add(vectors, "autre_paire_de_cles", new LicenceIssuer(otherKey).Issue(full), 1000, Today, LicenceStatus.InvalidSignature, null);
        Add(vectors, "tronquee", fullKey.Substring(0, fullKey.Length - 1), 1000, Today, LicenceStatus.Malformed, null);
        Add(vectors, "caractere_interdit", fullKey.Replace(fullKey[0], 'U'), 1000, Today, LicenceStatus.Malformed, null);
        Add(vectors, "vide", "", 1000, Today, LicenceStatus.Malformed, null);

        // Champs personnalises : valeurs relues par le test C++ (RunFields) a l'identique.
        var withFields = new LicenceData { ProductId = 2001, Client = "ACME" };
        withFields.Fields.SetText(1, "Édition Pro");
        withFields.Fields.SetInt(2, -123456);
        withFields.Fields.SetDate(3, new DateTime(2028, 6, 30));
        withFields.Fields.SetBool(4, true);
        withFields.Fields.SetMask(5, ulong.MaxValue);
        withFields.Fields.SetBool(200, false);
        string fieldsKey = issuer.Issue(withFields);
        Add(vectors, "champs_tous_types", fieldsKey, 2001, Today, LicenceStatus.Valid, withFields);

        var fullFields = new LicenceData { ProductId = 2001 };
        fullFields.Fields.SetText(1, new string('a', 30));
        fullFields.Fields.SetText(255, new string('b', 30));
        Add(vectors, "champs_64_octets", issuer.Issue(fullFields), 2001, Today, LicenceStatus.Valid, fullFields);

        // Bloc de champs corrompu : refuse a la lecture, avant meme la signature.
        int block = LicenceData.FixedSize + 4; // apres le client "ACME"
        LicenceKeyCodec.TryDecode(fieldsKey, out raw);
        raw[block] = 3; // premier numero (1) remplace par 3 : plus croissant avec le suivant (2)
        Add(vectors, "champs_numeros_non_croissants", LicenceKeyCodec.Encode(raw), 2001, Today, LicenceStatus.Malformed, null);
        LicenceKeyCodec.TryDecode(fieldsKey, out raw);
        raw[block + 1] = 60; // taille du premier champ au-dela du bloc
        Add(vectors, "champs_taille_debordante", LicenceKeyCodec.Encode(raw), 2001, Today, LicenceStatus.Malformed, null);
        LicenceKeyCodec.TryDecode(fieldsKey, out raw);
        raw[block] = 0;
        Add(vectors, "champs_numero_zero", LicenceKeyCodec.Encode(raw), 2001, Today, LicenceStatus.Malformed, null);

        string posteA = MachineCode.Format(MachineCode.Derive("A:", MachineCode.ParseSmbiosUuid(SmbiosSample)));
        string posteB = MachineCode.Format(MachineCode.DeriveFromMachineGuid(MachineGuidSample));
        var bound = new LicenceData { ProductId = 1000, Client = "Poste unique", MachineCode = posteA, Expiration = new DateTime(2027, 12, 31) };
        string boundKey = issuer.Issue(bound);
        Add(vectors, "liee_bon_poste", boundKey, 1000, Today, LicenceStatus.Valid, bound, posteA);
        Add(vectors, "liee_bon_poste_saisie_tolerante", boundKey, 1000, Today, LicenceStatus.Valid, bound, posteA.ToLowerInvariant().Replace("-", " "));
        Add(vectors, "liee_autre_poste", boundKey, 1000, Today, LicenceStatus.WrongMachine, bound, posteB);
        Add(vectors, "liee_poste_non_identifiable", boundKey, 1000, Today, LicenceStatus.WrongMachine, bound, "");
        Add(vectors, "liee_autre_produit_avant_poste", boundKey, 2000, Today, LicenceStatus.WrongProduct, bound, posteB);
        Add(vectors, "liee_expiree", boundKey, 1000, new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc), LicenceStatus.Expired, bound, posteA);
        Add(vectors, "non_liee_tout_poste", fullKey, 1000, Today, LicenceStatus.Valid, full, posteB);

        LicenceKeyCodec.TryDecode(boundKey, out raw);
        raw[25] ^= 0x01;
        Add(vectors, "poste_modifie_crc_recalcule", LicenceKeyCodec.Encode(raw), 1000, Today, LicenceStatus.InvalidSignature, null, posteA);

        var validator = new LicenceValidator(key.PublicKey);
        foreach (Vector v in vectors)
        {
            LicenceCheck check = validator.Validate(v.Key, v.ProductId, v.Now, v.CurrentMachine == "" ? null : v.CurrentMachine);
            if (check.Status != v.Status)
            {
                Console.Error.WriteLine($"{v.Name} : .NET donne {check.Status}, attendu {v.Status}");
                return 1;
            }
        }

        File.WriteAllText(output, Render(key.PublicKey.ToBase64(), otherKey.PublicKey.ToBase64(), vectors, posteA, posteB), new UTF8Encoding(false));
        Console.WriteLine($"{vectors.Count} vecteurs ecrits dans {output}");
        return 0;
    }

    private static void Add(List<Vector> list, string name, string key, int productId, DateTime now, LicenceStatus status,
        LicenceData data, string currentMachine = "") =>
        list.Add(new Vector { Name = name, Key = key, ProductId = productId, Now = now, Status = status, Data = data, CurrentMachine = currentMachine });

    private static string Render(string publicKey, string otherPublicKey, List<Vector> vectors, string posteA, string posteB)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// GENERE par LicenceManagerCpp/tools/vectorgen - ne pas modifier a la main.");
        sb.AppendLine("// Paire de cles jetable, generee pour ces tests uniquement.");
        sb.AppendLine("#pragma once");
        sb.AppendLine("#include <cstdint>");
        sb.AppendLine();
        sb.AppendLine("struct TestVector {");
        sb.AppendLine("    const char* name;");
        sb.AppendLine("    const char* key;");
        sb.AppendLine("    int32_t productId;");
        sb.AppendLine("    int64_t nowUnix;");
        sb.AppendLine("    licmgr::LicenceStatus status;");
        sb.AppendLine("    bool hasData;");
        sb.AppendLine("    int32_t dataProductId;");
        sb.AppendLine("    const char* client; // UTF-8");
        sb.AppendLine("    int64_t expiryUnix;");
        sb.AppendLine("    uint64_t features;");
        sb.AppendLine("    uint64_t licenceId;");
        sb.AppendLine("    uint16_t seats;");
        sb.AppendLine("    const char* machineCode;    // poste lie attendu, \"\" = tout poste");
        sb.AppendLine("    const char* currentMachine; // poste suppose courant, \"\" = non identifiable");
        sb.AppendLine("    const char* fields;         // champs attendus : \"id:hex;...\"");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine($"static const char kPublicKey[] = {Literal(publicKey)};");
        sb.AppendLine($"static const char kOtherPublicKey[] = {Literal(otherPublicKey)};");
        sb.AppendLine();
        sb.AppendLine("// Derivation du code poste (FORMAT.md section 5).");
        sb.AppendLine($"static const unsigned char kSmbiosSample[] = {{ {string.Join(", ", Array.ConvertAll(SmbiosSample, b => "0x" + b.ToString("X2")))} }};");
        sb.AppendLine($"static const char kSmbiosSampleCode[] = {Literal(posteA)};");
        sb.AppendLine($"static const char kMachineGuidSample[] = {Literal(MachineGuidSample)};");
        sb.AppendLine($"static const char kMachineGuidSampleCode[] = {Literal(posteB)};");
        sb.AppendLine();
        sb.AppendLine("static const TestVector kVectors[] = {");
        foreach (Vector v in vectors)
        {
            LicenceData d = v.Data ?? new LicenceData();
            long expiry = d.Expiration.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(d.Expiration.Value.Date, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0;
            sb.AppendLine($"    {{ {Literal(v.Name)}, {Literal(v.Key)}, {v.ProductId}, {new DateTimeOffset(v.Now).ToUnixTimeSeconds()}LL, " +
                          $"licmgr::LicenceStatus::{v.Status}, {(v.Data != null ? "true" : "false")}, {d.ProductId}, {Literal(d.Client ?? "")}, " +
                          $"{expiry}LL, 0x{d.Features:X16}ULL, 0x{d.LicenceId:X16}ULL, {d.Seats}, " +
                          $"{Literal(d.MachineCode ?? "")}, {Literal(v.CurrentMachine)}, {Literal(FieldsText(d.Fields))} }},");
        }
        sb.AppendLine("};");
        return sb.ToString();
    }

    // Champs sous forme canonique "id:hex;" (ordre croissant), compares au rendu du C++.
    private static string FieldsText(LicenceFields fields)
    {
        var sb = new StringBuilder();
        foreach (byte id in fields.Ids)
            sb.Append(id).Append(':').Append(BitConverter.ToString(fields.GetRaw(id)).Replace("-", "")).Append(';');
        return sb.ToString();
    }

    // Litteral C++ portable : octets hors ASCII imprimable en octal (une sequence \x avalerait
    // les chiffres hexadecimaux qui suivent).
    private static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            if (b == '"' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            else sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
        }
        return sb.Append('"').ToString();
    }
}
