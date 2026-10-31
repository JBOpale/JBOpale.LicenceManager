#pragma once
// Decodage du texte et des octets d'une cle de licence (FORMAT.md, sections 1 et 3).
#include <array>
#include <cstddef>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace licmgr {

constexpr uint8_t kFormatVersion = 1;
constexpr size_t kClientOffset = 34;
constexpr size_t kPayloadFixedSize = kClientOffset + 1; // + octet fieldsLength
constexpr size_t kFieldsMaxBytes = 64;
constexpr size_t kClientMaxBytes = 40;
constexpr size_t kSignatureSize = 64;
constexpr size_t kMachineIdSize = 8;

using MachineId = std::array<uint8_t, kMachineIdSize>; // tout a zero = licence non liee

struct Payload {
    int32_t productId = 0;
    uint16_t expirationDays = 0; // jours depuis le 01/01/2000, 0 = jamais
    uint64_t features = 0;
    uint64_t licenceId = 0;
    uint16_t seats = 0;
    MachineId machine{};
    std::string client;
    std::map<uint8_t, std::vector<uint8_t>> fields; // champs personnalises (FORMAT.md section 6)
};

// Base32 Crockford tolerant + verification du CRC-16 ; `raw` = payload || signature.
bool DecodeKeyText(const std::string& text, std::vector<uint8_t>& raw);

// Lit `count` octets de payload (version deja verifiee par l'appelant).
bool ParsePayload(const uint8_t* data, size_t count, Payload& out);

uint16_t Crc16CcittFalse(const uint8_t* data, size_t count);

bool IsZero(const MachineId& id);

// Code poste (FORMAT.md section 5) : 8 octets + CRC-16 en Base32, groupes de 4 caracteres.
std::string FormatMachineCode(const MachineId& id);
bool ParseMachineCode(const std::string& text, MachineId& id);

} // namespace licmgr
