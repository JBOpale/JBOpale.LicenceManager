# LicenceManager key format (version 1)

Shared specification of the .NET (`LicenceManager/`) and C++ (`LicenceManagerCpp/`) implementations.
Any change goes through a new version byte: a version 1 key must stay valid forever.

## 1. Signed bytes (payload)

Unsigned integers are **little-endian**.

| Offset | Size | Field | Description |
|---|---|---|---|
| 0 | 1 | `version` | `1` |
| 1 | 4 | `productId` | Product identifier (32-bit integer) |
| 5 | 2 | `expiration` | Days since 2000-01-01 (UTC) of the **last valid day**; `0` = never expires |
| 7 | 8 | `features` | Feature bitmask; the meaning of each bit belongs to the product |
| 15 | 8 | `licenceId` | Unique identifier, random and non-zero when issued |
| 23 | 2 | `seats` | Number of seats, `0` = unlimited |
| 25 | 8 | `machine` | Machine code (section 5); 8 zero bytes = usable on any machine |
| 33 | 1 | `clientLength` | `n`, from 0 to 40 |
| 34 | n | `client` | Customer name, UTF-8 |
| 34 + n | 1 | `fieldsLength` | `m`, size of the custom fields block, from 0 to 64 |
| 35 + n | m | `fields` | Custom fields (section 6) |

Payload size: `35 + n + m` bytes. Any other length is a malformed key.

A licence is expired when the current UTC date is **strictly after** the `expiration` day: it stays
valid for the whole day.

## 2. Signature

ECDSA on curve **NIST P-256** (secp256r1), **SHA-256** digest of the payload.
The signature is **64 bytes**: `r` then `s`, 32 bytes each, big-endian (IEEE P1363 format, as
produced by `ECDsa.SignData` in .NET and `BCryptSignHash` in CNG).

Public key: `X` then `Y`, 32 bytes each, big-endian, i.e. 64 bytes, exchanged as Base64
(88 characters). Displayed fingerprint: first 8 bytes of the SHA-256 of those 64 bytes, in hex.

## 3. Key text

1. `raw = payload || signature`.
2. Append the **CRC-16/CCITT-FALSE** of `raw` (polynomial `0x1021`, initial value `0xFFFF`, no
   reflection, no final XOR; `"123456789"` gives `0x29B1`), as 2 **big-endian** bytes.
3. **Crockford Base32** encoding (`0123456789ABCDEFGHJKMNPQRSTVWXYZ`), most significant bits first,
   last character padded with zero bits.
4. Groups of **6 characters** separated by `-`.

Lenient reading: dashes and whitespace ignored, lowercase accepted, `O` read as `0`, `I` and `L`
read as `1`. Padding bits must be zero and fewer than 5 of them may remain.

Size example: 20-byte customer name, no custom field: 35 + 20 + 64 + 2 = 121 bytes, i.e.
194 characters. Each custom field byte adds about 1.6 characters.

## 4. Validation order and statuses

1. Base32 decoding and CRC -> otherwise **Malformed**.
2. `version == 1` -> otherwise **UnsupportedVersion**.
3. Consistent lengths (`clientLength`, `fieldsLength`, field block of section 6) -> otherwise **Malformed**.
4. Signature -> otherwise **InvalidSignature**.
5. Expected `productId` -> otherwise **WrongProduct**.
6. If `machine` is not zero: same code as the current machine -> otherwise **WrongMachine**
   (also when the current machine cannot be identified).
7. Expiration -> otherwise **Expired**.
8. **Valid**.

## 5. Machine code

Machine binding is optional, per licence: only licences issued with a machine code are checked.

**Identifier** (8 bytes): the first 8 bytes of the SHA-256 of `prefix || value`, with, in order of
preference:

| Source | Prefix (ASCII) | Value |
|---|---|---|
| A. Motherboard UUID | `LicenceManager.Machine/1:A:` | The 16 raw bytes of the UUID field (offset 8) of the first SMBIOS type 1 structure, as returned by `GetSystemFirmwareTable('RSMB')` |
| B. Windows installation | `LicenceManager.Machine/1:B:` | `MachineGuid` from `HKLM\SOFTWARE\Microsoft\Cryptography` (64-bit view), trimmed, lowercase, UTF-8 |

Source A is skipped when the SMBIOS version is older than 2.1, when the type 1 structure is
shorter than `0x19` bytes, or when the UUID is `00..00`, `FF..FF` or the common placeholder
`03000200-0400-0500-0006-000700080009` (raw bytes `00 02 00 03 00 04 00 05 00 06 00 07 00 08 00 09`).
An all-zero identifier is reserved and treated as missing.

**Text**: the 8 bytes + CRC-16, encoded like a key (section 3) but in groups of **4 characters**,
i.e. 16 characters (e.g. `WSZ6-G7W2-KQCT-CD3Q`). Same lenient reading.

## 6. Custom fields

Product-specific information, declared in the KeyGenerator (`products.json`, `fields`). The key
only holds the values, each prefixed by its number; names and types are known by the product and
the KeyGenerator, like the bits of `features`.

Block of `m` bytes (0 to 64), sequence of entries:

| Size | Content |
|---|---|
| 1 | Field number, 1 to 255, **strictly increasing** from one entry to the next |
| 1 | Value size `k` |
| k | Value |

The block is malformed if an entry overflows it, or if a number is 0 or not strictly greater than
the previous one. A missing field is not an error: the product picks its default value.

Types (value encoding, little-endian integers):

| Type | Size | Value |
|---|---|---|
| `int` | 4 | Signed 32-bit integer |
| `bool` | 1 | `0` or `1` |
| `date` | 2 | Days since 2000-01-01 (UTC), 1 to 65535 |
| `text` | 0 to 32 | UTF-8 |
| `mask` | 8 | Unsigned 64-bit integer |

A typed read fails when the size does not match the requested type (or, for `bool`, when the value
is neither 0 nor 1; for `date`, when it is 0).
