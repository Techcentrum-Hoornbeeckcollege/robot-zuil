import type { ReactNode } from 'react';
import type { AdminFieldError } from '../../api/types';

/** Looks up the server's complaint about one field, if there is one. */
export function errorFor(errors: AdminFieldError[], field: string): string | undefined {
  return errors.find((e) => e.field === field)?.message;
}

interface FieldProps {
  label: string;
  htmlFor?: string;
  /** Shown under the label: what this setting does, in one line. */
  hint?: ReactNode;
  error?: string;
  /** Marks a setting that only takes effect after a restart. */
  restartRequired?: boolean;
  children: ReactNode;
}

export function Field({
  label,
  htmlFor,
  hint,
  error,
  restartRequired = false,
  children,
}: FieldProps) {
  return (
    <div className="admin-field">
      <label className="label" htmlFor={htmlFor}>
        {label}
        {restartRequired && <span className="badge badge-warning">herstart vereist</span>}
      </label>

      {children}

      {hint && <p className="admin-field-hint">{hint}</p>}
      {error && <p className="error">{error}</p>}
    </div>
  );
}

interface TextFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: ReactNode;
  error?: string;
  placeholder?: string;
  type?: 'text' | 'password';
  multiline?: boolean;
}

export function TextField({
  id,
  label,
  value,
  onChange,
  hint,
  error,
  placeholder,
  type = 'text',
  multiline = false,
}: TextFieldProps) {
  return (
    <Field label={label} htmlFor={id} hint={hint} error={error}>
      {multiline ? (
        <textarea
          id={id}
          className="input input-multiline"
          value={value}
          rows={3}
          placeholder={placeholder}
          onChange={(e) => onChange(e.target.value)}
        />
      ) : (
        <input
          id={id}
          className="input"
          type={type}
          value={value}
          placeholder={placeholder}
          autoComplete={type === 'password' ? 'new-password' : 'off'}
          autoCorrect="off"
          spellCheck={false}
          onChange={(e) => onChange(e.target.value)}
        />
      )}
    </Field>
  );
}

interface NumberFieldProps {
  id: string;
  label: string;
  value: number;
  onChange: (value: number) => void;
  /** The unit the number is in, shown beside the box. */
  unit: string;
  hint?: ReactNode;
  error?: string;
  min?: number;
  max?: number;
  restartRequired?: boolean;
}

/**
 * A number in a stated unit.
 *
 * Deliberately not a free-text "hh:mm:ss": nobody should have to type a
 * TimeSpan on a touchscreen, and `inputMode="numeric"` gets the on-screen
 * keypad rather than the full keyboard.
 */
export function NumberField({
  id,
  label,
  value,
  onChange,
  unit,
  hint,
  error,
  min,
  max,
  restartRequired = false,
}: NumberFieldProps) {
  return (
    <Field
      label={label}
      htmlFor={id}
      hint={hint}
      error={error}
      restartRequired={restartRequired}
    >
      <span className="admin-number">
        <input
          id={id}
          className="input input-number"
          type="number"
          inputMode="numeric"
          value={Number.isFinite(value) ? value : ''}
          min={min}
          max={max}
          onChange={(e) => {
            const parsed = Number.parseInt(e.target.value, 10);
            onChange(Number.isNaN(parsed) ? 0 : parsed);
          }}
        />
        <span className="admin-unit">{unit}</span>
      </span>
    </Field>
  );
}

interface Option<T extends string> {
  value: T;
  label: string;
}

interface SegmentedFieldProps<T extends string> {
  label: string;
  value: T;
  options: readonly Option<T>[];
  onChange: (value: T) => void;
  hint?: ReactNode;
  error?: string;
}

/**
 * A row of mutually exclusive buttons.
 *
 * A native select on a touchscreen in kiosk mode opens a dropdown that can be
 * rendered off-screen, and there are never more than three or four choices here.
 */
export function SegmentedField<T extends string>({
  label,
  value,
  options,
  onChange,
  hint,
  error,
}: SegmentedFieldProps<T>) {
  return (
    <Field label={label} hint={hint} error={error}>
      <div className="admin-segmented" role="group">
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            className={
              option.value === value ? 'segment segment-active' : 'segment'
            }
            aria-pressed={option.value === value}
            onClick={() => onChange(option.value)}
          >
            {option.label}
          </button>
        ))}
      </div>
    </Field>
  );
}

interface ToggleFieldProps {
  label: string;
  value: boolean;
  onChange: (value: boolean) => void;
  hint?: ReactNode;
}

export function ToggleField({ label, value, onChange, hint }: ToggleFieldProps) {
  return (
    <Field label={label} hint={hint}>
      <div className="admin-segmented" role="group">
        <button
          type="button"
          className={value ? 'segment segment-active' : 'segment'}
          aria-pressed={value}
          onClick={() => onChange(true)}
        >
          Aan
        </button>
        <button
          type="button"
          className={value ? 'segment' : 'segment segment-active'}
          aria-pressed={!value}
          onClick={() => onChange(false)}
        >
          Uit
        </button>
      </div>
    </Field>
  );
}

interface ActionsProps {
  busy: boolean;
  dirty: boolean;
  saved: boolean;
  onSave: () => void;
  children?: ReactNode;
}

export function SaveActions({ busy, dirty, saved, onSave, children }: ActionsProps) {
  return (
    <div className="admin-actions">
      <button className="button" type="button" disabled={busy || !dirty} onClick={onSave}>
        {busy ? 'Opslaan…' : 'Opslaan'}
      </button>

      {children}

      {saved && !dirty && <span className="admin-saved">Opgeslagen</span>}
    </div>
  );
}
