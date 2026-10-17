import { List, ListItem, ListItemText, Typography } from '@mui/material';
import type { ReconciliationCheck } from '../api/types';

/** Each check states "Passed" or "Failed" in text; colour and icon only reinforce it. */
export function ReconciliationList({ checks }: { checks: ReconciliationCheck[] | null }) {
  if (!checks) {
    return (
      <Typography variant="body2" color="text.secondary">
        Reconciliation runs when the run is generated and again on finalize.
      </Typography>
    );
  }
  return (
    <List dense aria-label="Reconciliation checks">
      {checks.map((c) => (
        <ListItem key={c.name} disableGutters>
          <ListItemText
            primary={
              <Typography component="span" color={c.passed ? 'success.main' : 'error.main'} sx={{ fontWeight: 600 }}>
                <span aria-hidden="true">{c.passed ? '✓ ' : '✗ '}</span>
                {c.passed ? 'Passed' : 'Failed'}: {c.name}
              </Typography>
            }
            secondary={c.detail}
          />
        </ListItem>
      ))}
    </List>
  );
}
