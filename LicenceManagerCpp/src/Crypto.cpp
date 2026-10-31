#include "Crypto.h"

#include <cstring>

#ifdef _WIN32

#ifndef NOMINMAX
#define NOMINMAX // windows.h definirait sinon des macros min/max qui cassent std::min
#endif
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")

#ifndef NT_SUCCESS
#define NT_SUCCESS(status) (((NTSTATUS)(status)) >= 0)
#endif

namespace licmgr {

namespace {

// Fournisseur d'algorithme ferme automatiquement.
struct Algorithm {
    BCRYPT_ALG_HANDLE handle = nullptr;
    explicit Algorithm(LPCWSTR id) {
        if (!NT_SUCCESS(BCryptOpenAlgorithmProvider(&handle, id, nullptr, 0))) handle = nullptr;
    }
    ~Algorithm() {
        if (handle) BCryptCloseAlgorithmProvider(handle, 0);
    }
};

// Sans BCRYPT_NO_KEY_VALIDATION, CNG verifie a l'import que le point est sur la courbe.
BCRYPT_KEY_HANDLE ImportPublicKey(BCRYPT_ALG_HANDLE alg, const uint8_t pub[64]) {
    uint8_t blob[sizeof(BCRYPT_ECCKEY_BLOB) + 64];
    auto* header = reinterpret_cast<BCRYPT_ECCKEY_BLOB*>(blob);
    header->dwMagic = BCRYPT_ECDSA_PUBLIC_P256_MAGIC;
    header->cbKey = 32;
    std::memcpy(blob + sizeof(BCRYPT_ECCKEY_BLOB), pub, 64);

    BCRYPT_KEY_HANDLE key = nullptr;
    if (!NT_SUCCESS(BCryptImportKeyPair(alg, nullptr, BCRYPT_ECCPUBLIC_BLOB, &key, blob, sizeof(blob), 0)))
        return nullptr;
    return key;
}

} // namespace

bool Sha256(const uint8_t* data, size_t size, uint8_t digest[32]) {
    Algorithm sha(BCRYPT_SHA256_ALGORITHM);
    if (!sha.handle || size > 0xFFFFFFFFu) return false;

    BCRYPT_HASH_HANDLE hash = nullptr;
    bool ok = NT_SUCCESS(BCryptCreateHash(sha.handle, &hash, nullptr, 0, nullptr, 0, 0))
           && NT_SUCCESS(BCryptHashData(hash, const_cast<PUCHAR>(data), (ULONG)size, 0))
           && NT_SUCCESS(BCryptFinishHash(hash, digest, 32, 0));
    if (hash) BCryptDestroyHash(hash);
    return ok;
}

bool IsValidP256PublicKey(const uint8_t pub[64]) {
    Algorithm ecdsa(BCRYPT_ECDSA_P256_ALGORITHM);
    if (!ecdsa.handle) return false;
    BCRYPT_KEY_HANDLE key = ImportPublicKey(ecdsa.handle, pub);
    if (!key) return false;
    BCryptDestroyKey(key);
    return true;
}

bool VerifyP256Sha256(const uint8_t pub[64], const uint8_t* data, size_t size, const uint8_t signature[64]) {
    uint8_t digest[32];
    if (!Sha256(data, size, digest)) return false;

    Algorithm ecdsa(BCRYPT_ECDSA_P256_ALGORITHM);
    if (!ecdsa.handle) return false;
    BCRYPT_KEY_HANDLE key = ImportPublicKey(ecdsa.handle, pub);
    if (!key) return false;

    NTSTATUS status = BCryptVerifySignature(key, nullptr, digest, sizeof(digest),
                                            const_cast<PUCHAR>(signature), 64, 0);
    BCryptDestroyKey(key);
    return NT_SUCCESS(status);
}

} // namespace licmgr

#else // OpenSSL 3 : compilation et tests hors Windows uniquement

#include <openssl/bn.h>
#include <openssl/core_names.h>
#include <openssl/ecdsa.h>
#include <openssl/evp.h>
#include <openssl/params.h>

namespace licmgr {

namespace {

EVP_PKEY* LoadPublicKey(const uint8_t pub[64]) {
    uint8_t point[65];
    point[0] = 0x04; // point non compresse
    std::memcpy(point + 1, pub, 64);

    char group[] = "prime256v1";
    OSSL_PARAM params[] = {
        OSSL_PARAM_construct_utf8_string(OSSL_PKEY_PARAM_GROUP_NAME, group, 0),
        OSSL_PARAM_construct_octet_string(OSSL_PKEY_PARAM_PUB_KEY, point, sizeof(point)),
        OSSL_PARAM_construct_end(),
    };

    EVP_PKEY* pkey = nullptr;
    EVP_PKEY_CTX* ctx = EVP_PKEY_CTX_new_from_name(nullptr, "EC", nullptr);
    if (ctx && EVP_PKEY_fromdata_init(ctx) > 0)
        EVP_PKEY_fromdata(ctx, &pkey, EVP_PKEY_PUBLIC_KEY, params);
    EVP_PKEY_CTX_free(ctx);
    if (!pkey) return nullptr;

    EVP_PKEY_CTX* check = EVP_PKEY_CTX_new_from_pkey(nullptr, pkey, nullptr);
    bool onCurve = check && EVP_PKEY_public_check(check) == 1;
    EVP_PKEY_CTX_free(check);
    if (!onCurve) {
        EVP_PKEY_free(pkey);
        return nullptr;
    }
    return pkey;
}

} // namespace

bool Sha256(const uint8_t* data, size_t size, uint8_t digest[32]) {
    unsigned int length = 0;
    return EVP_Digest(data, size, digest, &length, EVP_sha256(), nullptr) == 1 && length == 32;
}

bool IsValidP256PublicKey(const uint8_t pub[64]) {
    EVP_PKEY* pkey = LoadPublicKey(pub);
    EVP_PKEY_free(pkey);
    return pkey != nullptr;
}

bool VerifyP256Sha256(const uint8_t pub[64], const uint8_t* data, size_t size, const uint8_t signature[64]) {
    EVP_PKEY* pkey = LoadPublicKey(pub);
    if (!pkey) return false;

    // OpenSSL attend une signature DER : conversion depuis r || s.
    ECDSA_SIG* sig = ECDSA_SIG_new();
    BIGNUM* r = BN_bin2bn(signature, 32, nullptr);
    BIGNUM* s = BN_bin2bn(signature + 32, 32, nullptr);
    unsigned char* der = nullptr;
    int derSize = -1;
    if (sig && r && s && ECDSA_SIG_set0(sig, r, s) == 1) {
        r = s = nullptr; // appartiennent desormais a sig
        derSize = i2d_ECDSA_SIG(sig, &der);
    }
    BN_free(r);
    BN_free(s);
    ECDSA_SIG_free(sig);

    bool ok = false;
    EVP_MD_CTX* md = EVP_MD_CTX_new();
    if (derSize > 0 && md && EVP_DigestVerifyInit(md, nullptr, EVP_sha256(), nullptr, pkey) == 1)
        ok = EVP_DigestVerify(md, der, (size_t)derSize, data, size) == 1;

    EVP_MD_CTX_free(md);
    OPENSSL_free(der);
    EVP_PKEY_free(pkey);
    return ok;
}

} // namespace licmgr

#endif
