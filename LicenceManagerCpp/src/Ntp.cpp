#include "Ntp.h"

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")
#endif

namespace licmgr {

#ifndef _WIN32
// Hors Windows (compilation de test uniquement) : pas de NTP, repli sur l'horloge locale.
bool GetNtpTimeUtc(const std::string&, int, int64_t&) {
    return false;
}
#else
namespace {
constexpr int64_t kNtpToUnixEpochSeconds = 2208988800LL; // 1900-01-01 -> 1970-01-01
}

bool GetNtpTimeUtc(const std::string& server, int timeoutMs, int64_t& outUnixTimeUtc) {
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) return false;

    bool ok = false;
    SOCKET sock = INVALID_SOCKET;

    do {
        struct addrinfo hints{}, *res = nullptr;
        hints.ai_family = AF_INET;
        hints.ai_socktype = SOCK_DGRAM;
        hints.ai_protocol = IPPROTO_UDP;

        if (getaddrinfo(server.c_str(), "123", &hints, &res) != 0 || res == nullptr) break;

        sock = socket(res->ai_family, res->ai_socktype, res->ai_protocol);
        if (sock == INVALID_SOCKET) { freeaddrinfo(res); break; }

        DWORD timeout = (DWORD)timeoutMs;
        setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
        setsockopt(sock, SOL_SOCKET, SO_SNDTIMEO, (const char*)&timeout, sizeof(timeout));

        uint8_t packet[48] = {0};
        packet[0] = 0x1B; // LI=0, VN=3, Mode=3 (client)

        int sent = sendto(sock, (const char*)packet, sizeof(packet), 0, res->ai_addr, (int)res->ai_addrlen);
        freeaddrinfo(res);
        if (sent != sizeof(packet)) break;

        uint8_t reply[48] = {0};
        int received = recv(sock, (char*)reply, sizeof(reply), 0);
        if (received < 48) break;

        uint32_t intPart = (uint32_t(reply[40]) << 24) | (uint32_t(reply[41]) << 16) |
                            (uint32_t(reply[42]) << 8) | uint32_t(reply[43]);

        outUnixTimeUtc = (int64_t)intPart - kNtpToUnixEpochSeconds;
        ok = true;
    } while (false);

    if (sock != INVALID_SOCKET) closesocket(sock);
    WSACleanup();
    return ok;
}
#endif

} // namespace licmgr
