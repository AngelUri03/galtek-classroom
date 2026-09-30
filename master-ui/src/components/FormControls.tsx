import { Button } from "primereact/button";
import { Password } from "primereact/password";
import { X } from "lucide-react";
import { useId } from "react";

export function GaltekCloseButton({
  ariaLabel,
  disabled = false,
  onClick
}: {
  ariaLabel: string;
  disabled?: boolean;
  onClick: () => void;
}) {
  return (
    <Button
      type="button"
      text
      className="galtek-close-button"
      icon={<X size={16} aria-hidden="true" />}
      aria-label={ariaLabel}
      disabled={disabled}
      onClick={onClick}
    />
  );
}

export function FormPassword({
  label,
  value,
  onChange,
  help,
  error,
  autoFocus = false,
  newPassword = false,
  disabled = false
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  help?: string;
  error?: string | null;
  autoFocus?: boolean;
  newPassword?: boolean;
  disabled?: boolean;
}) {
  const id = useId();
  const helperId = `${id}-helper`;
  return (
    <label className={`galtek-field${error ? " galtek-field--error" : ""}`} htmlFor={id}>
      <span>{label}</span>
      <Password
        inputId={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        feedback={false}
        toggleMask
        autoFocus={autoFocus}
        disabled={disabled}
        autoComplete={newPassword ? "new-password" : "current-password"}
        className="galtek-password"
        inputClassName="galtek-input"
        aria-invalid={Boolean(error)}
        aria-describedby={help || error ? helperId : undefined}
        pt={{
          showIcon: { "aria-label": `Mostrar ${label.toLowerCase()}` },
          hideIcon: { "aria-label": `Ocultar ${label.toLowerCase()}` }
        }}
      />
      {error || help ? <small id={helperId} role={error ? "alert" : undefined}>{error ?? help}</small> : null}
    </label>
  );
}
