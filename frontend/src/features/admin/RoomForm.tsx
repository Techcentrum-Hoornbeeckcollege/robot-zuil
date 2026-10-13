import { useState } from 'react';
import type { AdminFieldError, AdminRoom } from '../../api/types';
import { errorFor, Field, TextField } from './fields';

interface Props {
  room: AdminRoom;
  /** True for a room that does not exist yet, so the id is still editable. */
  isNew: boolean;
  /** Indicator numbers used by the other rooms, which cannot be picked again. */
  takenSignalIds: readonly number[];
  errors: AdminFieldError[];
  onChange: (room: AdminRoom) => void;
  onDone: () => void;
  onDelete: () => void;
}

/** How many indicators the picker offers. The ESP32 protocol uses one byte. */
const SIGNAL_COUNT = 12;

/**
 * One room, edited in place.
 *
 * A full panel rather than a modal: with `cursor: none` and no keyboard, a modal
 * on a kiosk is a trap — there is no Escape key and no obvious outside to click.
 */
export function RoomForm({
  room,
  isNew,
  takenSignalIds,
  errors,
  onChange,
  onDone,
  onDelete,
}: Props) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  function set<K extends keyof AdminRoom>(key: K, value: AdminRoom[K]) {
    onChange({ ...room, [key]: value });
  }

  return (
    <div className="panel admin-room-form">
      <h3>{room.displayName || 'Nieuwe zaal'}</h3>

      <TextField
        id="room-name"
        label="Naam"
        value={room.displayName}
        onChange={(v) => set('displayName', v)}
        placeholder="Vergaderzaal 1.01"
        error={errorFor(errors, 'displayName')}
      />

      <TextField
        id="room-mailbox"
        label="Postvak"
        value={room.mailbox}
        onChange={(v) => set('mailbox', v)}
        placeholder="zaal-1-01@voorbeeld.nl"
        hint="Het resource-postvak van de zaal in Outlook."
        error={errorFor(errors, 'mailbox')}
      />

      <TextField
        id="room-floor"
        label="Verdieping"
        value={room.floor}
        onChange={(v) => set('floor', v)}
        placeholder="1e verdieping"
        error={errorFor(errors, 'floor')}
      />

      <TextField
        id="room-directions"
        label="Looproute"
        value={room.directions}
        onChange={(v) => set('directions', v)}
        placeholder="Trap op, dan rechts, derde deur."
        hint="Dit is wat de bezoeker leest. Schrijf het zoals u het zou zeggen."
        multiline
        error={errorFor(errors, 'directions')}
      />

      <Field
        label="Indicator"
        error={errorFor(errors, 'signalId')}
        hint="Welke indicator op de zuil deze zaal aanwijst. Bezette nummers zijn niet te kiezen."
      >
        <div className="admin-signal-grid" role="group">
          {Array.from({ length: SIGNAL_COUNT }, (_, i) => i + 1).map((id) => {
            const taken = takenSignalIds.includes(id) && id !== room.signalId;

            return (
              <button
                key={id}
                type="button"
                className={id === room.signalId ? 'segment segment-active' : 'segment'}
                // Two rooms sharing an indicator means the column points at the
                // wrong place half the time. The backend rejects it; disabling
                // it here means nobody has to find that out from an error.
                disabled={taken}
                aria-pressed={id === room.signalId}
                onClick={() => set('signalId', id)}
              >
                {id}
              </button>
            );
          })}
        </div>
      </Field>

      {isNew ? (
        <TextField
          id="room-id"
          label="Id"
          value={room.id}
          onChange={(v) => set('id', v)}
          placeholder="Automatisch uit de naam"
          hint="Laat leeg om er een te laten maken. Hierna niet meer te wijzigen."
          error={errorFor(errors, 'id')}
        />
      ) : (
        <Field label="Id" hint="Vast, omdat deze in opgeslagen gegevens wordt gebruikt.">
          <p className="admin-readonly">{room.id}</p>
        </Field>
      )}

      <div className="admin-actions">
        <button className="button" type="button" onClick={onDone}>
          Gereed
        </button>

        {confirmingDelete ? (
          <button className="button button-danger" type="button" onClick={onDelete}>
            Echt verwijderen
          </button>
        ) : (
          <button
            className="button button-secondary"
            type="button"
            onClick={() => setConfirmingDelete(true)}
          >
            Verwijderen
          </button>
        )}
      </div>
    </div>
  );
}
