#pragma once
// Client SNTP minimal (RFC 4330) : interroge un serveur NTP en UDP/123 et renvoie l'heure
// UTC courante. Utilise Winsock (composant standard du SDK Windows) ; hors Windows, renvoie
// toujours false (compilation de test uniquement).
#include <cstdint>
#include <string>

namespace licmgr {

// Renvoie true et remplit `outUnixTimeUtc` (secondes depuis epoch, UTC) en cas de succes.
// `timeoutMs` : delai d'attente reseau avant repli hors-ligne.
bool GetNtpTimeUtc(const std::string& server, int timeoutMs, int64_t& outUnixTimeUtc);

} // namespace licmgr
