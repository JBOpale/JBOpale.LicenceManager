#include "LicenceValidator.h"
#include "Base64.h"
#include "Crypto.h"
#include "KeyFormat.h"
#include "MachineIdentity.h"
#include "Ntp.h"

#include <algorithm>
#include <array>
#include <ctime>
#include <fstream>
#include <limits>
#include <mutex>
#include <sstream>
#include <stdexcept>

namespace licmgr {

namespace {

constexpr int64_t kEpoch2000Unix = 946684800; // 01/01/2000 00:00 UTC
constexpr int64_t kSecondsPerDay = 86400;
constexpr int kNtpTimeoutMs = 2000;

std::mutex g_mutex;
bool g_configured = false;
std::array<uint8_t, 64> g_publicKey{};
TimeValidationMethod g_timeMethod = TimeValidationMethod::PreferInternetTime;
std::string g_ntpServer = "time.windows.com";

std::string Trim(const std::string& s) {
    const char* ws = " \t\r\n";
    size_t b = s.find_first_not_of(ws);
    if (b == std::string::npos) return std::string();
    size_t e = s.find_last_not_of(ws);
    return s.substr(b, e - b + 1);
}

std::string ReadFile(const std::string& path) {
    std::ifstream f(path, std::ios::binary);
    if (!f) throw std::runtime_error("impossible d'ouvrir " + path);
    std::ostringstream ss;
    ss << f.rdbuf();
    return ss.str();
}

bool CopyConfig(std::array<uint8_t, 64>& publicKey, TimeValidationMethod& method, std::string& ntpServer) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_configured) return false;
    publicKey = g_publicKey;
    method = g_timeMethod;
    ntpServer = g_ntpServer;
    return true;
}

// Heure de reference selon la methode choisie ; ntpFailed indique un echec NTP.
int64_t CurrentUnixTimeUtc(TimeValidationMethod method, const std::string& ntpServer, bool& ntpFailed) {
    ntpFailed = false;
    if (method == TimeValidationMethod::UseLocalTime) return (int64_t)time(nullptr);

    int64_t ntpTime = 0;
    if (GetNtpTimeUtc(ntpServer, kNtpTimeoutMs, ntpTime)) return ntpTime;

    ntpFailed = true;
    return (int64_t)time(nullptr);
}

// Code du poste courant : celui fourni par l'appelant, sinon celui de la machine.
bool ResolveMachine(const std::string* currentMachineCode, MachineId& id) {
    if (currentMachineCode) return ParseMachineCode(*currentMachineCode, id);
    return CurrentMachineId(id);
}

// Verifie format, signature, produit et poste (FORMAT.md section 4, etapes 1 a 6). Laisse le
// statut a Valid si tout est correct : l'expiration est appliquee ensuite par ApplyExpiration.
void Check(const std::array<uint8_t, 64>& publicKey, const std::string& licenceKey, bool checkProduct,
           int32_t productId, const std::string* currentMachineCode, LicenceInfo& out) {
    out = LicenceInfo{};
    out.status = LicenceStatus::Malformed;

    std::vector<uint8_t> raw;
    if (!DecodeKeyText(licenceKey, raw)) return;
    if (raw[0] != kFormatVersion) {
        out.status = LicenceStatus::UnsupportedVersion;
        return;
    }
    if (raw.size() < kPayloadFixedSize + kSignatureSize) return;

    size_t payloadSize = raw.size() - kSignatureSize;
    Payload p;
    if (!ParsePayload(raw.data(), payloadSize, p)) return;

    if (!VerifyP256Sha256(publicKey.data(), raw.data(), payloadSize, raw.data() + payloadSize)) {
        out.status = LicenceStatus::InvalidSignature;
        return;
    }

    out.signatureValid = true;
    out.productId = p.productId;
    out.product = std::to_string(p.productId);
    out.client = p.client;
    out.features = p.features;
    out.licenceId = p.licenceId;
    out.seats = p.seats;
    if (!IsZero(p.machine)) out.machineCode = FormatMachineCode(p.machine);
    out.fields = p.fields;
    out.expiryUnix = p.expirationDays == 0 ? 0 : kEpoch2000Unix + (int64_t)p.expirationDays * kSecondsPerDay;

    if (checkProduct && p.productId != productId) {
        out.status = LicenceStatus::WrongProduct;
        return;
    }
    if (!IsZero(p.machine)) {
        // Le poste n'est identifie que si la licence y est liee.
        MachineId current{};
        if (!ResolveMachine(currentMachineCode, current) || current != p.machine) {
            out.status = LicenceStatus::WrongMachine;
            return;
        }
    }
    out.status = LicenceStatus::Valid;
    out.valid = true;
}

