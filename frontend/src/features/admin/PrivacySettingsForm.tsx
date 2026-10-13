import { useState } from 'react';
import type {
  AdminFieldError,
  AdminPrivacySettings,
  AdminSettingsUpdate,
  SubjectVisibility,
} from '../../api/types';
import { errorFor, NumberField, SaveActions, SegmentedField } from './fields';

interface Props {
  settings: AdminPrivacySettings;
  errors: AdminFieldError[];
  busy: boolean;
  saved: boolean;
  onSave: (update: AdminSettingsUpdate) => void;
}

const VISIBILITY: readonly { value: SubjectVisibility; label: string }[] = [
  { value: 'Hidden', label: 'Verborgen' },
  { value: 'AdminOnly', label: 'Alleen beheer' },
  { value: 'Everyone', label: 'Iedereen' },
];

export function PrivacySettingsForm({ settings, errors, busy, saved, onSave }: Props) {
  const [draft, setDraft] = useState(settings);

  const dirty =
    draft.subjectVisibility !== settings.subjectVisibility ||
    draft.idleTimeoutSeconds !== settings.idleTimeoutSeconds;

  return (
    <section className="admin-section">
      <SegmentedField
        label="Onderwerpen van vergaderingen"
        value={draft.subjectVisibility}
        options={VISIBILITY}
        onChange={(value) => setDraft((p) => ({ ...p, subjectVisibility: value }))}
        error={errorFor(errors, 'privacy.subjectVisibility')}
        hint={
          <>
            <strong>Verborgen</strong> toont alleen tijd en zaal. <strong>Alleen beheer</strong>{' '}
            laat onderwerpen zien als u bent ingelogd, handig om een verkeerde zaal uit te
            zoeken.
          </>
        }
      />

      {draft.subjectVisibility === 'Everyone' && (
        <p className="warning">
          Hiermee komen de titels van alle vergaderingen op een scherm in een openbare hal.
          Een titel zegt vaak meer dan een naam. Overleg dit met wie de privacyverklaring
          van het gebouw beheert.
        </p>
      )}

      <NumberField
        id="privacy-idle"
        label="Terug naar beginscherm na"
        unit="seconden"
        min={10}
        max={600}
        value={draft.idleTimeoutSeconds}
        onChange={(value) => setDraft((p) => ({ ...p, idleTimeoutSeconds: value }))}
        hint="Dit is wat de naam van de vorige bezoeker van het scherm haalt. Niet te ruim zetten."
        error={errorFor(errors, 'privacy.idleTimeout')}
      />

      <SaveActions
        busy={busy}
        dirty={dirty}
        saved={saved}
        onSave={() => onSave({ privacy: draft })}
      />
    </section>
  );
}
