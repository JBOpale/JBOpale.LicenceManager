// Test croise .NET -> C++ : chaque cle de test_vectors.h a ete emise et revalidee par la
// bibliotheque .NET (tools/vectorgen) ; le validateur C++ doit rendre le meme statut et
// relire exactement les memes champs. Code de sortie 0 si tout passe.
#include "LicenceValidator.h"
#include "MachineIdentity.h"
#include "test_vectors.h"

#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <string>

namespace {

int g_failures = 0;

void Expect(bool condition, const std::string& what) {
    if (!condition) {
        std::printf("  ECHEC : %s\n", what.c_str());
        g_failures++;
    }
}

std::string FieldsText(const licmgr::LicenceInfo& info) {
    static const char hex[] = "0123456789ABCDEF";
    std::string s;
    for (const auto& kv : info.fields) {
        s += std::to_string(kv.first) + ":";
        for (uint8_t b : kv.second) {
            s += hex[b >> 4];
            s += hex[b & 15];
        }
        s += ";";
    }
    return s;
}

const char* StatusName(licmgr::LicenceStatus s) {
    switch (s) {
        case licmgr::LicenceStatus::Valid: return "Valid";
        case licmgr::LicenceStatus::Malformed: return "Malformed";
        case licmgr::LicenceStatus::UnsupportedVersion: return "UnsupportedVersion";
        case licmgr::LicenceStatus::InvalidSignature: return "InvalidSignature";
        case licmgr::LicenceStatus::WrongProduct: return "WrongProduct";
        case licmgr::LicenceStatus::WrongMachine: return "WrongMachine";
        case licmgr::LicenceStatus::Expired: return "Expired";
    }
    return "?";
}

void RunVectors() {
    for (const TestVector& v : kVectors) {
        std::printf("%s\n", v.name);
        licmgr::LicenceInfo info;
        bool ok = licmgr::LicenceValidator::ValidateAt(v.key, v.productId, v.nowUnix, v.currentMachine, info);

        Expect(info.status == v.status, std::string("statut ") + StatusName(info.status) + ", attendu " + StatusName(v.status));
        Expect(ok == (v.status == licmgr::LicenceStatus::Valid), "valeur de retour");
        Expect(info.valid == ok, "info.valid");
        Expect(info.expired == (v.status == licmgr::LicenceStatus::Expired), "info.expired");
        Expect(info.signatureValid == v.hasData, "info.signatureValid");
        if (!v.hasData) continue;

        Expect(info.productId == v.dataProductId, "productId");
        Expect(info.product == std::to_string(v.dataProductId), "product (texte)");
        Expect(info.client == v.client, "client : '" + info.client + "'");
        Expect(info.expiryUnix == v.expiryUnix, "expiryUnix");
        Expect(info.features == v.features, "features");
        Expect(info.licenceId == v.licenceId, "licenceId");
        Expect(info.seats == v.seats, "seats");
        Expect(info.machineCode == v.machineCode, "machineCode : '" + info.machineCode + "'");
        Expect(FieldsText(info) == v.fields, "champs : '" + FieldsText(info) + "'");
    }
}

const TestVector& FindVector(const char* name) {
    for (const TestVector& v : kVectors)
        if (std::strcmp(v.name, name) == 0) return v;
    throw std::runtime_error(std::string("vecteur introuvable : ") + name);
}

// Lectures typees sur la cle "champs_tous_types" (valeurs fixees dans tools/vectorgen).
void RunFields() {
    std::printf("champs_lecture_typee\n");
    const TestVector& v = FindVector("champs_tous_types");
    licmgr::LicenceInfo info;
    licmgr::LicenceValidator::ValidateAt(v.key, v.productId, v.nowUnix, v.currentMachine, info);

    std::string text;
    int32_t number = 0;
    int64_t date = 0;
    bool flag = false;
    uint64_t mask = 0;
    Expect(info.GetText(1, text) && text == "\303\211dition Pro", "texte");
    Expect(info.GetInt(2, number) && number == -123456, "entier");
    Expect(info.GetDate(3, date) && date == 1845936000LL, "date (30/06/2028)");
    Expect(info.GetBool(4, flag) && flag, "oui/non vrai");
    Expect(info.GetMask(5, mask) && mask == 0xFFFFFFFFFFFFFFFFULL, "masque");
    Expect(info.GetBool(200, flag) && !flag, "oui/non faux");

    Expect(info.HasField(2) && !info.HasField(9), "HasField");
    number = 42;
    Expect(!info.GetInt(9, number) && number == 42, "champ absent, valeur inchangee");
    Expect(!info.GetBool(2, flag) && !info.GetDate(2, date) && !info.GetMask(2, mask), "mauvais type");
}

void RunMachineCode() {
    std::printf("code_poste\n");
    uint8_t uuid[16];
    licmgr::MachineId id{};
    Expect(licmgr::ParseSmbiosUuid(kSmbiosSample, sizeof(kSmbiosSample), uuid), "lecture SMBIOS");
    Expect(licmgr::DeriveMachineId('A', uuid, sizeof(uuid), id) && licmgr::FormatMachineCode(id) == kSmbiosSampleCode,
           "code poste depuis l'UUID SMBIOS");
    Expect(licmgr::DeriveFromMachineGuid(kMachineGuidSample, id) && licmgr::FormatMachineCode(id) == kMachineGuidSampleCode,
           "code poste depuis MachineGuid");

    // Table tronquee a toutes les longueurs : jamais de lecture hors limites (ASan/valgrind).
    for (size_t n = 0; n < sizeof(kSmbiosSample); n++) licmgr::ParseSmbiosUuid(kSmbiosSample, n, uuid);

    licmgr::MachineId parsed{};
    Expect(licmgr::ParseMachineCode(kSmbiosSampleCode, parsed), "relecture du code poste");
    Expect(!licmgr::ParseMachineCode("0000-0000-0000-0000", parsed), "code poste nul refuse");
    std::string typo = kSmbiosSampleCode;
    typo[0] = typo[0] == 'A' ? 'B' : 'A';
    Expect(!licmgr::ParseMachineCode(typo, parsed), "faute de frappe dans le code poste");

    std::string current = licmgr::LicenceValidator::GetMachineCode();
#ifdef _WIN32
    Expect(!current.empty() && licmgr::ParseMachineCode(current, parsed), "poste courant identifiable");
    Expect(current == licmgr::LicenceValidator::GetMachineCode(), "code poste stable");
#else
    Expect(current.empty(), "pas de code poste hors Windows");
#endif
}

void RunCompatibilityApi() {
    std::printf("api_productid_texte\n");
    const TestVector& v = kVectors[0]; // complete_valide, produit 1000, expire fin 2027
    licmgr::LicenceInfo info;

    // Horloge locale : la cle expire le 31/12/2027, test valable jusqu'a cette date.
    licmgr::LicenceValidator::SetTimeValidationMethod(licmgr::TimeValidationMethod::UseLocalTime);
    Expect(licmgr::LicenceValidator::Validate(v.key, "1000", info), "Validate(\"1000\")");
    Expect(licmgr::LicenceValidator::Validate(v.key, 1000, info), "Validate(1000)");
    Expect(!licmgr::LicenceValidator::Validate(v.key, "2000", info) && info.status == licmgr::LicenceStatus::WrongProduct, "Validate(\"2000\")");
    Expect(!licmgr::LicenceValidator::Validate(v.key, "abc", info) && info.status == licmgr::LicenceStatus::WrongProduct, "Validate(\"abc\")");
    Expect(licmgr::LicenceValidator::Validate(v.key, "", info), "Validate(\"\") sans controle produit");
}

void RunConfiguration() {
    std::printf("configuration\n");
    auto throws = [](const std::string& key) {
        try {
            licmgr::LicenceValidator::Configure(key);
        } catch (const std::invalid_argument&) {
            return true;
        }
        return false;
    };
    Expect(throws("<KeyTemplate/>"), "contenu XML refuse");
    Expect(throws("pas du base64"), "Base64 invalide refuse");
    Expect(throws("AAAA"), "longueur invalide refusee");
    Expect(throws(std::string(86, 'A') + "=="), "point hors courbe refuse");

    // Cle publique d'une autre paire : les cles valides deviennent InvalidSignature.
    licmgr::LicenceValidator::Configure(kOtherPublicKey);
    licmgr::LicenceInfo info;
    licmgr::LicenceValidator::ValidateAt(kVectors[0].key, 1000, kVectors[0].nowUnix, info);
    Expect(info.status == licmgr::LicenceStatus::InvalidSignature, "autre cle publique");

    // Espaces et retour a la ligne autour de la cle (fichier texte) acceptes.
    licmgr::LicenceValidator::Configure(std::string("  ") + kPublicKey + "\r\n");
    licmgr::LicenceValidator::ValidateAt(kVectors[0].key, 1000, kVectors[0].nowUnix, info);
    Expect(info.valid, "cle publique entouree d'espaces");
}

} // namespace

int main(int argc, char** argv) {
    // Utilise par la CI pour comparer avec MachineCode.Current (.NET) sur la meme machine.
    if (argc > 1 && std::string(argv[1]) == "--machine-code") {
        std::printf("%s\n", licmgr::LicenceValidator::GetMachineCode().c_str());
        return 0;
    }
    try {
        licmgr::LicenceValidator::Configure(kPublicKey);
        RunVectors();
        RunMachineCode();
        RunFields();
        RunCompatibilityApi();
        RunConfiguration();
    } catch (const std::exception& ex) {
        std::printf("EXCEPTION : %s\n", ex.what());
        return 2;
    }

    if (g_failures == 0)
        std::printf("\nOK : tous les tests passent\n");
    else
        std::printf("\n%d ECHEC(S)\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