// Etape 6 : valide toute la journee d'expiration, expiree a partir de 00:00 UTC le lendemain.
void ApplyExpiration(int64_t nowUnixUtc, bool forceExpired, LicenceInfo& out) {
    if (out.status != LicenceStatus::Valid || out.expiryUnix == 0) return;
    if (forceExpired || nowUnixUtc >= out.expiryUnix + kSecondsPerDay) {
        out.status = LicenceStatus::Expired;
        out.valid = false;
        out.expired = true;
    }
}

bool ValidateWithClock(const std::string& licenceKey, bool checkProduct, int32_t productId, LicenceInfo& out) {
    std::array<uint8_t, 64> publicKey;
    TimeValidationMethod method;
    std::string ntpServer;
    if (!CopyConfig(publicKey, method, ntpServer))
        throw std::logic_error("LicenceValidator::Configure() n'a pas ete appele");

    Check(publicKey, licenceKey, checkProduct, productId, nullptr, out);
    // On n'interroge l'horloge (eventuellement NTP) que pour une cle authentique qui expire.
    if (out.status != LicenceStatus::Valid || out.expiryUnix == 0) return out.valid;

    bool ntpFailed = false;
    int64_t now = CurrentUnixTimeUtc(method, ntpServer, ntpFailed);
    ApplyExpiration(now, ntpFailed && method == TimeValidationMethod::UseInternetTime, out);
    return out.valid;
}

} // namespace

namespace {

const std::vector<uint8_t>* FindField(const LicenceInfo& info, uint8_t id, size_t size) {
    auto it = info.fields.find(id);
    return it != info.fields.end() && it->second.size() == size ? &it->second : nullptr;
}

uint64_t ReadLe(const std::vector<uint8_t>& b) {
    uint64_t v = 0;
    for (size_t i = 0; i < b.size(); i++) v |= (uint64_t)b[i] << (8 * i);
    return v;
}

} // namespace

bool LicenceInfo::HasField(uint8_t id) const {
    return fields.count(id) != 0;
}

bool LicenceInfo::GetInt(uint8_t id, int32_t& value) const {
    const std::vector<uint8_t>* b = FindField(*this, id, 4);
    if (!b) return false;
    value = (int32_t)(uint32_t)ReadLe(*b);
    return true;
}

bool LicenceInfo::GetBool(uint8_t id, bool& value) const {
    const std::vector<uint8_t>* b = FindField(*this, id, 1);
    if (!b || (*b)[0] > 1) return false;
    value = (*b)[0] == 1;
    return true;
}

bool LicenceInfo::GetDate(uint8_t id, int64_t& unixUtc) const {
    const std::vector<uint8_t>* b = FindField(*this, id, 2);
    if (!b) return false;
    uint64_t days = ReadLe(*b);
    if (days == 0) return false;
    unixUtc = kEpoch2000Unix + (int64_t)days * kSecondsPerDay;
    return true;
}

bool LicenceInfo::GetText(uint8_t id, std::string& value) const {
    auto it = fields.find(id);
    if (it == fields.end() || it->second.size() > 32) return false;
    value.assign(it->second.begin(), it->second.end());
    return true;
}

