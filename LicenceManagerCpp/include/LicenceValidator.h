#pragma once
//
// API publique du validateur C++ de LicenceManager (validation uniquement).
// Format des cles : FORMAT.md a la racine du depot (ECDSA P-256 / SHA-256).
//
// Usage :
//   1) Au demarrage (une seule fois), fournir la cle PUBLIQUE du produit (Base64, 88 caracteres,
//      affichee et copiable dans le KeyGenerator) :
//          licmgr::LicenceValidator::Configure("BCx...==");
//      ou      licmgr::LicenceValidator::ConfigureFromFile("licence-public-key.txt");
//   2) Valider une cle :
//          licmgr::LicenceInfo info;
//          bool ok = licmgr::LicenceValidator::Validate(key, 1000, info);
//          // info.status detaille le refus ; info.features = masque de modules
//
// Aucune cle privee n'est presente dans ce module.
//
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace licmgr {

enum class LicenceStatus {
    Valid,              // signature valide, bon produit, non expiree
    Malformed,          // texte illisible : caractere invalide, longueur, erreur de copie (CRC)
    UnsupportedVersion, // version de format inconnue de ce validateur
    InvalidSignature,   // cle alteree ou emise avec une autre paire de cles
    WrongProduct,       // signature valide mais autre produit
    WrongMachine,       // licence liee a un autre poste (ou poste courant non identifiable)
    Expired,            // signature valide mais date depassee (ou heure NTP obligatoire indisponible)
};

struct LicenceInfo {
    bool valid = false;          // status == Valid
    bool expired = false;        // status == Expired
    bool signatureValid = false; // signature valide (independamment du produit et de l'expiration)
    LicenceStatus status = LicenceStatus::Malformed;

    // Champs ci-dessous renseignes uniquement si signatureValid.
    int32_t productId = 0;
    std::string product;         // productId en decimal (compatibilite avec l'ancienne API)
    std::string client;          // UTF-8
    int64_t expiryUnix = 0;      // debut (00:00 UTC) du dernier jour valide ; 0 = n'expire jamais
    uint64_t features = 0;       // masque de modules, semantique propre au produit
    uint64_t licenceId = 0;      // identifiant unique de la licence
    uint16_t seats = 0;          // nombre de postes, 0 = non limite
    std::string machineCode;     // code du poste lie (ex. "WSZ6-G7W2-KQCT-CD3Q"), vide = tout poste

    // Champs personnalises du produit (FORMAT.md section 6), par numero. Les lectures typees
    // renvoient false si le champ est absent ou d'un autre type ; `value` reste alors inchange.
    std::map<uint8_t, std::vector<uint8_t>> fields;
    bool HasField(uint8_t id) const;
    bool GetInt(uint8_t id, int32_t& value) const;
    bool GetBool(uint8_t id, bool& value) const;
    bool GetDate(uint8_t id, int64_t& unixUtc) const; // debut du jour (00:00 UTC)
    bool GetText(uint8_t id, std::string& value) const; // UTF-8
    bool GetMask(uint8_t id, uint64_t& value) const;
};

enum class TimeValidationMethod {
    PreferInternetTime, // NTP si possible, sinon horloge locale (par defaut)
    UseInternetTime,    // NTP obligatoire ; echec reseau => considere comme expire
    UseLocalTime,       // horloge locale uniquement (pas de requete reseau)
};

class LicenceValidator {
public:
    // Cle publique ECDSA P-256 en Base64 (X puis Y, 64 octets). Leve std::invalid_argument si
    // elle est invalide (par exemple un fichier XML a la place de la cle).
    static void Configure(const std::string& publicKeyBase64);
    static void ConfigureFromFile(const std::string& path);

    static void SetTimeValidationMethod(TimeValidationMethod method);
    static void SetNtpServer(const std::string& hostname); // "time.windows.com" par defaut

    // true si status == Valid. `out` est toujours rempli, y compris en cas d'echec.
    static bool Validate(const std::string& licenceKey, int32_t productId, LicenceInfo& out);

    // Compatibilite avec l'ancienne API : productId en decimal. Une chaine vide desactive le
    // controle du produit (deconseille : preferer la surcharge entiere).
    static bool Validate(const std::string& licenceKey, const std::string& productId, LicenceInfo& out);

    // Comme Validate, a une date donnee (secondes Unix UTC) sans interroger l'horloge : tests,
    // ou produit qui gere lui-meme sa source de temps.
    static bool ValidateAt(const std::string& licenceKey, int32_t productId, int64_t nowUnixUtc, LicenceInfo& out);

    // Comme ValidateAt, en supposant que le poste courant a le code currentMachineCode (vide :
    // poste non identifiable). Sert aux tests.
    static bool ValidateAt(const std::string& licenceKey, int32_t productId, int64_t nowUnixUtc,
                           const std::string& currentMachineCode, LicenceInfo& out);

    // Code du poste courant, a transmettre pour obtenir une licence liee a ce poste. Chaine
    // vide si le poste n'est pas identifiable. Ne necessite pas Configure().
    static std::string GetMachineCode();
};

} // namespace licmgr
