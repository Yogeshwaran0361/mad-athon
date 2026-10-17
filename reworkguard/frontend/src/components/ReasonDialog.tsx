import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField } from '@mui/material';
import { useState, type ReactNode } from 'react';

interface Props {
  open: boolean;
  title: string;
  description?: ReactNode;
  label: string;
  minLength: number;
  confirmLabel: string;
  busy?: boolean;
  children?: ReactNode;
  onClose: () => void;
  onConfirm: (reason: string) => void;
}

/** A dialog that asks for a written reason of a minimum length before confirming. */
export function ReasonDialog({ open, title, description, label, minLength, confirmLabel, busy, children, onClose, onConfirm }: Props) {
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);
  const tooShort = reason.trim().length < minLength;
  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth="sm"
      aria-labelledby="reason-dialog-title"
      slotProps={{
        transition: {
          onExited: () => {
            setReason('');
            setTouched(false);
          },
        },
      }}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setTouched(true);
          if (!tooShort) {
            onConfirm(reason.trim());
          }
        }}
      >
        <DialogTitle id="reason-dialog-title">{title}</DialogTitle>
        <DialogContent>
          {description && <DialogContentText sx={{ mb: 2 }}>{description}</DialogContentText>}
          {children}
          <TextField
            autoFocus
            fullWidth
            multiline
            minRows={3}
            label={label}
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
            }}
            onBlur={() => {
              setTouched(true);
            }}
            error={touched && tooShort}
            helperText={`At least ${minLength} characters.`}
            required
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={busy}>
            {confirmLabel}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
