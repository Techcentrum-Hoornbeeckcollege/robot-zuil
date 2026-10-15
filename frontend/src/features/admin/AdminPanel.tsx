import { useCallback, useEffect, useState } from 'react';
import { api, ApiError } from '../../api/client';
import type { AdminFieldError, AdminRoom, AdminSettings, AdminSettingsUpdate } from '../../api/types';
import { useIdleReset } from '../../hooks/useIdleReset';
import { ADMIN_TABS } from '../../shell/viewState';
import type { AdminTab } from '../../shell/viewState';
import { AccountForm } from './AccountForm';
import { DeviceSettingsForm } from './DeviceSettingsForm';
import { DiagnosticsPanel } from './DiagnosticsPanel';
import { DisplaySettingsForm } from './DisplaySettingsForm';
import { GraphSettingsForm } from './GraphSettingsForm';
import { PrivacySettingsForm } from './PrivacySettingsForm';
import { RoomsEditor } from './RoomsEditor';
import { SyncSettingsForm } from './SyncSettingsForm';
import { SystemSettingsForm } from './SystemSettingsForm';

interface Props {
  tab: AdminTab;
  onTab: (tab: AdminTab) => void;
  onLeave: () => void;
  /** Called after a save, so the kiosk can pick up a changed idle timeout. */
  onChanged: () => void;
}

/**
 * Two minutes of inactivity in here and we leave.
 *
 * Shorter than the visitor-facing timeout, because a settings panel left open
 * on a lobby kiosk is worse than an attract screen left open. The server's
 * sliding 15-minute session expiry is the real guard; this is so the screen does
 * not sit there advertising itself.
 */
const ADMIN_IDLE_MS = 120_000;

export function AdminPanel({ tab, onTab, onLeave, onChanged }: Props) {
  const [settings, setSettings] = useState<AdminSettings | null>(null);
  const [errors, setErrors] = useState<AdminFieldError[]>([]);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);

  useEffect(() => {
    const controller = new AbortController();

    api
      .adminSettings(controller.signal)
      .then(setSettings)
      .catch((caught: unknown) => {
        if (caught instanceof ApiError && caught.status === 401) {
          setExpired(true);
        } else if (!controller.signal.aborted) {
          setFailure('De instellingen konden niet worden geladen.');
        }
      });

    return () => controller.abort();
  }, []);

  const leave = useCallback(() => {
    void api.adminLogout();
    onLeave();
  }, [onLeave]);

  useIdleReset(ADMIN_IDLE_MS, leave);

  const save = useCallback(
    async (update: AdminSettingsUpdate) => {
      setBusy(true);
      setErrors([]);
      setFailure(null);
      setSaved(false);

      try {
        setSettings(await api.adminSaveSettings(update));
        setSaved(true);
        onChanged();
      } catch (caught) {
        if (caught instanceof ApiError && caught.status === 400) {
          setErrors(parseErrors(caught.body));
        } else if (caught instanceof ApiError && caught.status === 401) {
          setExpired(true);
        } else {
          setFailure('Opslaan is niet gelukt.');
        }
      } finally {
        setBusy(false);
      }
    },
    [onChanged],
  );

  const roomsSaved = useCallback(
    (rooms: AdminRoom[]) => {
      setSettings((previous) => (previous ? { ...previous, rooms } : previous));
      onChanged();
    },
    [onChanged],
  );

  if (expired) {
    return (
      <div className="panel">
        <h2>Sessie verlopen</h2>
        <p>U bent uitgelogd. Houd de hoek weer ingedrukt om opnieuw in te loggen.</p>
        <button className="button" type="button" onClick={onLeave}>
          Terug
        </button>
      </div>
    );
  }

  if (!settings) {
    return (
      <div className="panel">
        {failure ? <p className="error">{failure}</p> : <p>Laden…</p>}
        <button className="button" type="button" onClick={leave}>
          Terug
        </button>
      </div>
    );
  }

  return (
    <div className="admin">
      <header className="admin-header">
        <h2>Beheer</h2>
        <button className="button button-secondary" type="button" onClick={leave}>
          Uitloggen
        </button>
      </header>

      {settings.credentialsAreDefault && (
        <p className="warning">
          Dit apparaat gebruikt nog de standaardgegevens voor beheer. Iedereen die dit
          project kent, kan hier dus binnen. Stel onder <strong>Account</strong> een eigen
          gebruikersnaam en wachtwoord in.
        </p>
      )}

      <nav className="admin-tabs">
        {ADMIN_TABS.map((item) => (
          <button
            key={item.id}
            type="button"
            className={item.id === tab ? 'admin-tab admin-tab-active' : 'admin-tab'}
            onClick={() => onTab(item.id)}
          >
            {item.label}
          </button>
        ))}
      </nav>

      {failure && <p className="error">{failure}</p>}

      {/* Each form seeds a local draft from its props, so it is remounted by key
          when the saved values change. Settings only change on save, never
          while someone is typing. */}
      {tab === 'outlook' && (
        <GraphSettingsForm
          key={`graph-${settings.graph.secretIsSet}-${settings.graph.tenantId}-${settings.graph.clientId}-${settings.graph.timeZone}`}
          settings={settings.graph}
          errors={errors}
          busy={busy}
          saved={saved}
          onSave={save}
        />
      )}

      {tab === 'sync' && (
        <SyncSettingsForm
          key={JSON.stringify(settings.sync)}
          settings={settings.sync}
          errors={errors}
          busy={busy}
          saved={saved}
          onSave={save}
        />
      )}

      {tab === 'rooms' && (
        <RoomsEditor
          key={JSON.stringify(settings.rooms)}
          rooms={settings.rooms}
          onSaved={roomsSaved}
        />
      )}

      {tab === 'privacy' && (
        <PrivacySettingsForm
          key={JSON.stringify(settings.privacy)}
          settings={settings.privacy}
          errors={errors}
          busy={busy}
          saved={saved}
          onSave={save}
        />
      )}

      {tab === 'device' && (
        <DeviceSettingsForm
          key={JSON.stringify(settings.device)}
          settings={settings.device}
          rooms={settings.rooms}
          errors={errors}
          busy={busy}
          saved={saved}
          onSave={save}
        />
      )}

      {tab === 'display' && (
        <DisplaySettingsForm
          key={JSON.stringify(settings.ui)}
          settings={settings.ui}
          errors={errors}
          busy={busy}
          saved={saved}
          onSave={save}
        />
      )}

      {tab === 'system' && (
        <SystemSettingsForm
          key={JSON.stringify([settings.security, settings.network, settings.browser])}
          security={settings.security}
          network={settings.network}
          browser={settings.browser}
          errors={errors}
          busy={busy}
          saved={saved}
          onSave={save}
        />
      )}

      {tab === 'account' && (
        <AccountForm
          key={settings.username}
          username={settings.username}
          credentialsAreDefault={settings.credentialsAreDefault}
          onChanged={onLeave}
        />
      )}

      {tab === 'diagnostics' && <DiagnosticsPanel />}
    </div>
  );
}

function parseErrors(body: string): AdminFieldError[] {
  try {
    const parsed: unknown = JSON.parse(body);
    if (typeof parsed === 'object' && parsed !== null && 'errors' in parsed) {
      const errors = (parsed as { errors: unknown }).errors;
      if (Array.isArray(errors)) {
        return errors as AdminFieldError[];
      }
    }
  } catch {
    // Fall through: a generic failure is shown instead.
  }

  return [];
}
