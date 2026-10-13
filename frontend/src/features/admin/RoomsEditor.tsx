import { useState } from 'react';
import { api, ApiError } from '../../api/client';
import type { AdminFieldError, AdminRoom } from '../../api/types';
import { SaveActions } from './fields';
import { RoomForm } from './RoomForm';

interface Props {
  rooms: AdminRoom[];
  onSaved: (rooms: AdminRoom[]) => void;
}

function emptyRoom(): AdminRoom {
  return { id: '', mailbox: '', displayName: '', floor: '', directions: '', signalId: 0 };
}

/**
 * The room directory.
 *
 * Saves the whole array in one request. Uniqueness of ids, mailboxes and
 * indicators is a property of the set, so there is no coherent per-room save:
 * the server validates the list as a whole and replaces it in one transaction.
 */
export function RoomsEditor({ rooms, onSaved }: Props) {
  const [draft, setDraft] = useState<AdminRoom[]>(rooms);
  const [editing, setEditing] = useState<number | null>(null);
  const [newIndexes, setNewIndexes] = useState<Set<number>>(new Set());
  const [errors, setErrors] = useState<AdminFieldError[]>([]);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const dirty = JSON.stringify(draft) !== JSON.stringify(rooms);

  function add() {
    const index = draft.length;
    setDraft([...draft, emptyRoom()]);
    setNewIndexes(new Set(newIndexes).add(index));
    setEditing(index);
  }

  function update(index: number, room: AdminRoom) {
    setDraft(draft.map((existing, i) => (i === index ? room : existing)));
    setSaved(false);
  }

  function remove(index: number) {
    setDraft(draft.filter((_, i) => i !== index));
    setEditing(null);
    setNewIndexes(new Set());
    setSaved(false);
  }

  async function save() {
    setBusy(true);
    setErrors([]);
    setFailure(null);

    try {
      const result = await api.adminSaveRooms(draft);
      setDraft(result.rooms);
      setNewIndexes(new Set());
      setEditing(null);
      setSaved(true);
      onSaved(result.rooms);
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 400) {
        setErrors(parseErrors(caught.body));
      } else {
        setFailure('Opslaan is niet gelukt.');
      }
    } finally {
      setBusy(false);
    }
  }

  // The server reports errors as rooms[2].signalId; show them on that row.
  function errorsFor(index: number): AdminFieldError[] {
    const prefix = `rooms[${index}].`;
    return errors
      .filter((e) => e.field.startsWith(prefix))
      .map((e) => ({ field: e.field.slice(prefix.length), message: e.message }));
  }

  if (editing !== null && draft[editing]) {
    const index = editing;

    return (
      <RoomForm
        room={draft[index]}
        isNew={newIndexes.has(index)}
        takenSignalIds={draft.filter((_, i) => i !== index).map((r) => r.signalId)}
        errors={errorsFor(index)}
        onChange={(room) => update(index, room)}
        onDone={() => setEditing(null)}
        onDelete={() => remove(index)}
      />
    );
  }

  return (
    <section className="admin-section">
      {draft.length === 0 && (
        <p className="admin-field-hint">
          Er zijn nog geen zalen. Voeg er een toe; zonder zalen heeft de zuil niets op te
          halen.
        </p>
      )}

      <ul className="admin-room-list">
        {draft.map((room, index) => {
          const rowErrors = errorsFor(index);

          return (
            <li key={room.id || `nieuw-${index}`}>
              <button
                type="button"
                className={rowErrors.length > 0 ? 'room-row room-row-invalid' : 'room-row'}
                onClick={() => setEditing(index)}
              >
                <span className="room-row-signal">{room.signalId || '–'}</span>
                <span className="room-row-name">{room.displayName || 'Nieuwe zaal'}</span>
                <span className="room-row-floor">{room.floor}</span>
              </button>

              {rowErrors.map((e) => (
                <p className="error" key={e.field}>
                  {e.message}
                </p>
              ))}
            </li>
          );
        })}
      </ul>

      <SaveActions busy={busy} dirty={dirty} saved={saved} onSave={save}>
        <button className="button button-secondary" type="button" onClick={add}>
          Zaal toevoegen
        </button>
      </SaveActions>

      {failure && <p className="error">{failure}</p>}
    </section>
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
    // Fall through: the caller shows a generic failure instead.
  }

  return [];
}
