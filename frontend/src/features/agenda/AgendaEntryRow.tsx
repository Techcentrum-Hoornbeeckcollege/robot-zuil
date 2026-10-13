import type { AgendaEntry } from '../../api/types';

const TIME = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit' });

export function timeRange(start: string, end: string): string {
  return `${TIME.format(new Date(start))} – ${TIME.format(new Date(end))}`;
}

interface Props {
  entry: AgendaEntry;
  /** The board already has a column per room, so the name would just repeat. */
  showRoom?: boolean;
  /** Whether this meeting is on right now. */
  current?: boolean;
}

/**
 * One line of an agenda.
 *
 * Renders `subject` when it is there and says "Bezet" when it is not. That is
 * not a privacy decision being made here — the server already made it by
 * nulling the field. If a subject reaches this component, it may be shown.
 */
export function AgendaEntryRow({ entry, showRoom = true, current = false }: Props) {
  const classes = ['agenda-row'];
  if (current) {
    classes.push('agenda-row-current');
  }

  return (
    <li className={classes.join(' ')}>
      <span className="agenda-time">{timeRange(entry.start, entry.end)}</span>

      <span className="agenda-what">
        {showRoom && <span className="agenda-room">{entry.roomDisplayName}</span>}
        {entry.subject ? (
          <span className="agenda-subject">{entry.subject}</span>
        ) : (
          <span className="agenda-subject agenda-subject-hidden">Bezet</span>
        )}
      </span>

      {showRoom && <span className="agenda-floor">{entry.floor}</span>}
    </li>
  );
}

/** Whether `now` falls inside this entry. */
export function isCurrent(entry: AgendaEntry, now: number): boolean {
  return new Date(entry.start).getTime() <= now && new Date(entry.end).getTime() > now;
}
