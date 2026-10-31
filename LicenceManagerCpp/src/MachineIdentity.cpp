#include "MachineIdentity.h"
#include "Crypto.h"

#include <algorithm>
#include <cstring>
#include <vector>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX // windows.h definirait sinon des macros min/max qui cassent std::min
#endif
#include <windows.h>
#pragma comment(lib, "advapi32.lib")
#endif

namespace licmgr {

namespace {

const uint8_t kDefaultOemUuid[16] = {0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05,
                                     0x00, 0x06, 0x00, 0x07, 0x00, 0x08, 0x00, 0x09};

bool IsPlaceholder(const uint8_t uuid[16]) {
    bool allZero = true, allFf = true;
    for (int i = 0; i < 16; i++) {
        allZero = allZero && uuid[i] == 0x00;
        allFf = allFf && uuid[i] == 0xFF;
    }
    return allZero || allFf || std::memcmp(uuid, kDefaultOemUuid, 16) == 0;
}

uint32_t ReadLe32(const uint8_t* p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

#ifdef _WIN32
bool ReadSmbiosUuid(uint8_t uuid[16]) {
    const DWORD rsmb = 0x52534D42; // 'RSMB'
    UINT size = GetSystemFirmwareTable(rsmb, 0, nullptr, 0);
    if (size == 0) return false;
    std::vector<uint8_t> buffer(size);
    if (GetSystemFirmwareTable(rsmb, 0, buffer.data(), size) != size) return false;
    return ParseSmbiosUuid(buffer.data(), buffer.size(), uuid);
}

// Vue 64 bits du registre : un processus 32 bits lirait sinon une autre cle.
bool ReadMachineGuid(std::string& guid) {
    wchar_t value[128];
    DWORD bytes = sizeof(value);
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Cryptography", L"MachineGuid",
                     RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, value, &bytes) != ERROR_SUCCESS)
        return false;
    guid.clear();
    for (const wchar_t* p = value; *p; p++) {
        if (*p > 0x7F) return false; // un GUID est en ASCII
        guid += (char)*p;
    }
    return true;
}
#endif

} // namespace

bool ParseSmbiosUuid(const uint8_t* raw, size_t size, uint8_t uuid[16]) {
    // En-tete RawSMBIOSData : methode(1), version majeure(1), mineure(1), revision DMI(1), longueur(4)
    if (raw == nullptr || size < 8) return false;
    int major = raw[1], minor = raw[2];
    if (major < 2 || (major == 2 && minor < 1)) return false; // UUID apparu en SMBIOS 2.1
    size_t end = std::min(size, (size_t)8 + ReadLe32(raw + 4));

    size_t p = 8;
    while (p + 4 <= end) {
        int type = raw[p];
        size_t len = raw[p + 1];
        if (len < 4 || p + len > end) return false;
        if (type == 1 && len >= 0x19) {
            std::memcpy(uuid, raw + p + 8, 16);
            return !IsPlaceholder(uuid);
        }
        if (type == 127) return false;

        // Zone de chaines : terminee par deux octets nuls.
        size_t q = p + len;
        while (q + 1 < end && (raw[q] != 0 || raw[q + 1] != 0)) q++;
        p = q + 2;
    }
    return false;
}

bool DeriveMachineId(char source, const uint8_t* value, size_t size, MachineId& id) {
    std::string prefix = std::string("LicenceManager.Machine/1:") + source + ":";
    std::vector<uint8_t> input(prefix.begin(), prefix.end());
    input.insert(input.end(), value, value + size);

    uint8_t digest[32];
    if (!Sha256(input.data(), input.size(), digest)) return false;
    std::copy(digest, digest + kMachineIdSize, id.begin());
    return !IsZero(id); // 0 est reserve a "licence non liee"
}

bool DeriveFromMachineGuid(const std::string& machineGuid, MachineId& id) {
    const char* ws = " \t\r\n";
    size_t b = machineGuid.find_first_not_of(ws);
    if (b == std::string::npos) return false;
    std::string guid = machineGuid.substr(b, machineGuid.find_last_not_of(ws) - b + 1);
    for (char& c : guid)
        if (c >= 'A' && c <= 'Z') c = (char)(c - 'A' + 'a');
    return DeriveMachineId('B', (const uint8_t*)guid.data(), guid.size(), id);
}

bool CurrentMachineId(MachineId& id) {
#ifdef _WIN32
    uint8_t uuid[16];
    if (ReadSmbiosUuid(uuid)) return DeriveMachineId('A', uuid, sizeof(uuid), id);

    std::string guid;
    if (ReadMachineGuid(guid)) return DeriveFromMachineGuid(guid, id);
    return false;
#else
    (void)id;
    return false;
#endif
}

} // namespace licmgr
