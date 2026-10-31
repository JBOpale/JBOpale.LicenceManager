#include "Base64.h"

namespace licmgr {

namespace {
int DecodeChar(unsigned char c) {
    if (c >= 'A' && c <= 'Z') return c - 'A';
    if (c >= 'a' && c <= 'z') return c - 'a' + 26;
    if (c >= '0' && c <= '9') return c - '0' + 52;
    if (c == '+') return 62;
    if (c == '/') return 63;
    return -1;
}
}

bool Base64DecodeStrict(const std::string& s, std::vector<uint8_t>& out) {
    out.clear();
    if (s.empty() || s.size() % 4 != 0) return false;

    size_t padding = 0;
    if (s[s.size() - 1] == '=') padding++;
    if (s[s.size() - 2] == '=') padding++;

    uint32_t val = 0;
    int bits = 0;
    for (size_t i = 0; i < s.size() - padding; i++) {
        int d = DecodeChar((unsigned char)s[i]);
        if (d < 0) return false;
        val = (val << 6) | (uint32_t)d;
        bits += 6;
        if (bits >= 8) {
            out.push_back((uint8_t)(val >> (bits - 8)));
            bits -= 8;
        }
        val &= (1u << bits) - 1u;
    }
    return true;
}

} // namespace licmgr
