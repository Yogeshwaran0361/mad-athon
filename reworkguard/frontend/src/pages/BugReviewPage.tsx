import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Link,
  Pagination,
  Paper,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tabs,
  TextField,
} from '@mui/material';
import { useBugs, useReviewBug } from '../api/hooks';
import { ROOT_CAUSES, type BugDetail, type RootCause } from '../api/types';
import { AttributionDialog } from '../components/AttributionDialog';
import { ErrorAlert } from '../components/ErrorAlert';
import { humanize } from '../components/format';
import { PageHeader } from '../components/PageHeader';
import { BugStatusBadge } from '../components/StatusBadge';
import { useToast } from '../components/toastContext';

function ReviewDialog({ bug, onClose }: { bug: BugDetail; onClose: () => void }) {
  const review = useReviewBug();
  const { notify } = useToast();
  const [rootCause, setRootCause] = useState<RootCause>(bug.rootCause ?? 'OwnDefect');
  const [caused, setCaused] = useState(bug.causedByOriginalDelivery ?? true);
  const [justification, setJustification] = useState(bug.reviewJustification ?? '');
  const [regression, setRegression] = useState(bug.regressionOfBugId ? String(bug.regressionOfBugId) : '');
  const needsJustification = !(rootCause === 'OwnDefect' && caused);

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="review-title">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          review.mutate(
            {
              id: bug.id,
              input: {
                rootCause,
                causedByOriginalDelivery: caused,
                justification: justification.trim() || null,
                regressionOfBugId: regression ? Number(regression) : null,
              },
            },
            {
              onSuccess: (result) => {
                notify(`Bug #${result.id} reviewed: ${result.isDeductible ? 'deductible' : 'not deductible'}.`);
                onClose();
              },
            },
          );
        }}
      >
        <DialogTitle id="review-title">Review bug #{bug.id}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <div>{bug.description}</div>
            <TextField
              select
              label="Root cause"
              value={rootCause}
              onChange={(e) => {
                setRootCause(e.target.value as RootCause);
              }}
              slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
            >
              {ROOT_CAUSES.map((r) => (
                <option key={r} value={r}>
                  {humanize(r)}
                </option>
              ))}
            </TextField>
            <FormControlLabel
              control={
                <Checkbox
                  checked={caused}
                  onChange={(e) => {
                    setCaused(e.target.checked);
                  }}
                />
              }
              label="Caused by the original delivery"
            />
            <TextField
              type="number"
              label="Regression of bug # (optional)"
              value={regression}
              onChange={(e) => {
                setRegression(e.target.value);
              }}
              slotProps={{ htmlInput: { min: 1 } }}
            />
            <TextField
              label="Justification"
              multiline
              minRows={2}
              required={needsJustification}
              value={justification}
              onChange={(e) => {
                setJustification(e.target.value);
              }}
              helperText={needsJustification ? 'Required unless it is the deliverer’s own defect caused by the original delivery.' : 'Optional.'}
            />
            <ErrorAlert error={review.error} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={review.isPending}>
            Save review
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

export function BugReviewPage() {
  const [tab, setTab] = useState(0);
  const [page, setPage] = useState(1);
  const reviewed = tab === 1;
  const { data, isLoading, error } = useBugs({ reviewed, page });
  const [reviewing, setReviewing] = useState<BugDetail | null>(null);
  const [attribution, setAttribution] = useState<number | null>(null);

  return (
    <Stack spacing={2}>
      <PageHeader title="Bug review" subtitle="Decide each bug's root cause. Only the deliverer's own defect, caused by the original delivery inside warranty, is deductible." />
      <Tabs
        value={tab}
        onChange={(_, v: number) => {
          setTab(v);
          setPage(1);
        }}
        aria-label="Bug review queues"
      >
        <Tab label="Awaiting review" />
        <Tab label="Reviewed" />
      </Tabs>
      <ErrorAlert error={error} />
      {isLoading && <CircularProgress aria-label="Loading bugs" />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={reviewed ? 'Reviewed bugs' : 'Bugs awaiting review'}>
            <TableHead>
              <TableRow>
                <TableCell>#</TableCell>
                <TableCell>Task</TableCell>
                <TableCell>Severity</TableCell>
                <TableCell>Description</TableCell>
                <TableCell>Found</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Warranty</TableCell>
                <TableCell>Verdict</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.items.map((b) => (
                <TableRow key={b.id}>
                  <TableCell>{b.id}</TableCell>
                  <TableCell>
                    <Link component={RouterLink} to={`/tasks/${b.taskId}`}>
                      {b.taskTitle}
                    </Link>
                  </TableCell>
                  <TableCell>{b.severity}</TableCell>
                  <TableCell>{b.description}</TableCell>
                  <TableCell>{b.foundDate}</TableCell>
                  <TableCell>
                    <BugStatusBadge status={b.status} />
                  </TableCell>
                  <TableCell>{b.withinWarranty ? 'Within' : 'Outside'}</TableCell>
                  <TableCell>{b.rootCause ? `${humanize(b.rootCause)} · ${b.isDeductible ? 'deductible' : 'not deductible'}` : '–'}</TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                      <Button
                        size="small"
                        variant={reviewed ? 'text' : 'outlined'}
                        aria-label={`${reviewed ? 'Re-review' : 'Review'} bug ${b.id}`}
                        onClick={() => {
                          setReviewing(b);
                        }}
                      >
                        {reviewed ? 'Re-review' : 'Review'}
                      </Button>
                      {reviewed && (
                        <Button
                          size="small"
                          aria-label={`Attribution for bug ${b.id}`}
                          onClick={() => {
                            setAttribution(b.id);
                          }}
                        >
                          Attribution
                        </Button>
                      )}
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
              {data.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9}>{reviewed ? 'No reviewed bugs.' : 'Nothing awaiting review.'}</TableCell>
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
      {reviewing && (
        <ReviewDialog
          key={reviewing.id}
          bug={reviewing}
          onClose={() => {
            setReviewing(null);
          }}
        />
      )}
      {attribution !== null && (
        <AttributionDialog
          bugId={attribution}
          canOverride
          onClose={() => {
            setAttribution(null);
          }}
        />
      )}
    </Stack>
  );
}
