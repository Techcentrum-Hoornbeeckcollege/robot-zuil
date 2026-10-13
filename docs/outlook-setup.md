# Outlook / Graph setup

The kiosk reads six room mailboxes with an **app-only** credential, because
nobody signs in to a column in a lobby.

## 1. Register the application

Entra admin centre → App registrations → New registration.

- Name: `robot-zuil-kiosk`
- Supported account types: single tenant
- Redirect URI: none (there is no interactive sign-in)

Note the **Application (client) ID** and **Directory (tenant) ID**.

## 2. Grant application permission

API permissions → Microsoft Graph → **Application permissions** →
`Calendars.Read` → Grant admin consent.

## 3. Restrict it to the six mailboxes — do not skip this

`Calendars.Read` as an application permission means **every mailbox in the
tenant**. Scope it down with an application access policy:

```powershell
# Exchange Online PowerShell, as an Exchange admin.
New-DistributionGroup -Name "ZuilKioskRooms" -Type Security `
  -Members "zaal-0-01@example.com","zaal-0-02@example.com", `
           "zaal-1-01@example.com","zaal-1-02@example.com", `
           "zaal-2-01@example.com","zaal-2-02@example.com"

New-ApplicationAccessPolicy `
  -AppId "<client-id>" `
  -PolicyScopeGroupId "ZuilKioskRooms@example.com" `
  -AccessRight RestrictAccess `
  -Description "robot-zuil kiosk: six wayfinding rooms only"

# Verify, per mailbox:
Test-ApplicationAccessPolicy -Identity "zaal-0-01@example.com" -AppId "<client-id>"
Test-ApplicationAccessPolicy -Identity "someone.else@example.com" -AppId "<client-id>"
```

The second test must come back **denied**. Until it does, a device bolted to a
wall in a public building holds a credential that can read the whole
organisation's calendars — that is the single largest risk in this system.

Policy changes can take up to ~30 minutes to take effect.

## 4. Give the kiosk a credential

There are two ways, and they are not equally good.

### The admin panel (a client secret)

What most installations will do, and what needs no SSH. Create a secret under
**Certificates & secrets → Client secrets** in the app registration, then at the
kiosk itself: hold the bottom-right corner of the screen for three seconds, log
in, and fill in **Outlook** — tenant id, client id, client secret. *Opslaan en
testen* acquires a token and reads one event from the first room's calendar, so
you find out immediately rather than from an empty agenda.

The secret is encrypted with AES-GCM against a key file beside the database, is
never returned by the API, and cannot be read back from the panel. Be clear about
what that buys and does not: see [privacy.md](privacy.md#the-graph-credential).

**Set a calendar reminder for the secret's expiry.** Entra caps them at two
years, and the failure mode is a lobby screen quietly serving a stale cache.

### A certificate (stronger, and still supported)

A secret — wherever it is stored — is a bearer credential on an unattended
machine in a public building, and now sits behind an admin password that someone
has to have remembered to change. A certificate is better, and if a certificate
path is configured it **wins over** any stored secret, so a site that already
does this loses nothing by upgrading.

The *path* is a setting like any other, but the file itself has to be put on the
device by hand — that is the point of a certificate.

Generate a self-signed cert on the device:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
  -subj "/CN=robot-zuil-kiosk" \
  -keyout zuil-graph.key -out zuil-graph.crt

openssl pkcs12 -export -out zuil-graph.pfx \
  -inkey zuil-graph.key -in zuil-graph.crt
```

Upload `zuil-graph.crt` under Certificates & secrets → Certificates. Keep the
`.pfx` on the device at `/var/lib/zuil/zuil-graph.pfx`, mode `600`, owned by
`zuil` — that directory is the one the kiosk already owns and the one that
survives a release swap. The `.key`/`.pfx` are gitignored; never commit them.

Then fill in **Instellingen → Outlook**: the path in *Certificaatbestand* and
the export password in *Certificaatwachtwoord*. Both are stored like the client
secret — encrypted in the database with `secret.key` — and *Opslaan en testen*
tells you straight away whether Entra accepted the certificate. There is no
config file to edit and no restart.

**Set a calendar reminder for the certificate expiry.** A two-year cert expires
on a Tuesday eighteen months from now, and the failure mode is a lobby screen
quietly serving a stale cache.

## 5. Configure the rooms

Under **Instellingen → Zalen** on the kiosk: the mailbox address, display name,
floor, walking directions, and which indicator points at each room. Changes take
effect without a restart.

`config/rooms.json` is only the first-run seed — it is read once, on the very
first start, and editing it afterwards does nothing. It is still the easiest way
to pre-load six rooms before the device ever boots.

## 6. Verify

```bash
curl -s http://localhost:5000/api/health | jq
```

`lastSuccessfulSync` should be recent and `lastError` null. A `403` in
`lastError` almost always means the access policy has not propagated yet, or the
mailbox is not in the scope group.
