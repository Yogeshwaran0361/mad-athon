import { useState } from 'react';
import { Alert, Button, CircularProgress, Pagination, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField } from '@mui/material';
import { downloadFile, qs } from '../api/client';
import { useAuditEvents, useVerifyAudit } from '../api/hooks';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatUtc } from '../components/format';
import { PageHeader } from '../components/PageHeader';

export function AuditPage() {
  const [entityType, setEntityType] = useState('');
  const [entityId, setEntityId] = useState('');
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useAuditEvents(entityType.trim(), entityId.trim(), page);
  const verify = useVerifyAudit();
  const [exportError, setExportError] = useState<unknown>(null);

  return (
    <Stack spacing={2}>
      <PageHeader
        title="Audit trail"
        subtitle="Every change, append-only and hash-chained."
        actions={
          <>
            <Button
              variant="outlined"
              disabled={verify.isPending}
              onClick={() => {
                verify.mutate();
              }}
            >
              Verify chain
            </Button>
            <Button
              variant="outlined"
              onClick={() => {
                setExportError(null);
                downloadFile(`/audit-events/export.csv${qs({ entityType: entityType.trim(), entityId: entityId.trim() })}`, 'audit-events.csv').catch((e: unknown) => {
                  setExportError(e);
                });
              }}
            >
              Export CSV
            </Button>
          </>
        }
      />
      <div aria-live="polite">
        {verify.data && (
          <Alert severity={verify.data.valid ? 'success' : 'error'}>
            {verify.data.valid
              ? `Chain intact: ${verify.data.checkedCount} events verified.`
              : `Chain broken at event #${verify.data.firstBrokenId ?? '?'} after checking ${verify.data.checkedCount} events.`}
          </Alert>
        )}
      </div>
      <ErrorAlert error={verify.error ?? exportError} />
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <TextField
          size="small"
          label="Entity type"
          placeholder="e.g. Deduction"
          value={entityType}
          onChange={(e) => {
            setEntityType(e.target.value);
            setPage(1);
          }}
        />
        <TextField
          size="small"
          label="Entity id"
          value={entityId}
          onChange={(e) => {
            setEntityId(e.target.value);
            setPage(1);
          }}
        />
      </Stack>
      <ErrorAlert error={error} />
      {isLoading && <CircularProgress aria-label="Loading audit events" />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small" aria-label="Audit events">
            <TableHead>
              <TableRow>
                <TableCell>#</TableCell>
                <TableCell>When</TableCell>
                <TableCell>Actor</TableCell>
                <TableCell>Entity</TableCell>
                <TableCell>Action</TableCell>
                <TableCell>Details</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.items.map((e) => (
                <TableRow key={e.id}>
                  <TableCell>{e.id}</TableCell>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatUtc(e.occurredAtUtc)}</TableCell>
                  <TableCell>{e.actorEmployeeId ?? 'system'}</TableCell>
                  <TableCell>
                    {e.entityType} #{e.entityId}
                  </TableCell>
                  <TableCell>{e.action}</TableCell>
                  <TableCell>
                    <code style={{ fontSize: 12, wordBreak: 'break-all' }}>{e.detailsJson}</code>
                  </TableCell>
                </TableRow>
              ))}
              {data.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>No events match.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {data && data.totalPages > 1 && (
        <Pagination
          count={data.totalPages}
          page={page}
          onChange={(_, p) => {
            setPage(p);
          }}
        />
      )}
    </Stack>
  );
}
