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

## 4. Use a certificate, not a secret

A client secret in a config file next to an unattended kiosk is a credential
anyone with physical access can walk off with. Generate a self-signed cert on
the device:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
  -subj "/CN=robot-zuil-kiosk" \
  -keyout zuil-graph.key -out zuil-graph.crt

openssl pkcs12 -export -out zuil-graph.pfx \
  -inkey zuil-graph.key -in zuil-graph.crt
```

Upload `zuil-graph.crt` under Certificates & secrets → Certificates. Keep the
`.pfx` on the device at `/opt/zuil/config/zuil-graph.pfx`, mode `600`, owned by
`zuil`. The `.key`/`.pfx` are gitignored; never commit them.

Then in `config/appsettings.Production.json`:

```json
{
  "Graph": {
    "TenantId": "…",
    "ClientId": "…",
    "CertificatePath": "/opt/zuil/config/zuil-graph.pfx",
    "CertificatePassword": "…",
    "TimeZone": "Europe/Amsterdam"
  }
}
```

**Set a calendar reminder for the certificate expiry.** A two-year cert expires
on a Tuesday eighteen months from now, and the failure mode is a lobby screen
quietly serving a stale cache.

## 5. Configure the rooms

Put the six mailbox addresses in `config/rooms.json`, with the display name,
floor, walking directions and the `signalId` of the indicator that points at
each one.

## 6. Verify

```bash
curl -s http://localhost:5000/api/health | jq
```

`lastSuccessfulSync` should be recent and `lastError` null. A `403` in
`lastError` almost always means the access policy has not propagated yet, or the
mailbox is not in the scope group.
