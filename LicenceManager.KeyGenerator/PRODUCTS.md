# KeyGenerator product profiles

Products are declared in `%LOCALAPPDATA%/LicenceManager/KeyGenerator/products.json` (next to the
private key, outside any repository). On first launch (missing file or no product), it is created
with an example product to replace with your own. An invalid profile is skipped with a warning.

```json
{
  "products": [
    { "productId": 2000, "name": "Simple product" },

    { "productId": 2001, "name": "With modules",
      "modules": [ { "name": "OPTION_A", "bit": 0 }, { "name": "OPTION_B", "bit": 1 } ] },

    { "productId": 2002, "name": "Without features", "hasFeatures": false },

    { "productId": 2003, "name": "With custom fields",
      "fields": [
        { "id": 1, "name": "Edition",   "type": "text" },
        { "id": 2, "name": "MaxUsers",  "type": "int" },
        { "id": 3, "name": "SupportEnd","type": "date" },
        { "id": 4, "name": "DemoMode",  "type": "bool" },
        { "id": 5, "name": "Options",   "type": "mask" }
      ] }
  ]
}
```

- No `modules`: the feature bitmask is entered in hexadecimal only. With `modules`: one checkbox per
  module. Never reassign a bit used by issued keys.
- `"hasFeatures": false`: no feature input, the key's bitmask is always 0.
- `fields`: product-specific information (FORMAT.md section 6), one input per field. Types: `int`
  (32-bit integer), `bool`, `date`, `text` (up to 32 UTF-8 bytes), `mask` (64 bits, hexadecimal
  input). The `id` (1 to 255) is what your product reads from the key: like module bits, never
  reassign an id. An empty input leaves the field out of the key (except `bool`, always included).
  All fields of a key are limited to 64 bytes (number and size count 2 bytes per field).
- The key format is the same for every product (see `FORMAT.md` at the repository root).
- The signing key pair is unique: every product validates with the same public key.
