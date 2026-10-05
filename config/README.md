# config/

These files ship **next to** the binary (`/opt/zuil/config/` on the Pi), not
inside it:

| File | Purpose |
|---|---|
| `rooms.json` | The six rooms, their mailboxes, and the walking directions shown to visitors. Edit and restart; no rebuild needed. |
| `appsettings.Production.json` | Entra tenant/client id and the certificate path. **Gitignored** — copy from the `.example` and fill in on the device. |
| `appsettings.Production.json.example` | Template, safe to commit. |

The binary also carries a built-in `appsettings.json` with defaults; anything
here overrides it.
