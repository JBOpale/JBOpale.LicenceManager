# C++ integration

## What this project builds

`LicenceManagerCpp.vcxproj` builds an **x64 static library** (MSVC, toolset `v145`, C++17):
`LicenceManagerCpp.lib`. It exports no DLL symbol: link it statically into your own DLL/EXE.

No third-party dependency: only the CRT, `bcrypt.lib` (Windows CNG, ECDSA P-256 verification),
`ws2_32.lib` (Winsock, optional NTP time) and `advapi32.lib` (registry read for the machine code).
The key format is specified in [`../FORMAT.md`](../FORMAT.md).

On Linux, `CMakeLists.txt` builds the same library with OpenSSL instead of CNG, for tests only
(see `test/README.md`).

## 1. Consume the package

Install the NuGet package **`JBOpale.LicenceManager.Cpp`** in your `.vcxproj`. Its
`build/native/JBOpale.LicenceManager.Cpp.targets` adds the include directory, the static library
matching the active configuration (Debug or Release) and the system libraries above.

Without NuGet: add `LicenceManagerCpp\include` to the include directories and link
`LicenceManagerCpp.lib`, `bcrypt.lib`, `ws2_32.lib` and `advapi32.lib`.

## 2. Configure the public key and validate

The only configuration is your **public key** (Base64, 88 characters), shown by the KeyGenerator.
It holds no secret: compile it into your product or ship it as a text file.

```cpp
#include "LicenceValidator.h"

// Once, at startup:
licmgr::LicenceValidator::Configure("<public key, Base64>");
// or: licmgr::LicenceValidator::ConfigureFromFile("licence-public-key.txt");

licmgr::LicenceInfo info;
if (licmgr::LicenceValidator::Validate(enteredKey, 1000, info)) {
    // info.client, info.features (bitmask), info.seats, info.licenceId,
    // info.expiryUnix (0 = never expires)
} else {
    switch (info.status) {
        case licmgr::LicenceStatus::Malformed:          /* copy error */ break;
        case licmgr::LicenceStatus::InvalidSignature:   /* forged or altered key */ break;
        case licmgr::LicenceStatus::WrongProduct:       /* key for another product */ break;
        case licmgr::LicenceStatus::WrongMachine:       /* bound to another machine */ break;
        case licmgr::LicenceStatus::Expired:            /* past expiration date */ break;
        case licmgr::LicenceStatus::UnsupportedVersion: /* newer key format */ break;
        default: break;
    }
}
```

`Configure` throws `std::invalid_argument` for an invalid key (bad Base64, wrong length, point not
on the curve, XML content). Prefer the integer `productId` overload of `Validate`: in the string
overload, kept for compatibility, an empty string disables the product check.

`features` is an **opaque** bitmask for this library: the meaning of each bit belongs to your
product.

### Time check

```cpp
licmgr::LicenceValidator::SetTimeValidationMethod(licmgr::TimeValidationMethod::PreferInternetTime); // default
licmgr::LicenceValidator::SetNtpServer("time.windows.com"); // default
```

- `PreferInternetTime`: NTP request (UDP/123, 2 s); falls back to the local clock on failure.
- `UseInternetTime`: NTP required; on failure an expiring key is refused (`Expired`).
- `UseLocalTime`: local clock only.

The clock is only read for an authentic key that has an expiration date. A licence stays valid for
its whole expiration day (UTC). `ValidateAt(key, productId, unixTime, info)` validates at a given
time without reading any clock.

### Machine-bound licences (optional)

A licence can be bound to one machine, licence by licence: only licences issued with a machine code
are checked; the others work on any machine.

1. Your customer gets the **machine code** (16 characters, e.g. `WSZ6-G7W2-KQCT-CD3Q`):
   - with `LicenceManager.CodePoste.exe` (`tools/machinecode`, standalone, static CRT: it shows the
     code and copies it to the clipboard);
   - or directly in your product with `licmgr::LicenceValidator::GetMachineCode()`.
2. In the KeyGenerator, tick "Lier la licence à un poste" and enter that code.
3. On another machine, `Validate` refuses the key with `LicenceStatus::WrongMachine`;
   `info.machineCode` holds the expected machine.

The code is a truncated SHA-256 of the motherboard UUID (SMBIOS), or of the Windows `MachineGuid`
as a fallback: it survives a Windows reinstall but not a motherboard change. Details in
[`../FORMAT.md`](../FORMAT.md) section 5.

### Product-specific information (custom fields)

Products can receive information declared in the KeyGenerator's `products.json` (`fields`, see
`LicenceManager.KeyGenerator/PRODUCTS.md`). The key only holds the values, identified by number:
your product and `products.json` share the same numbering.

```cpp
std::string edition = "Standard";     // default when the field is missing
info.GetText(1, edition);
int32_t maxUsers = 1;
info.GetInt(2, maxUsers);
int64_t supportEnd = 0;               // start of day, Unix seconds UTC
bool hasSupport = info.GetDate(3, supportEnd);
```

Each read returns `false` when the field is missing or has another type, and leaves the variable
unchanged: initialise it with your product's default value.

## 3. What this module does not do

- **No private key, no key generation**: keys are issued with the .NET library or the KeyGenerator.
- No online activation: machine binding relies on exchanging the machine code.

## Project files

```
LicenceManagerCpp/
  include/LicenceValidator.h        public API
  src/KeyFormat.*                   key text (Crockford Base32, CRC-16) and field parsing
  src/Crypto.*                      SHA-256 and ECDSA P-256 verification (CNG; OpenSSL outside Windows)
  src/MachineIdentity.*             machine code (SMBIOS UUID, else MachineGuid)
  src/Base64.*                      public key decoding
  src/Ntp.*                         optional NTP time (Winsock)
  src/LicenceValidator.cpp          validation (FORMAT.md section 4)
  test/round_trip_test.cpp          .NET -> C++ cross-check (see test/README.md)
  test/test_vectors.h               keys issued by the .NET library (generated)
  tools/vectorgen/                  test_vectors.h generator
  tools/machinecode/                LicenceManager.CodePoste.exe customer tool
  CMakeLists.txt                    test build outside Windows
  LicenceManagerCpp.vcxproj         x64 static library / v145
  LicenceManagerCpp.nuspec          native NuGet package (header + Debug/Release .lib)
  nuget/JBOpale.LicenceManager.Cpp.targets   MSBuild integration for consumers
```
