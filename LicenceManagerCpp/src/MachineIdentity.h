#pragma once
// Identifiant du poste (FORMAT.md section 5) : UUID SMBIOS de la carte mere, sinon MachineGuid
// de Windows. Calcul identique a MachineCode.cs cote .NET.
#include "KeyFormat.h"

#include <cstddef>
#include <cstdint>
#include <string>

namespace licmgr {

// false si aucune source n'est exploitable (systeme autre que Windows, acces refuse...).
bool CurrentMachineId(MachineId& id);

// Exposes pour les tests (independants de la machine).
bool ParseSmbiosUuid(const uint8_t* rawSmbios, size_t size, uint8_t uuid[16]);
bool DeriveMachineId(char source, const uint8_t* value, size_t size, MachineId& id); // source 'A' ou 'B'
bool DeriveFromMachineGuid(const std::string& machineGuid, MachineId& id);

} // namespace licmgr
