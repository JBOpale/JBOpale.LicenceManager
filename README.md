# JBOpale.LicenceManager

Compact, offline software licence keys for .NET and native C++ applications.

- **Standard cryptography**: ECDSA P-256 / SHA-256 signatures through the platform crypto
  (`System.Security.Cryptography` in .NET, Windows CNG `bcrypt.dll` in C++). No third-party
  dependency.
- **Short, typeable keys**: Crockford Base32 in groups of 6 characters with a CRC-16 that catches
  copy errors before signature checking (about 195 characters).
- **Same keys everywhere**: issued in .NET, validated identically in .NET and in a
  dependency-free C++ static library.
- **Optional machine binding**, per licence: a 16-character machine code (motherboard UUID, or
  the Windows `MachineGuid` as fallback).
- **Feature bitmask, seats, expiration, licence ID** and up to 64 bytes of **typed custom fields**
  per product.
- **Key generator** (WPF): private key protected by DPAPI, passphrase-protected backups, product
  profiles with modules and custom fields.

The byte-level format is specified in [FORMAT.md](FORMAT.md).

| Folder | Content |
|---|---|
| `LicenceManager/` | .NET library (net48, net8.0): issuing and validation — NuGet `JBOpale.LicenceManager` |
| `LicenceManagerCpp/` | C++ validator (x64 static library) — NuGet `JBOpale.LicenceManager.Cpp`, see [INTEGRATION.md](LicenceManagerCpp/INTEGRATION.md) |
| `LicenceManager.KeyGenerator/` | Windows key generator (WPF), see [PRODUCTS.md](LicenceManager.KeyGenerator/PRODUCTS.md) |
| `LicenceManagerCpp/tools/machinecode/` | `LicenceManager.CodePoste.exe`: shows the machine code to your customers |

## Validate a key (.NET)

```csharp
using LicenceManager;

var validator = new LicenceValidator("<your public key, Base64>");
LicenceCheck check = validator.Validate(enteredKey, productId: 1000);
if (check.IsValid)
{
    ulong modules = check.Data.Features;
    check.Data.Fields.TryGetInt(2, out int maxUsers); // custom field #2, if any
}
else
{
    // check.Status: Malformed, UnsupportedVersion, InvalidSignature, WrongProduct, WrongMachine, Expired
}
```

## Validate a key (C++)

```cpp
#include "LicenceValidator.h"

licmgr::LicenceValidator::Configure("<your public key, Base64>");
licmgr::LicenceInfo info;
if (licmgr::LicenceValidator::Validate(enteredKey, 1000, info)) {
    uint64_t modules = info.features;
}
std::string machineCode = licmgr::LicenceValidator::GetMachineCode(); // for machine-bound licences
```

## Issue a key (your private tool only)

```csharp
LicenceSigningKey key = LicenceSigningKey.Create();          // once; keep it secret and backed up
string publicKey = key.PublicKey.ToBase64();                  // embed this in your products

var data = new LicenceData
{
    ProductId = 1000,
    Client = "ACME Corp",
    Expiration = new DateTime(2027, 12, 31),
    Features = 0x7,
    MachineCode = null,                                       // or the customer's machine code
};
data.Fields.SetInt(2, 25);
string licenceKey = new LicenceIssuer(key).Issue(data);
```

Or use the WPF **KeyGenerator**, which stores the private key with DPAPI and exports
passphrase-protected backups.

## Security notes

- The scheme is only as safe as your **private key**: never ship it, back it up offline.
- Publishing this code does not weaken your licences: forging a key requires the private key.
- Like any client-side check, a validation can be bypassed by patching your binary; this library
  makes forging keys impractical, not tampering with your executable.

## Building and testing

- .NET: `dotnet test LicenceManager.sln` (Windows for the WPF KeyGenerator and DPAPI tests).
- C++ on Windows: build `LicenceManagerCpp/LicenceManagerCpp.sln` (x64, MSVC v145) and run
  `bin\x64\Release\LicenceManagerCpp.Tests.exe`.
- C++ on Linux (OpenSSL instead of CNG, for tests only):
  `cmake -S LicenceManagerCpp -B build && cmake --build build && ./build/LicenceManagerCpp.Tests`.

Code comments and the KeyGenerator user interface are currently in French.

## License

[MIT](LICENSE) © 2026 JB-Opale.
