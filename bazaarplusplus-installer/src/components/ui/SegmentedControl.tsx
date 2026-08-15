import clsx from 'clsx';
import { useId, type ReactNode } from 'react';

export interface SegmentedControlOption<Value extends string> {
  value: Value;
  label: ReactNode;
  disabled?: boolean;
}

export interface SegmentedControlProps<Value extends string> {
  label: string;
  name: string;
  value: Value;
  options: readonly SegmentedControlOption<Value>[];
  onChange: (value: Value) => void;
  disabled?: boolean;
  className?: string;
}

export function SegmentedControl<Value extends string>({
  label,
  name,
  value,
  options,
  onChange,
  disabled = false,
  className
}: SegmentedControlProps<Value>) {
  const id = useId();

  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={clsx('bpp-segmented-control', className)}
    >
      {options.map((option) => {
        const optionDisabled = disabled || option.disabled;
        return (
          <label
            key={option.value}
            htmlFor={`${id}-${option.value}`}
            className="bpp-segmented-label"
          >
            <input
              id={`${id}-${option.value}`}
              className="bpp-segmented-input"
              type="radio"
              name={name}
              value={option.value}
              checked={value === option.value}
              disabled={optionDisabled}
              onChange={() => onChange(option.value)}
            />
            <span className="bpp-segmented-option">{option.label}</span>
          </label>
        );
      })}
    </div>
  );
}
