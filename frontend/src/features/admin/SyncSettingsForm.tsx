import { useState } from 'react';
import { api } from '../../api/client';
import type { AdminFieldError, AdminSettingsUpdate, AdminSyncSettings } from '../../api/types';
import { errorFor, NumberField, SaveActions } from './fields';

interface Props {
  settings: AdminSyncSettings;
  errors: AdminFieldError[];
  busy: boolean;
  saved: boolean;
  onSave: (update: AdminSettingsUpdate) => void;
}

export function SyncSettingsForm({ settings, errors, busy, saved, onSave }: Props) {
  const [draft, setDraft] = useState(settings);
  const [syncing, setSyncing] = useState(false);

  const dirty =
    draft.intervalMinutes !== settings.intervalMinutes ||
    draft.windowBehindHours !== settings.windowBehindHours ||
    draft.windowAheadHours !== settings.windowAheadHours ||
    draft.staleAfterMinutes !== settings.staleAfterMinutes ||
    draft.retainPastMeetingsForHours !== settings.retainPastMeetingsForHours;

  function set<K extends keyof AdminSyncSettings>(key: K, value: AdminSyncSettings[K]) {
    setDraft((previous) => ({ ...previous, [key]: value }));
  }

  async function syncNow() {
    setSyncing(true);
    try {
      await api.adminSyncNow();
    } finally {
      // Deliberately brief: the worker wakes within a second or two, and what
      // proves it worked is the agenda, not this button.
      window.setTimeout(() => setSyncing(false), 1_000);
    }
  }

  return (
    <section className="admin-section">
      <NumberField
        id="sync-interval"
        label="Hoe vaak synchroniseren"
        unit="minuten"
        min={1}
        max={60}
        value={draft.intervalMinutes}
        onChange={(v) => set('intervalMinutes', v)}
        hint="Zes agenda's ophalen is weinig werk; elke paar minuten is ruim genoeg."
        error={errorFor(errors, 'sync.interval')}
      />

      <NumberField
        id="sync-behind"
        label="Hoe ver terug ophalen"
        unit="uur"
        min={0}
        max={24}
        value={draft.windowBehindHours}
        onChange={(v) => set('windowBehindHours', v)}
        hint="Zodat een vergadering die al bezig is ook gevonden wordt."
        error={errorFor(errors, 'sync.windowBehind')}
      />

      <NumberField
        id="sync-ahead"
        label="Hoe ver vooruit ophalen"
        unit="uur"
        min={1}
        max={168}
        value={draft.windowAheadHours}
        onChange={(v) => set('windowAheadHours', v)}
        hint="Vandaag plus morgen is genoeg om iemand de weg te wijzen."
        error={errorFor(errors, 'sync.windowAhead')}
      />

      <NumberField
        id="sync-stale"
        label="Waarschuwen na"
        unit="minuten"
        min={1}
        max={1440}
        value={draft.staleAfterMinutes}
        onChange={(v) => set('staleAfterMinutes', v)}
        hint="Hierna meldt de zuil dat de gegevens misschien niet actueel zijn. Moet ruimer zijn dan het interval."
        error={errorFor(errors, 'sync.staleAfter')}
      />

      <NumberField
        id="sync-retain"
        label="Afgelopen vergaderingen bewaren"
        unit="uur"
        min={0}
        max={168}
        value={draft.retainPastMeetingsForHours}
        onChange={(v) => set('retainPastMeetingsForHours', v)}
        hint="Kort houden: dit zijn namen van bezoekers op een apparaat in een openbaar gebouw."
        error={errorFor(errors, 'sync.retainPastMeetingsFor')}
      />

      <SaveActions busy={busy} dirty={dirty} saved={saved} onSave={() => onSave({ sync: draft })}>
        <button
          className="button button-secondary"
          type="button"
          disabled={busy || syncing}
          onClick={syncNow}
        >
          {syncing ? 'Bezig…' : 'Nu synchroniseren'}
        </button>
      </SaveActions>
    </section>
  );
}
