import { Alert, Snackbar, type AlertColor } from '@mui/material';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { ToastContext } from './toastContext';

interface ToastMessage {
  id: number;
  text: string;
  severity: AlertColor;
}

/**
 * Shows transient confirmations. The text is mirrored into a permanently mounted polite live region so screen readers
 * announce it even though the Snackbar mounts and unmounts.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [current, setCurrent] = useState<ToastMessage | null>(null);
  const [announcement, setAnnouncement] = useState('');

  const notify = useCallback((text: string, severity: AlertColor = 'success') => {
    setCurrent({ id: Date.now(), text, severity });
    setAnnouncement(text);
  }, []);

  const value = useMemo(() => ({ notify }), [notify]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div role="status" aria-live="polite" aria-atomic="true" className="visually-hidden" style={visuallyHidden}>
        {announcement}
      </div>
      <Snackbar
        key={current?.id}
        open={current !== null}
        autoHideDuration={6000}
        onClose={(_, reason) => {
          if (reason !== 'clickaway') {
            setCurrent(null);
          }
        }}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        {current ? (
          <Alert
            severity={current.severity}
            variant="filled"
            onClose={() => {
              setCurrent(null);
            }}
            role="presentation"
          >
            {current.text}
          </Alert>
        ) : undefined}
      </Snackbar>
    </ToastContext.Provider>
  );
}

const visuallyHidden: React.CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  padding: 0,
  margin: -1,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
  border: 0,
};
