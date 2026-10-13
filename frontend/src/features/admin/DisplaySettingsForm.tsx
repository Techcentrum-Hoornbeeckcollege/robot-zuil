import { useState } from 'react';
import type {
  AdminFieldError,
  AdminSettingsUpdate,
  AdminUiSettings,
  AttractView,
} from '../../api/types';
import { errorFor, NumberField, SaveActions, SegmentedField, ToggleField } from './fields';

interface Props {
  settings: AdminUiSettings;
  errors: AdminFieldError[];
  busy: boolean;
  saved: boolean;
  onSave: (update: AdminSettingsUpdate) => void;
}

const VIEWS: readonly { value: AttractView; label: string }[] = [
  { value: 'Upcoming', label: 'Straks' },
  { value: 'Board', label: "Alle agenda's" },
  { value: 'Attract', label: 'Welkom' },
];

export function DisplaySettingsForm({ settings, errors, busy, saved, onSave }: Props) {
  const [draft, setDraft] = useState(settings);

  const dirty =
    draft.attractView !== settings.attractView ||
    draft.upcomingCount !== settings.upcomingCount ||
    draft.showAgendaNav !== settings.showAgendaNav;

  return (
    <section className="admin-section">
      <SegmentedField
        label="Beginscherm"
        value={draft.attractView}
        options={VIEWS}
        onChange={(value) => setDraft((p) => ({ ...p, attractView: value }))}
        hint="Wat de zuil laat zien als er niemand bij staat."
        error={errorFor(errors, 'ui.attractView')}
      />

      <NumberField
        id="ui-upcoming"
        label="Aantal regels op Straks"
        unit="vergaderingen"
        min={1}
        max={100}
        value={draft.upcomingCount}
        onChange={(value) => setDraft((p) => ({ ...p, upcomingCount: value }))}
        error={errorFor(errors, 'ui.upcomingCount')}
      />

      <ToggleField
        label="Agendaknoppen onderaan"
        value={draft.showAgendaNav}
        onChange={(value) => setDraft((p) => ({ ...p, showAgendaNav: value }))}
        hint="Uit zetten als de zuil alleen voor naam zoeken bedoeld is."
      />

      <SaveActions busy={busy} dirty={dirty} saved={saved} onSave={() => onSave({ ui: draft })} />
    </section>
  );
}
