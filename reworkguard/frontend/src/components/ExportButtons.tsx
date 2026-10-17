import { useState } from 'react';
import { Button, ButtonGroup } from '@mui/material';
import { downloadFile, qs } from '../api/client';
import { ErrorAlert } from './ErrorAlert';

const FORMATS = ['xlsx', 'csv', 'pdf'] as const;

/** Export buttons for a report; files are fetched with the bearer token and saved locally. */
export function ExportButtons({ report, params, label, disabled }: { report: string; params: Record<string, string>; label: string; disabled?: boolean }) {
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  return (
    <>
      <ButtonGroup size="small" variant="outlined" aria-label={`Export ${label}`} disabled={disabled}>
        {FORMATS.map((format) => (
          <Button
            key={format}
            disabled={busy !== null}
            aria-label={`Export ${label} as ${format.toUpperCase()}`}
            onClick={() => {
              setBusy(format);
              setError(null);
              downloadFile(`/reports/${report}${qs({ ...params, format })}`, `${report}.${format}`)
                .catch((e: unknown) => {
                  setError(e);
                })
                .finally(() => {
                  setBusy(null);
                });
            }}
          >
            {busy === format ? '…' : format.toUpperCase()}
          </Button>
        ))}
      </ButtonGroup>
      <ErrorAlert error={error} />
    </>
  );
}
