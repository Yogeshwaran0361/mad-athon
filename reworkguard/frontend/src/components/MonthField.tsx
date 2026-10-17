import { TextField } from '@mui/material';

/** yyyy-MM input; an empty value means "not set". */
export function MonthField({ label, value, onChange, required }: { label: string; value: string; onChange: (value: string) => void; required?: boolean }) {
  return (
    <TextField
      type="month"
      size="small"
      label={label}
      value={value}
      required={required}
      onChange={(e) => {
        onChange(e.target.value);
      }}
      slotProps={{ inputLabel: { shrink: true }, htmlInput: { pattern: '\\d{4}-\\d{2}' } }}
    />
  );
}
