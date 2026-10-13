import { useState } from 'react';
import { api } from '../../api/client';
import type {
  AdminDeviceSettings,
  AdminFieldError,
  AdminRoom,
  AdminSettingsUpdate,
  AdminTestResult,
} from '../../api/types';
import { errorFor, Field, NumberField, SaveActions } from './fields';

interface Props {
  settings: AdminDeviceSettings;
  rooms: AdminRoom[];
  errors: AdminFieldError[];
  busy: boolean;
  saved: boolean;
  onSave: (update: AdminSettingsUpdate) => void;
}

/**
 * The i2c link to the ESP32.
 *
 * Bus, address and attention pin are badged "herstart vereist" because they are
 * read once at startup: re-opening an I2cDevice and re-registering GPIO
 * callbacks on a running kiosk is a new way for a wall-mounted machine to fail,
 * for no real gain. Timing and retries apply immediately.
 */
export function DeviceSettingsForm({
  settings,
  rooms,
  errors,
  busy,
  saved,
  onSave,
}: Props) {
  const [draft, setDraft] = useState(settings);
  const [test, setTest] = useState<AdminTestResult | null>(null);

  const dirty =
    draft.busId !== settings.busId ||
    draft.address !== settings.address ||
    draft.attentionPin !== settings.attentionPin ||
    draft.responseDelayMs !== settings.responseDelayMs ||
    draft.maxRetries !== settings.maxRetries ||
    draft.maxCommandsPerSecond !== settings.maxCommandsPerSecond;

  function set<K extends keyof AdminDeviceSettings>(key: K, value: AdminDeviceSettings[K]) {
    setDraft((previous) => ({ ...previous, [key]: value }));
  }

  async function testIndicator(signalId: number) {
    try {
      setTest(await api.adminTestDevice(signalId));
    } catch {
      setTest({ ok: false, message: 'De indicator kon niet worden aangestuurd.' });
    }
  }

  return (
    <section className="admin-section">
      <NumberField
        id="device-bus"
        label="I2c-bus"
        unit="busnummer"
        min={0}
        max={10}
        restartRequired
        value={draft.busId}
        onChange={(v) => set('busId', v)}
        hint="Op de GPIO-header van een Raspberry Pi is dit bus 1."
        error={errorFor(errors, 'device.busId')}
      />

      <NumberField
        id="device-address"
        label="I2c-adres"
        unit={`decimaal (nu 0x${draft.address.toString(16).toUpperCase()})`}
        min={8}
        max={119}
        restartRequired
        value={draft.address}
        onChange={(v) => set('address', v)}
        hint="Moet overeenkomen met de firmware. Bruikbaar bereik 0x08 tot 0x77."
        error={errorFor(errors, 'device.address')}
      />

      <NumberField
        id="device-pin"
        label="Attention-pin"
        unit="BCM-pin"
        min={0}
        max={27}
        restartRequired
        value={draft.attentionPin ?? -1}
        onChange={(v) => set('attentionPin', v < 0 ? null : v)}
        hint="De lijn waarmee de ESP32 meldt dat hij iets te zeggen heeft. Zet op -1 om uit te schakelen."
        error={errorFor(errors, 'device.attentionPin')}
      />

      <NumberField
        id="device-delay"
        label="Wachten op antwoord"
        unit="ms"
        min={1}
        max={1000}
        value={draft.responseDelayMs}
        onChange={(v) => set('responseDelayMs', v)}
        hint="De ESP32 kan de klok niet betrouwbaar rekken, dus geven we de firmware even tijd."
        error={errorFor(errors, 'device.responseDelay')}
      />

      <NumberField
        id="device-retries"
        label="Pogingen per commando"
        unit="keer"
        min={0}
        max={10}
        value={draft.maxRetries}
        onChange={(v) => set('maxRetries', v)}
        error={errorFor(errors, 'device.maxRetries')}
      />

      <NumberField
        id="device-rate"
        label="Maximaal aantal commando's"
        unit="per seconde"
        min={1}
        max={100}
        value={draft.maxCommandsPerSecond}
        onChange={(v) => set('maxCommandsPerSecond', v)}
        hint="Zodat iemand die op het scherm staat te rammen de bus niet volloopt."
        error={errorFor(errors, 'device.maxCommandsPerSecond')}
      />

      <SaveActions
        busy={busy}
        dirty={dirty}
        saved={saved}
        onSave={() => onSave({ device: draft })}
      />

      {rooms.length > 0 && (
        <Field
          label="Indicators testen"
          hint="Stuurt de indicator van één zaal aan, zodat u kunt zien of de juiste gaat branden."
        >
          <div className="admin-test-grid">
            {rooms.map((room) => (
              <button
                key={room.id}
                className="button button-secondary"
                type="button"
                onClick={() => testIndicator(room.signalId)}
              >
                {room.signalId}. {room.displayName}
              </button>
            ))}
          </div>
        </Field>
      )}

      {test && <p className={test.ok ? 'admin-test-ok' : 'error'}>{test.message}</p>}
    </section>
  );
}
