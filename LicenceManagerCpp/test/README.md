# .NET -> C++ cross-check

`round_trip_test.cpp` checks that the C++ validator reads keys exactly like the .NET library. The
keys in `test_vectors.h` are issued by the **real** .NET library (`LicenceIssuer`) with a
throw-away key pair, then re-validated by the .NET `LicenceValidator` before being written: the C++
code must return the same status and read the same fields.

Covered cases: complete key (customer name with accents, `&` and quotes), last valid day, expired
the next day, other product, lenient input (lowercase, spaces, `O`/`l`), extreme values (no
expiration, mask and ID at 2^64-1, negative product ID, 40-byte customer name), mask altered with
a recomputed CRC, unknown version, inconsistent lengths, typo, other key pair, truncated key,
forbidden character, empty key, machine-bound keys (right machine, other machine, unknown machine),
custom fields of every type and corrupted field blocks. Plus the compatibility API
(`Validate(key, "1000", info)`), configuration errors and the machine code derivation.

## Run the tests

- **Windows**: build `LicenceManagerCpp.sln` (x64), then run `bin\x64\Release\LicenceManagerCpp.Tests.exe`.
- **Linux** (ECDSA through OpenSSL 3, see `src/Crypto.cpp`):

  ```bash
  cmake -S LicenceManagerCpp -B build && cmake --build build && ./build/LicenceManagerCpp.Tests
  ```

## Regenerate the vectors

After any format change (and therefore any change to `FORMAT.md`), from the repository root:

```bash
dotnet run --project LicenceManagerCpp/tools/vectorgen -- LicenceManagerCpp/test/test_vectors.h
```

Each run draws a new throw-away key pair: the whole file changes, which is expected.
