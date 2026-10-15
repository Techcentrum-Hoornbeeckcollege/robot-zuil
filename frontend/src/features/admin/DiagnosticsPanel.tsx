import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { AdminDiagnostics } from '../../api/types';

const STAMP = new Intl.DateTimeFormat('nl-NL', {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
});

function when(value: string | null): string {
  return value ? STAMP.format(new Date(value)) : 'nooit';
}

/**
 * Read-only state, for working out why the screen in the lobby looks wrong.
 *
 * This is where the unsanitised last sync error lives. /api/health answers
 * unauthenticated callers and Graph messages can quote a response body, so that
 * endpoint gets a short description and this one gets the detail.
 */
export function DiagnosticsPanel() {
  const [data, setData] = useState<AdminDiagnostics | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(() => {
    api
      .adminDiagnostics()
      .then((next) => {
        setData(next);
        setFailed(false);
      })
      .catch(() => setFailed(true));
  }, []);

  useEffect(load, [load]);

  if (failed) {
    return <p className="error">De diagnose kon niet worden geladen.</p>;
  }

  if (!data) {
    return <p className="admin-field-hint">Laden…</p>;
  }

  return (
    <section className="admin-section">
      <dl className="admin-facts">
        <dt>Klok gesynchroniseerd</dt>
        <dd>{data.clockSynchronized ? 'ja' : 'nee — NTP werkt niet'}</dd>

        <dt>Zuil verbonden</dt>
        <dd>{data.deviceConnected ? 'ja' : 'nee'}</dd>

        <dt>Laatst gelukt</dt>
        <dd>{when(data.lastSuccessfulSync)}</dd>

        <dt>Laatste poging</dt>
        <dd>{when(data.lastAttemptedSync)}</dd>

        <dt>Vergaderingen in cache</dt>
        <dd>{data.meetingCount}</dd>

        <dt>Zalen</dt>
        <dd>{data.roomCount}</dd>

        <dt>Actieve beheersessies</dt>
        <dd>{data.activeSessions}</dd>

        <dt>Gegevensmap</dt>
        <dd>{data.dataDirectory}</dd>
      </dl>

      {data.credentialsAreDefault && (
        <p className="warning">
          De beheergegevens staan nog op de standaardwaarde. Wijzig ze onder Account.
        </p>
      )}

      {data.stale && (
        <p className="warning">
          De gegevens zijn ouder dan de ingestelde grens. De zuil toont een waarschuwing aan
          bezoekers.
        </p>
      )}

      {data.lastError && (
        <div className="admin-field">
          <p className="label">Laatste foutmelding</p>
          <pre className="admin-error-detail">{data.lastError}</pre>
        </div>
      )}

      <div className="admin-actions">
        <button className="button button-secondary" type="button" onClick={load}>
          Verversen
        </button>
      </div>
    </section>
  );
}
