import { AgendaEntryRow, isCurrent } from './AgendaEntryRow';
import { useRoomAgendas } from './useAgenda';

interface Props {
  /** Bumped by the hub when the cache changes. */
  refreshNonce: number;
}

const DAY = new Intl.DateTimeFormat('nl-NL', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
});

/**
 * "Alle agenda's": one column per room, each its own day.
 *
 * The column this runs on is portrait, so six rooms will not fit side by side at
 * a readable size. Rather than shrinking them to illegibility the board scrolls
 * sideways with scroll-snap, showing two or three columns at a time — a swipe is
 * the one gesture a touchscreen is unambiguously good at.
 */
export function AgendaBoard({ refreshNonce }: Props) {
  const { data, loading, failed } = useRoomAgendas(undefined, refreshNonce);

  const now = data ? new Date(data.now).getTime() : Date.now();

  return (
    <section className="board">
      <header className="agenda-header">
        <h1>Alle agenda's</h1>
        {data && <p className="agenda-updated">{DAY.format(new Date(data.from))}</p>}
      </header>

      {data?.stale && (
        <p className="warning">
          De gegevens zijn mogelijk niet actueel. Controleer bij de receptie.
        </p>
      )}

      {data?.truncated && (
        <p className="warning">
          Deze dag valt buiten de opgeslagen periode, dus de lijst kan onvolledig zijn.
        </p>
      )}

      {loading && <p className="agenda-empty">Laden…</p>}

      {!loading && failed && <p className="error">De agenda's konden niet worden geladen.</p>}

      {!loading && !failed && data && data.rooms.length === 0 && (
        <p className="agenda-empty">Er zijn nog geen zalen ingesteld.</p>
      )}

      {data && data.rooms.length > 0 && (
        <div className="board-columns">
          {data.rooms.map((room) => (
            <article className="board-column" key={room.id}>
              <header className="board-column-head">
                <h2>{room.displayName}</h2>
                <p className="board-column-floor">{room.floor}</p>
              </header>

              {room.entries.length === 0 ? (
                <p className="board-column-free">Vrij</p>
              ) : (
                <ul className="agenda-list">
                  {room.entries.map((entry) => (
                    <AgendaEntryRow
                      key={`${entry.roomId}-${entry.start}`}
                      entry={entry}
                      showRoom={false}
                      current={isCurrent(entry, now)}
                    />
                  ))}
                </ul>
              )}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
