# Plugin secret storage with Windows DPAPI

## Outcome

BepisDB's `CfClearanceCookie` and Pixiv Authors' SauceNao API key are now encrypted at rest with Windows DPAPI in `CurrentUser` scope. Their in-memory values remain plaintext, so the existing cookie setup capability, plugin settings UI, reverse-image-search capability, and HTTP consumers keep the same behavior.

No Plugin SDK or application code changed. No Fanbox source, cache implementation, HTTP/fetcher logic, setting key, setting label, commit, or push changed.

Both plugin projects explicitly reference `System.Security.Cryptography.ProtectedData` version `10.0.9`. The publish output contains the dependency in each isolated plugin directory, and each plugin deps file records the same package version.

## Verified ownership boundary

The host creates a per-plugin storage directory and exposes it through `IPluginHost.StorageDirectory`; each plugin owns its `settings.json` read and write. `IPluginSettingsProvider` exposes only setting definitions plus `GetSettingValue` and `SetSettingValue`. Its `Secret` value type causes the host to use a masked `PasswordBox` but performs no serialization or encryption.

Pixiv exchanges the SauceNao key with the host as plaintext through `GetSettingValue` and `SetSettingValue`. The import flow retrieves that plaintext and passes it to the reverse-image-search provider. BepisDB does not expose its cookie as an ordinary `Secret` setting: the browser setup passes plaintext cookies through `ICookieSetupProvider.ApplyCookies`. Both plugins therefore have a complete interception point inside their own `PluginSettings` load and save methods.

## Persisted format

The existing JSON property names and object structures are unchanged. A non-empty secret is stored as one JSON string:

```text
dpapi:v1:<base64 DPAPI payload>
```

`dpapi:v1:` is the reserved format and version marker. The helper assumes that a legacy plaintext cookie or API key will not naturally begin with this prefix. This accepted collision risk is documented in the helper XML remarks. A prefixed value that is not valid base64 or cannot be decrypted follows the normal decryption-failure path; it is not reinterpreted as legacy plaintext.

Null remains omitted under the existing `JsonIgnoreCondition.WhenWritingNull` behavior. An empty string remains an empty string and is not tagged or encrypted.

The two repositories contain a `DpapiSecretProtector` implementation that is byte-for-byte identical after replacing only the namespace. Keeping the copies aligned is intentional preparation for moving the helper into the future SDK/Common NuGet package.

## Migration semantics

On load, a non-empty value without the reserved prefix is treated as legacy plaintext and remains immediately usable in memory. Loading does not write the settings file. The next existing plugin save serializes the current plaintext through DPAPI, so changing any setting or completing cookie setup migrates the legacy value without adding startup I/O.

Known limitation: a user who loads a legacy secret but never triggers any existing save path can keep the plaintext value on disk indefinitely. This is accepted because forced startup migration would add a new write and failure mode to initialization. Normal cookie setup and settings editing already use the save paths that perform migration.

## Failure and logging behavior

Invalid base64 and DPAPI decryption failures return null rather than throwing through plugin initialization. This covers damaged or forged data and values copied from another computer or Windows user. Existing behavior then treats the cookie or API key as unset and asks the user to configure it again.

Warnings contain only the JSON field name, a fixed failure category, and the reconfiguration guidance. They never include exception messages, plaintext, ciphertext, base64 payloads, or fragments of those values.

## Automated coverage

Each plugin executes real DPAPI calls under the current Windows test user. Tests cover round-trip protection, legacy pass-through and migration marking, no immediate rewrite, encryption on the next save, reload after migration, valid-base64 payload tampering, invalid base64, null and empty values, unset fallback, and exact safe warning text. Every settings-file test writes only synthetic values below a GUID-named test directory under the system temporary folder and deletes that directory on disposal; no real plugin storage directory is used.

Final verification results:

```text
BepisDB tests:                  58 passed, 0 failed, 0 skipped
Pixiv Authors tests:            29 passed, 0 failed, 0 skipped
BepisDB Release build:          succeeded, 0 warnings, 0 errors
Pixiv Authors Release build:    succeeded, 0 warnings, 0 errors
Main application tests:         79 passed, 0 failed, 0 skipped
Plugin load smoke tests:         5 passed, 0 failed, 0 skipped
Full win-x64 publication:       succeeded with all four plugins
```

The application, smoke project, and all four plugin projects were restored for `win-x64` before smoke testing. `Publish-WithPlugins.ps1 -NoRestore` then rebuilt the application and all plugin directories. The BepisDB and Pixiv Authors output directories each contain `System.Security.Cryptography.ProtectedData.dll` and do not contain duplicate Plugin SDK or Core assemblies.

Both plugin suites also passed with `SceneGalleryAppDir` pointed at a deliberately missing directory, proving that their standalone CI checkout shape still builds and tests against the repository-local fallback SDK DLL. The two plugin projects were restored for `win-x64` again afterward so their final asset graphs retain the RID required by smoke tests and publication.

## Future SDK/Common package extraction

Move the helper without changing the `dpapi:v1:` prefix, UTF-8 encoding, null/empty behavior, `CurrentUser` scope, prefix-collision assumption, legacy migration signal, or fixed safe-warning contract. Existing ciphertext must remain readable after extraction. Centralize the explicitly pinned ProtectedData version at the same time, then remove the two plugin-local package references only after both standalone fallback-SDK builds and the combined plugin smoke test prove dependency resolution.
