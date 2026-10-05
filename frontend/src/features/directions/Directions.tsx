import type { DirectionHint } from '../../api/types';

interface Props {
  results: DirectionHint[];
  stale: boolean;
  onReset: () => void;
}

function timeRange(start: string, end: string): string {
  const fmt = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit' });
  return `${fmt.format(new Date(start))} – ${fmt.format(new Date(end))}`;
}

/**
 * Shows where to walk.
 *
 * Deliberately does not render the meeting subject or the other attendees: the
 * visitor needs a room and a time, and everything beyond that is personal data
 * on a screen in a lobby. See docs/privacy.md.
 */
export function Directions({ results, stale, onReset }: Props) {
  if (results.length === 0) {
    return (
      <div className="panel">
        <h2>Geen vergadering gevonden</h2>
        <p>
          Controleer de spelling van uw naam, of vraag bij de receptie. Uw
          vergadering staat mogelijk op een andere naam.
        </p>
        <button className="button" onClick={onReset}>
          Opnieuw proberen
        </button>
      </div>
    );
  }

  return (
    <div className="panel">
      {stale && (
        <p className="warning">
          De gegevens zijn mogelijk niet actueel. Controleer bij de receptie.
        </p>
      )}

      {results.map((hint) => (
        <div className="result" key={`${hint.roomDisplayName}-${hint.start}`}>
          <p className="result-time">{timeRange(hint.start, hint.end)}</p>
          <h2 className="result-room">{hint.roomDisplayName}</h2>
          <p className="result-floor">{hint.floor}</p>
          <p className="result-directions">{hint.directions}</p>
        </div>
      ))}

      <button className="button" onClick={onReset}>
        Klaar
      </button>
    </div>
  );
}