bool LicenceInfo::GetMask(uint8_t id, uint64_t& value) const {
    const std::vector<uint8_t>* b = FindField(*this, id, 8);
    if (!b) return false;
    value = ReadLe(*b);
    return true;
}

void LicenceValidator::Configure(const std::string& publicKeyBase64) {
    std::string text = Trim(publicKeyBase64);
    if (!text.empty() && text[0] == '<')
        throw std::invalid_argument(
            "contenu XML : configurer la cle publique Base64 affichee par le KeyGenerator");

    std::vector<uint8_t> bytes;
    if (!Base64DecodeStrict(text, bytes) || bytes.size() != 64)
        throw std::invalid_argument("cle publique invalide : 64 octets en Base64 attendus (88 caracteres)");
    if (!IsValidP256PublicKey(bytes.data()))
        throw std::invalid_argument("cle publique invalide : le point n'est pas sur la courbe P-256");

    std::lock_guard<std::mutex> lock(g_mutex);
    std::copy(bytes.begin(), bytes.end(), g_publicKey.begin());
    g_configured = true;
}

void LicenceValidator::ConfigureFromFile(const std::string& path) {
    Configure(ReadFile(path));
}

void LicenceValidator::SetTimeValidationMethod(TimeValidationMethod method) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_timeMethod = method;
}

void LicenceValidator::SetNtpServer(const std::string& hostname) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_ntpServer = hostname;
}

bool LicenceValidator::Validate(const std::string& licenceKey, int32_t productId, LicenceInfo& out) {
    return ValidateWithClock(licenceKey, true, productId, out);
}

bool LicenceValidator::Validate(const std::string& licenceKey, const std::string& productId, LicenceInfo& out) {
    if (productId.empty()) return ValidateWithClock(licenceKey, false, 0, out);

    // Un identifiant non numerique ne peut correspondre a aucune cle : refus apres lecture.
    int32_t id = 0;
    bool numeric = false;
    try {
        size_t used = 0;
        long long v = std::stoll(productId, &used);
        numeric = used == productId.size() && v >= std::numeric_limits<int32_t>::min()
               && v <= std::numeric_limits<int32_t>::max();
        id = (int32_t)v;
    } catch (const std::exception&) {
    }
    if (numeric) return ValidateWithClock(licenceKey, true, id, out);

    ValidateWithClock(licenceKey, false, 0, out);
    if (out.signatureValid) {
        out.status = LicenceStatus::WrongProduct;
        out.valid = out.expired = false;
    }
    return false;
}

namespace {

bool ValidateAtImpl(const std::string& licenceKey, int32_t productId, int64_t nowUnixUtc,
                    const std::string* currentMachineCode, LicenceInfo& out) {
    std::array<uint8_t, 64> publicKey;
    TimeValidationMethod method;
    std::string ntpServer;
    if (!CopyConfig(publicKey, method, ntpServer))
        throw std::logic_error("LicenceValidator::Configure() n'a pas ete appele");
    Check(publicKey, licenceKey, true, productId, currentMachineCode, out);
    ApplyExpiration(nowUnixUtc, false, out);
    return out.valid;
}

} // namespace

bool LicenceValidator::ValidateAt(const std::string& licenceKey, int32_t productId, int64_t nowUnixUtc, LicenceInfo& out) {
    return ValidateAtImpl(licenceKey, productId, nowUnixUtc, nullptr, out);
}

bool LicenceValidator::ValidateAt(const std::string& licenceKey, int32_t productId, int64_t nowUnixUtc,
                                  const std::string& currentMachineCode, LicenceInfo& out) {
    return ValidateAtImpl(licenceKey, productId, nowUnixUtc, &currentMachineCode, out);
}

std::string LicenceValidator::GetMachineCode() {
    MachineId id{};
    return CurrentMachineId(id) ? FormatMachineCode(id) : std::string();
}

} // namespace licmgr
