import { useState } from 'react';
import { api } from '../../api/client';
import type {
  AdminFieldError,
  AdminGraphSettings,
  AdminSettingsUpdate,
  AdminTestResult,
} from '../../api/types';
import { errorFor, Field, SaveActions, TextField } from './fields';

interface Props {
  settings: AdminGraphSettings;
  errors: AdminFieldError[];
  busy: boolean;
  saved: boolean;
  onSave: (update: AdminSettingsUpdate) => void;
}

/**
 * Tenant, client id and client secret.
 *
 * The secret is write-only: the server never returns it, so this form cannot
 * show it and does not pretend to. It shows whether one is stored, and offers
 * Vervangen or Wissen as explicit actions, which map to the secretAction
 * discriminator on the API — rather than relying on "field left blank means
 * keep", which is the kind of thing that silently wipes a credential.
 */
export function GraphSettingsForm({ settings, errors, busy, saved, onSave }: Props) {
  const [tenantId, setTenantId] = useState(settings.tenantId);
  const [clientId, setClientId] = useState(settings.clientId);
  const [timeZone, setTimeZone] = useState(settings.timeZone);
  const [replacing, setReplacing] = useState(!settings.secretIsSet);
  const [secret, setSecret] = useState('');
  const [certificatePath, setCertificatePath] = useState(settings.certificatePath);
  const [certificatePassword, setCertificatePassword] = useState('');
  const [test, setTest] = useState<AdminTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const dirty =
    tenantId !== settings.tenantId ||
    clientId !== settings.clientId ||
    timeZone !== settings.timeZone ||
    certificatePath !== settings.certificatePath ||
    certificatePassword.length > 0 ||
    (replacing && secret.length > 0);

  function save() {
    setTest(null);
    onSave({
      graph: {
        tenantId,
        clientId,
        timeZone,
        baseUrl: settings.baseUrl,
        certificatePath,
        secretAction: replacing && secret.length > 0 ? 'Set' : 'Keep',
        secret: replacing && secret.length > 0 ? secret : undefined,
        certificatePasswordAction: certificatePassword.length > 0 ? 'Set' : 'Keep',
        certificatePassword: certificatePassword.length > 0 ? certificatePassword : undefined,
      },
    });
    setSecret('');
    setCertificatePassword('');
  }

  function clearSecret() {
    setTest(null);
    onSave({
      graph: {
        tenantId,
        clientId,
        timeZone,
        baseUrl: settings.baseUrl,
        certificatePath,
        secretAction: 'Clear',
        certificatePasswordAction: 'Keep',
      },
    });
    setSecret('');
    setReplacing(true);
  }

  async function runTest() {
    setTesting(true);
    try {
      // Tests what is saved, not what is typed: see GraphConnectionTester.
      setTest(await api.adminTestGraph());
    } catch {
      setTest({ ok: false, message: 'De test kon niet worden uitgevoerd.' });
    } finally {
      setTesting(false);
    }
  }

  return (
    <section className="admin-section">
      <TextField
        id="graph-tenant"
        label="Tenant-id"
        value={tenantId}
        onChange={setTenantId}
        placeholder="00000000-0000-0000-0000-000000000000"
        error={errorFor(errors, 'graph.tenantId')}
      />

      <TextField
        id="graph-client"
        label="Client-id"
        value={clientId}
        onChange={setClientId}
        placeholder="00000000-0000-0000-0000-000000000000"
        error={errorFor(errors, 'graph.clientId')}
      />

      <Field
          label="Client secret"
          htmlFor="graph-secret"
          error={errorFor(errors, 'graph.secret')}
          hint={
            settings.secretIsSet
              ? 'Er is een secret opgeslagen. De waarde wordt nooit teruggegeven.'
              : 'Nog geen secret opgeslagen. Outlook werkt pas als deze is ingevuld.'
          }
        >
          {replacing ? (
            <input
              id="graph-secret"
              className="input"
              type="password"
              value={secret}
              placeholder="Plak de secret uit Entra"
              autoComplete="new-password"
              onChange={(e) => setSecret(e.target.value)}
            />
          ) : (
            <div className="admin-secret">
              <span className="admin-secret-mask">••••••••</span>
              <button
                className="button button-secondary"
                type="button"
                onClick={() => setReplacing(true)}
              >
                Vervangen
              </button>
              <button
                className="button button-secondary"
                type="button"
                disabled={busy}
                onClick={clearSecret}
              >
                Wissen
              </button>
            </div>
          )}
      </Field>

      <TextField
        id="graph-certificate"
        label="Certificaat (optioneel)"
        value={certificatePath}
        onChange={setCertificatePath}
        placeholder="/var/lib/zuil/zuil-graph.pfx"
        hint={
          settings.authMode === 'certificate'
            ? 'In gebruik. Een certificaat gaat voor op de secret hierboven.'
            : 'Pad naar een .pfx op dit apparaat. Sterker dan een secret voor een onbeheerde zuil; laat leeg om de secret te gebruiken.'
        }
        error={errorFor(errors, 'graph.certificatePath')}
      />

      {certificatePath.length > 0 && (
        <TextField
          id="graph-certificate-password"
          label="Wachtwoord van het certificaat"
          type="password"
          value={certificatePassword}
          onChange={setCertificatePassword}
          placeholder={settings.certificatePasswordIsSet ? '••••••••' : 'Leeg laten als er geen is'}
          hint={
            settings.certificatePasswordIsSet
              ? 'Er is er een opgeslagen. Laat leeg om die te behouden.'
              : undefined
          }
        />
      )}

      <TextField
        id="graph-timezone"
        label="Tijdzone"
        value={timeZone}
        onChange={setTimeZone}
        placeholder="Europe/Amsterdam"
        hint="IANA-naam. Bepaalt ook wanneer een dag begint op het agendabord."
        error={errorFor(errors, 'graph.timeZone')}
      />

      <SaveActions busy={busy} dirty={dirty} saved={saved} onSave={save}>
        <button
          className="button button-secondary"
          type="button"
          disabled={busy || testing || dirty}
          onClick={runTest}
          title={dirty ? 'Sla eerst op; de test gebruikt de opgeslagen gegevens.' : undefined}
        >
          {testing ? 'Testen…' : 'Verbinding testen'}
        </button>
      </SaveActions>

      {test && (
        <p className={test.ok ? 'admin-test-ok' : 'error'}>{test.message}</p>
      )}
    </section>
  );
}
