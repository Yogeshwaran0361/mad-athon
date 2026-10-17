import { Alert, AlertTitle, Button } from '@mui/material';
import { ApiError } from '../api/client';

/**
 * Shows an API problem. Conflicts (409) show their domain `code` so operators can tell rules apart, and a stale-version
 * conflict offers to reload the latest data.
 */
export function ErrorAlert({ error, onReload }: { error: unknown; onReload?: () => void }) {
  if (!error) {
    return null;
  }
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  const apiError = error instanceof ApiError ? error : null;
  const conflict = apiError?.status === 409;
  const stale = apiError?.code?.endsWith('.stale') ?? false;
  return (
    <Alert
      severity={conflict ? 'warning' : 'error'}
      sx={{ my: 1 }}
      action={
        stale && onReload ? (
          <Button color="inherit" size="small" onClick={onReload}>
            Reload latest
          </Button>
        ) : undefined
      }
    >
      {conflict && <AlertTitle>{stale ? 'This changed since you loaded it' : 'Not allowed right now'}</AlertTitle>}
      {message}
      {apiError?.code && (
        <>
          {' '}
          <code>({apiError.code})</code>
        </>
      )}
      {stale && !onReload && ' Reload the page to see the latest version.'}
    </Alert>
  );
}
