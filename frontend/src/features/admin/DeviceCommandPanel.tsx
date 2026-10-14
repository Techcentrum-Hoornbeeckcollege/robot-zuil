import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type {
  AdminDeviceCapability,
  AdminDeviceParameter,
  AdminDeviceReply,
} from '../../api/types';
import { Field, NumberField, SegmentedField, SliderField, ToggleField } from './fields';

/**
 * Sends device commands from the admin screen.
 *
 * Nothing here knows what any command does. The host publishes its catalogue —
 * each command with its parameters, their ranges, steps and units — and this
 * renders a control per parameter. So when the column gains a motor and the
 * firmware learns a height command, the slider appears here on its own: the
 * work is one record in DeviceCatalogue.cs and a firmware handler, not a change
 * to this file.
 */
export function DeviceCommandPanel() {
  const [capabilities, setCapabilities] = useState<AdminDeviceCapability[] | null>(null);
  const [values, setValues] = useState<Record<string, Record<string, number>>>({});
  const [replies, setReplies] = useState<Record<string, AdminDeviceReply>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const abort = new AbortController();

    api
      .adminDeviceCapabilities(abort.signal)
      .then((response) => {
        setCapabilities(response.capabilities);
        setValues(defaults(response.capabilities));
      })
      .catch(() => {
        if (!abort.signal.aborted) {
          setFailed(true);
        }
      });

    return () => abort.abort();
  }, []);

  async function send(capability: AdminDeviceCapability) {
    setBusy(capability.key);

    try {
      const reply = await api.adminSendDeviceCommand(
        capability.key,
        values[capability.key] ?? {},
      );
      setReplies((previous) => ({ ...previous, [capability.key]: reply }));
    } catch {
      setReplies((previous) => ({
        ...previous,
        [capability.key]: {
          ok: false,
          message: 'Het commando kon niet worden verstuurd.',
          response: null,
          payload: null,
          roundTripMs: null,
          attempts: null,
        },
      }));
    } finally {
      setBusy(null);
    }
  }

  function set(command: string, parameter: string, value: number) {
    setValues((previous) => ({
      ...previous,
      [command]: { ...previous[command], [parameter]: value },
    }));
  }

  if (failed) {
    return <p className="error">De commandolijst kon niet worden opgehaald.</p>;
  }

  if (capabilities === null) {
    return <p className="admin-field-hint">Commando's ophalen…</p>;
  }

  return (
    <>
      {capabilities.map((capability) => {
        const reply = replies[capability.key];

        return (
          <section className="admin-command" key={capability.key}>
            <h3 className="admin-command-title">
              {capability.label} <span className="admin-command-code">{capability.code}</span>
            </h3>

            {capability.hint && <p className="admin-field-hint">{capability.hint}</p>}

            {capability.parameters.map((parameter) =>
              renderParameter(
                capability,
                parameter,
                values[capability.key]?.[parameter.key] ?? parameter.default,
                (value) => set(capability.key, parameter.key, value),
              ),
            )}

            <div className="admin-actions">
              <button
                className="button"
                type="button"
                disabled={busy !== null}
                onClick={() => send(capability)}
              >
                {busy === capability.key ? 'Versturen…' : 'Versturen'}
              </button>
            </div>

            {reply && (
              <p className={reply.ok ? 'admin-test-ok' : 'error'}>
                {reply.message}
                {reply.attempts !== null && reply.attempts > 1 && (
                  <> Na {reply.attempts} pogingen.</>
                )}
                {reply.payload !== null && reply.payload.length > 0 && (
                  <> Antwoord-payload: {reply.payload}.</>
                )}
              </p>
            )}
          </section>
        );
      })}
    </>
  );
}

function renderParameter(
  capability: AdminDeviceCapability,
  parameter: AdminDeviceParameter,
  value: number,
  onChange: (value: number) => void,
) {
  const id = `device-${capability.key}-${parameter.key}`;
  const hint = parameter.hint ?? undefined;

  if (parameter.kind === 'Toggle') {
    return (
      <ToggleField
        key={parameter.key}
        label={parameter.label}
        value={value !== 0}
        onChange={(on) => onChange(on ? 1 : 0)}
        hint={hint}
      />
    );
  }

  if (parameter.kind === 'Choice') {
    return (
      <SegmentedField
        key={parameter.key}
        label={parameter.label}
        value={String(value)}
        options={parameter.choices.map((choice) => ({
          value: String(choice.value),
          label: choice.label,
        }))}
        onChange={(chosen) => onChange(Number.parseInt(chosen, 10))}
        hint={hint}
      />
    );
  }

  // A range wide enough to be unreadable as a slider gets a number box instead:
  // dragging across 256 indicator ids to land on 7 is worse than typing 7.
  const steps = (parameter.maximum - parameter.minimum) / parameter.step;

  if (steps > 0 && steps <= 128) {
    return (
      <SliderField
        key={parameter.key}
        id={id}
        label={parameter.label}
        value={value}
        onChange={onChange}
        min={parameter.minimum}
        max={parameter.maximum}
        step={parameter.step}
        unit={parameter.unit}
        hint={hint}
      />
    );
  }

  if (steps === 0) {
    // A parameter with exactly one legal value is worth showing, not hiding:
    // it usually means a range the firmware pinned.
    return (
      <Field key={parameter.key} label={parameter.label} hint={hint}>
        <p className="admin-readonly">
          {value} {parameter.unit}
        </p>
      </Field>
    );
  }

  return (
    <NumberField
      key={parameter.key}
      id={id}
      label={parameter.label}
      value={value}
      onChange={onChange}
      unit={parameter.unit}
      min={parameter.minimum}
      max={parameter.maximum}
      hint={hint}
    />
  );
}

function defaults(capabilities: AdminDeviceCapability[]): Record<string, Record<string, number>> {
  const seeded: Record<string, Record<string, number>> = {};

  for (const capability of capabilities) {
    seeded[capability.key] = Object.fromEntries(
      capability.parameters.map((parameter) => [parameter.key, parameter.default]),
    );
  }

  return seeded;
}
