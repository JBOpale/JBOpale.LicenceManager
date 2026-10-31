#include "KeyFormat.h"

#include <algorithm>

namespace licmgr {

namespace {

const char kAlphabet[] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

// O lu 0, I et L lus 1, minuscules acceptees ; -1 si invalide.
int DecodeChar(char c) {
    if (c >= 'a' && c <= 'z') c = (char)(c - 'a' + 'A');
    if (c == 'O') c = '0';
    else if (c == 'I' || c == 'L') c = '1';
    for (int i = 0; i < 32; i++)
        if (kAlphabet[i] == c) return i;
    return -1;
}

bool IsSkipped(char c) {
    return c == '-' || c == ' ' || c == '\t' || c == '\r' || c == '\n';
}

uint64_t ReadLe(const uint8_t* p, size_t size) {
    uint64_t v = 0;
    for (size_t i = 0; i < size; i++) v |= (uint64_t)p[i] << (8 * i);
    return v;
}

} // namespace

bool IsZero(const MachineId& id) {
    for (uint8_t b : id)
        if (b != 0) return false;
    return true;
}

std::string FormatMachineCode(const MachineId& id) {
    std::vector<uint8_t> data(id.begin(), id.end());
    uint16_t crc = Crc16CcittFalse(data.data(), data.size());
    data.push_back((uint8_t)(crc >> 8));
    data.push_back((uint8_t)crc);

    std::string out;
    uint32_t buffer = 0;
    int bits = 0, chars = 0;
    auto append = [&](uint32_t v) {
        if (chars > 0 && chars % 4 == 0) out += '-';
        out += kAlphabet[v & 31];
        chars++;
    };
    for (uint8_t b : data) {
        buffer = (buffer << 8) | b;
        bits += 8;
        while (bits >= 5) {
            append(buffer >> (bits - 5));
            bits -= 5;
        }
        buffer &= (1u << bits) - 1u;
    }
    if (bits > 0) append(buffer << (5 - bits));
    return out;
}

bool ParseMachineCode(const std::string& text, MachineId& id) {
    std::vector<uint8_t> raw;
    if (!DecodeKeyText(text, raw) || raw.size() != kMachineIdSize) return false;
    std::copy(raw.begin(), raw.end(), id.begin());
    return !IsZero(id);
}

uint16_t Crc16CcittFalse(const uint8_t* data, size_t count) {
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < count; i++) {
        crc ^= (uint16_t)(data[i] << 8);
        for (int j = 0; j < 8; j++)
            crc = (crc & 0x8000) ? (uint16_t)((crc << 1) ^ 0x1021) : (uint16_t)(crc << 1);
    }
    return crc;
}

bool DecodeKeyText(const std::string& text, std::vector<uint8_t>& raw) {
    raw.clear();
    std::vector<uint8_t> bytes;
    uint32_t buffer = 0;
    int bits = 0;
    for (char c : text) {
        if (IsSkipped(c)) continue;
        int v = DecodeChar(c);
        if (v < 0) return false;
        buffer = (buffer << 5) | (uint32_t)v;
        bits += 5;
        if (bits >= 8) {
            bytes.push_back((uint8_t)(buffer >> (bits - 8)));
            bits -= 8;
        }
        buffer &= (1u << bits) - 1u;
    }
    if (bits >= 5 || buffer != 0) return false; // remplissage invalide
    if (bytes.size() < 3) return false;

    size_t n = bytes.size() - 2;
    uint16_t expected = (uint16_t)((bytes[n] << 8) | bytes[n + 1]);
    if (Crc16CcittFalse(bytes.data(), n) != expected) return false;

    bytes.resize(n);
    raw.swap(bytes);
    return true;
}

bool ParsePayload(const uint8_t* data, size_t count, Payload& out) {
    if (count < kPayloadFixedSize) return false;
    size_t clientLength = data[33];
    if (clientLength > kClientMaxBytes || count < kPayloadFixedSize + clientLength) return false;
    size_t fieldsLength = data[kClientOffset + clientLength];
    if (fieldsLength > kFieldsMaxBytes || count != kPayloadFixedSize + clientLength + fieldsLength) return false;

    // Entrees (numero, taille, valeur), numeros strictement croissants.
    out.fields.clear();
    const uint8_t* p = data + kPayloadFixedSize + clientLength;
    const uint8_t* end = p + fieldsLength;
    int previous = 0;
    while (p < end) {
        if (end - p < 2) return false;
        uint8_t id = p[0];
        size_t length = p[1];
        p += 2;
        if (id <= previous || (size_t)(end - p) < length) return false;
        out.fields[id].assign(p, p + length);
        p += length;
        previous = id;
    }

    out.productId = (int32_t)(uint32_t)ReadLe(data + 1, 4);
    out.expirationDays = (uint16_t)ReadLe(data + 5, 2);
    out.features = ReadLe(data + 7, 8);
    out.licenceId = ReadLe(data + 15, 8);
    out.seats = (uint16_t)ReadLe(data + 23, 2);
    std::copy(data + 25, data + 25 + kMachineIdSize, out.machine.begin());
    out.client.assign((const char*)data + kClientOffset, clientLength);
    return true;
}

} // namespace licmgr
