#pragma once
// Decodeur Base64 standard (RFC 4648) strict, pour la cle publique produite par
// Convert.ToBase64String cote .NET.
#include <cstdint>
#include <string>
#include <vector>

namespace licmgr {

// false si un caractere est hors alphabet, si le remplissage '=' est mal place ou si la
// longueur n'est pas un multiple de 4.
bool Base64DecodeStrict(const std::string& s, std::vector<uint8_t>& out);

}
