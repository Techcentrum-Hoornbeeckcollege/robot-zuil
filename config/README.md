# config/

These files ship **next to** the binary (`/opt/zuil/config/` on the Pi), not
inside it.

**Most settings no longer live here.** Rooms, Outlook credentials, sync timing,
device parameters, privacy and display settings are now in the database and are
edited from the kiosk itself — hold the bottom-right corner of the screen for
three seconds. See [../docs/admin.md](../docs/admin.md).

| File | Purpose |
|---|---|
| `rooms.json` | **First-run seed only.** Read once, on the very first start, to populate the room table. Editing it afterwards does nothing; use Instellingen → Zalen. Still useful as a starting point, and as somewhere to paste the admin panel's export back into. |
| `appsettings.Production.json` | What genuinely cannot change at runtime: the admin password, the database path, the Kestrel URL, the Chromium path, and a certificate path if you use one. **Gitignored** — copy from the `.example`. |
| `appsettings.Production.json.example` | Template, safe to commit. |

The binary also carries a built-in `appsettings.json`; anything here overrides
it, and both are overridden by `ZUIL_`-prefixed environment variables.

## What stays in a file, and why

| Setting | Why not in the GUI |
|---|---|
| `Admin:Password` | A password a visitor could change from the touchscreen is not a password. |
| `Kestrel:Endpoints:Http:Url` | It binds loopback only. Making it editable would turn a plaintext default password into a LAN credential. |
| `Storage:DatabasePath` | The settings live in that database; it cannot configure its own location. |
| `Kiosk:*` | Which browser to launch, and whether to. Only relevant before the UI exists. |
| `Graph:CertificatePath` | A certificate is a file placed on the device by whoever set it up. It wins over a stored secret, so a hardened site loses nothing by upgrading. |

## Upgrading an existing installation

On the first start after the update, `FirstRunSeeder` copies the old values out
of these files into the database — the rooms from `rooms.json`, and the Graph
tenant, client id and **client secret** from `appsettings.Production.json`, which
gets encrypted on the way in. Behaviour is unchanged.

Afterwards, remove `Graph:ClientSecret` from `appsettings.Production.json`: it is
no longer read, and a credential in a plaintext file next to an unattended kiosk
is exactly what moving it into the database was for.

The seed happens exactly once, guarded by a marker row rather than by the room
table being empty — so deleting every room from the admin panel does not bring
them back on the next restart.

## Two other files appear here at runtime

| File | What it is |
|---|---|
| `../zuil-cache.db` | The cached calendar window, the settings, the rooms, and the encrypted Graph secret. Gitignored. Sensitive: it holds a credential. |
| `../secret.key` | The 32-byte AES-GCM key for that secret, mode `0600`. Gitignored. |

**Back those two up together.** Either one alone cannot recover the Graph
credential, and an update must clobber neither. If `secret.key` is lost, the
kiosk reports Outlook as not configured and an administrator re-enters the
secret — it does not crash-loop.
