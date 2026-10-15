import { useState } from 'react';
import type {
  AdminBrowserSettings,
  AdminFieldError,
  AdminNetworkSettings,
  AdminSecuritySettings,
  AdminSettingsUpdate,
} from '../../api/types';
import { errorFor, Field, NumberField, SaveActions, TextField, ToggleField } from './fields';

interface Props {
  security: AdminSecuritySettings;
  network: AdminNetworkSettings;
  browser: AdminBrowserSettings;
  errors: AdminFieldError[];
  busy: boolean;
  saved: boolean;
  onSave: (update: AdminSettingsUpdate) => void;
}

/**
 * The settings that used to be in a config file: session handling, the port,
 * and the browser the host launches. Everything here needs a restart except the
 * session rules.
 */
export function SystemSettingsForm({
  security,
  network,
  browser,
  errors,
  busy,
  saved,
  onSave,
}: Props) {
  const [draftSecurity, setSecurity] = useState(security);
  const [port, setPort] = useState(network.port);
  const [draftBrowser, setBrowser] = useState(browser);

  const dirty =
    draftSecurity.sessionIdleTimeoutMinutes !== security.sessionIdleTimeoutMinutes ||
    draftSecurity.maxFailedAttempts !== security.maxFailedAttempts ||
    draftSecurity.lockoutWindowMinutes !== security.lockoutWindowMinutes ||
    port !== network.port ||
    draftBrowser.launchBrowser !== browser.launchBrowser ||
    draftBrowser.browserPath !== browser.browserPath;

  return (
    <section className="admin-section">
      <NumberField
        id="security-idle"
        label="Beheersessie verloopt na"
        unit="minuten"
        min={1}
        max={480}
        value={draftSecurity.sessionIdleTimeoutMinutes}
        onChange={(v) => setSecurity((p) => ({ ...p, sessionIdleTimeoutMinutes: v }))}
        hint="Zonder activiteit. Het beheerscherm sluit zichzelf al eerder."
        error={errorFor(errors, 'security.sessionIdleTimeout')}
      />

      <NumberField
        id="security-attempts"
        label="Pogingen voor blokkade"
        unit="keer"
        min={1}
        max={100}
        value={draftSecurity.maxFailedAttempts}
        onChange={(v) => setSecurity((p) => ({ ...p, maxFailedAttempts: v }))}
        hint="Mislukte inlogpogingen worden in de database bijgehouden, dus de stekker eruit trekken helpt niet."
        error={errorFor(errors, 'security.maxFailedAttempts')}
      />

      <NumberField
        id="security-lockout"
        label="Blokkade duurt"
        unit="minuten"
        min={1}
        max={1440}
        value={draftSecurity.lockoutWindowMinutes}
        onChange={(v) => setSecurity((p) => ({ ...p, lockoutWindowMinutes: v }))}
        error={errorFor(errors, 'security.lockoutWindow')}
      />

      <NumberField
        id="network-port"
        label="Poort"
        unit="poortnummer"
        min={1024}
        max={65535}
        restartRequired
        value={port}
        onChange={setPort}
        hint={`De zuil luistert alleen op ${network.host}. Dat adres staat vast: het beheerscherm op het netwerk zetten zou het achter niets meer dan dit wachtwoord plaatsen.`}
        error={errorFor(errors, 'network.port')}
      />

      <Field label="Adres" hint="Vast, en niet instelbaar.">
        <p className="admin-readonly">{network.host}</p>
      </Field>

      <ToggleField
        label="Browser zelf starten"
        value={draftBrowser.launchBrowser}
        onChange={(v) => setBrowser((p) => ({ ...p, launchBrowser: v }))}
        hint="Uit laten op de Pi: daar start zuil-kiosk.service Chromium."
      />

      {draftBrowser.launchBrowser && (
        <TextField
          id="browser-path"
          label="Pad naar de browser"
          value={draftBrowser.browserPath}
          onChange={(v) => setBrowser((p) => ({ ...p, browserPath: v }))}
          placeholder="chromium-browser"
          error={errorFor(errors, 'browser.browserPath')}
        />
      )}

      <SaveActions
        busy={busy}
        dirty={dirty}
        saved={saved}
        onSave={() =>
          onSave({
            security: draftSecurity,
            network: { port },
            browser: draftBrowser,
          })
        }
      />
    </section>
  );
}
