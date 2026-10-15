import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { AdminDiagnostics, HealthLevel } from '../../api/types';

/** Short enough that a maintainer standing at the kiosk sees it recover. */
const REFRESH_MS = 5000;

const STATUS: Record<HealthLevel, { label: string; className: string }> = {
  Ok: { label: 'In orde', className: 'health-badge health-badge-ok' },
  Degraded: { label: 'Aandacht nodig', className: 'health-badge health-badge-degraded' },
  Down: { label: 'Storing', className: 'health-badge health-badge-down' },
};

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

  useEffect(() => {
    load();

    const timer = setInterval(load, REFRESH_MS);
    return () => clearInterval(timer);
  }, [load]);

  if (failed) {
    return <p className="error">De diagnose kon niet worden geladen.</p>;
  }

  if (!data) {
    return <p className="admin-field-hint">Laden…</p>;
  }

  return (
    <section className="admin-section">
      <dl className="admin-facts">
        <dt>Status</dt>
        <dd>
          <span className={STATUS[data.status].className}>
            {STATUS[data.status].label}
          </span>
        </dd>

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

      <p className="admin-field-hint">
        Dit scherm wordt elke vijf seconden bijgewerkt.
      </p>

      <div className="admin-actions">
        <button className="button button-secondary" type="button" onClick={load}>
          Nu verversen
        </button>
      </div>
    </section>
  );
}
