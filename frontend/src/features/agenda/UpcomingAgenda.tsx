import { AgendaEntryRow, isCurrent } from './AgendaEntryRow';
import { useUpcomingAgenda } from './useAgenda';

interface Props {
  limit: number;
  /** Bumped by the hub when the cache changes, which triggers a re-fetch. */
  refreshNonce: number;
  onSearch: () => void;
}

const UPDATED = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit' });

/**
 * "Straks": everything on across the building, soonest first.
 *
 * This is the kiosk's resting screen by default, so it is read from across the
 * lobby by someone who is not touching it. That shapes it: big type, no
 * interaction needed, and one obvious way through to the name search.
 */
export function UpcomingAgenda({ limit, refreshNonce, onSearch }: Props) {
  const { data, loading, failed } = useUpcomingAgenda(limit, refreshNonce);

  const now = data ? new Date(data.now).getTime() : Date.now();

  return (
    <section className="agenda">
      <header className="agenda-header">
        <h1>Straks</h1>
        {data && (
          <p className="agenda-updated">Bijgewerkt {UPDATED.format(new Date(data.now))}</p>
        )}
      </header>

      {data?.stale && (
        <p className="warning">
          De gegevens zijn mogelijk niet actueel. Controleer bij de receptie.
        </p>
      )}

      {loading && <p className="agenda-empty">Laden…</p>}

      {!loading && failed && (
        <p className="error">De agenda kon niet worden geladen.</p>
      )}

      {!loading && !failed && data && data.entries.length === 0 && (
        <p className="agenda-empty">Er staat niets meer op de agenda voor vandaag.</p>
      )}

      {data && data.entries.length > 0 && (
        <ul className="agenda-list">
          {data.entries.map((entry) => (
            <AgendaEntryRow
              key={`${entry.roomId}-${entry.start}`}
              entry={entry}
              current={isCurrent(entry, now)}
            />
          ))}
        </ul>
      )}

      <button className="button" type="button" onClick={onSearch}>
        Zoek mijn vergadering
      </button>
    </section>
  );
}
