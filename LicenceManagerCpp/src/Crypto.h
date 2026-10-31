#pragma once
// SHA-256 et verification ECDSA P-256 deleguees a la cryptographie du systeme :
// CNG (bcrypt.dll) sous Windows, libcrypto (OpenSSL 3) ailleurs - cette seconde variante ne
// sert qu'a compiler et tester le validateur hors Windows (CMakeLists.txt).
#include <cstddef>
#include <cstdint>

namespace licmgr {

bool Sha256(const uint8_t* data, size_t size, uint8_t digest[32]);

// pub : X puis Y (32 octets chacun, big-endian). false si le point n'est pas sur la courbe.
bool IsValidP256PublicKey(const uint8_t pub[64]);

// signature : r puis s (32 octets chacun, big-endian, format IEEE P1363).
bool VerifyP256Sha256(const uint8_t pub[64], const uint8_t* data, size_t size, const uint8_t signature[64]);

} // namespace licmgr
