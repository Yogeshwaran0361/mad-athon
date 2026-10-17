import { useState } from 'react';
import {
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Pagination,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { useAdminEmployees, useChangePay, useCreateEmployee, usePayHistory, useUpdateEmployee } from '../../api/hooks';
import { PAY_MODES, ROLES, type ChangePayInput, type EmployeeAdmin, type PayMode, type Role } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { ErrorAlert } from '../../components/ErrorAlert';
import { formatMoney, formatUtc } from '../../components/format';
import { PageHeader } from '../../components/PageHeader';
import { useToast } from '../../components/toastContext';
import { todayUtc } from '../../forms/dates';

function rateOf(e: { payMode: PayMode; monthlyGross: number | null; dailyRate: number | null; hourlyRate: number | null }): string {
  const value = e.payMode === 'Monthly' ? e.monthlyGross : e.payMode === 'Daily' ? e.dailyRate : e.hourlyRate;
  return `${formatMoney(value)} / ${e.payMode === 'Monthly' ? 'month' : e.payMode === 'Daily' ? 'day' : 'hour'}`;
}

function NativeSelect<T extends string>({ label, value, options, onChange }: { label: string; value: T; options: readonly T[]; onChange: (v: T) => void }) {
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value}
      onChange={(e) => {
        onChange(e.target.value as T);
      }}
      slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
    >
      {options.map((o) => (
        <option key={o} value={o}>
          {o}
        </option>
      ))}
    </TextField>
  );
}

function PayFields({ value, onChange }: { value: ChangePayInput; onChange: (v: ChangePayInput) => void }) {
  const field = value.payMode === 'Monthly' ? 'monthlyGross' : value.payMode === 'Daily' ? 'dailyRate' : 'hourlyRate';
  const label = value.payMode === 'Monthly' ? 'Monthly gross' : value.payMode === 'Daily' ? 'Daily rate' : 'Hourly rate';
  return (
    <>
      <NativeSelect
        label="Pay mode"
        value={value.payMode}
        options={PAY_MODES}
        onChange={(payMode) => {
          onChange({ ...value, payMode, monthlyGross: null, dailyRate: null, hourlyRate: null });
        }}
      />
      <TextField
        size="small"
        type="number"
        label={label}
        required
        value={value[field] ?? ''}
        onChange={(e) => {
          onChange({ ...value, [field]: e.target.value === '' ? null : Number(e.target.value) });
        }}
        slotProps={{ htmlInput: { min: 0.01, step: 0.01 } }}
      />
      <TextField
        size="small"
        type="date"
        label="Effective from"
        required
        value={value.effectiveFrom}
        onChange={(e) => {
          onChange({ ...value, effectiveFrom: e.target.value });
        }}
        slotProps={{ inputLabel: { shrink: true } }}
      />
    </>
  );
}

function PayDialog({ employee, editable, onClose }: { employee: EmployeeAdmin; editable: boolean; onClose: () => void }) {
  const history = usePayHistory(employee.id);
  const change = useChangePay(employee.id);
  const { notify } = useToast();
  const [pay, setPay] = useState<ChangePayInput>({ payMode: employee.payMode, monthlyGross: null, dailyRate: null, hourlyRate: null, effectiveFrom: todayUtc() });
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md" aria-labelledby="pay-title">
      <DialogTitle id="pay-title">Pay history – {employee.name}</DialogTitle>
      <DialogContent>
        <ErrorAlert error={history.error} />
        {history.isLoading && <CircularProgress aria-label="Loading pay history" />}
        <Table size="small" aria-label="Rate history">
          <TableHead>
            <TableRow>
              <TableCell>Effective from</TableCell>
              <TableCell>Rate</TableCell>
              <TableCell>Recorded</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {history.data?.map((r) => (
              <TableRow key={r.id}>
                <TableCell>{r.effectiveFrom}</TableCell>
                <TableCell>{rateOf(r)}</TableCell>
                <TableCell>
                  {formatUtc(r.recordedAtUtc)}
                  {r.recordedByEmployeeId !== null && ` by #${r.recordedByEmployeeId}`}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {editable && (
          <Stack
            component="form"
            spacing={2}
            sx={{ mt: 3 }}
            onSubmit={(e) => {
              e.preventDefault();
              change.mutate(pay, {
                onSuccess: () => {
                  notify(`Pay change recorded for ${employee.name}.`);
                },
              });
            }}
          >
            <Typography variant="subtitle1" component="h3">
              Record a pay change
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Earlier work keeps the rate in force when it was done.
            </Typography>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <PayFields value={pay} onChange={setPay} />
            </Stack>
            <ErrorAlert error={change.error} />
            <Button type="submit" variant="contained" disabled={change.isPending} sx={{ alignSelf: 'flex-start' }}>
              Save pay change
            </Button>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

function EditDialog({ employee, onClose }: { employee: EmployeeAdmin; onClose: () => void }) {
  const update = useUpdateEmployee();
  const { notify } = useToast();
  const [name, setName] = useState(employee.name);
  const [role, setRole] = useState<Role>(employee.role);
  const [managerId, setManagerId] = useState(employee.managerId ? String(employee.managerId) : '');
  const [isActive, setIsActive] = useState(employee.isActive);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="edit-title">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          update.mutate(
            { id: employee.id, input: { name: name.trim(), role, managerId: managerId ? Number(managerId) : null, isActive } },
            {
              onSuccess: () => {
                notify(`Saved ${name.trim()}.`);
                onClose();
              },
            },
          );
        }}
      >
        <DialogTitle id="edit-title">Edit {employee.name}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField
              size="small"
              label="Name"
              required
              value={name}
              onChange={(e) => {
                setName(e.target.value);
              }}
            />
            <NativeSelect label="Role" value={role} options={ROLES} onChange={setRole} />
            <TextField
              size="small"
              type="number"
              label="Manager id (optional)"
              value={managerId}
              onChange={(e) => {
                setManagerId(e.target.value);
              }}
            />
            <FormControlLabel
              control={
                <Checkbox
                  checked={isActive}
                  onChange={(e) => {
                    setIsActive(e.target.checked);
                  }}
                />
              }
              label="Active"
            />
            <ErrorAlert error={update.error} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={update.isPending}>
            Save
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function CreateDialog({ onClose }: { onClose: () => void }) {
  const create = useCreateEmployee();
  const { notify } = useToast();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<Role>('Employee');
  const [managerId, setManagerId] = useState('');
  const [password, setPassword] = useState('');
  const [pay, setPay] = useState<ChangePayInput>({ payMode: 'Monthly', monthlyGross: null, dailyRate: null, hourlyRate: null, effectiveFrom: todayUtc() });
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="create-title">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate(
            {
              code: code.trim(),
              name: name.trim(),
              email: email.trim(),
              role,
              managerId: managerId ? Number(managerId) : null,
              password,
              payMode: pay.payMode,
              monthlyGross: pay.monthlyGross,
              dailyRate: pay.dailyRate,
              hourlyRate: pay.hourlyRate,
              payEffectiveFrom: pay.effectiveFrom,
            },
            {
              onSuccess: (created) => {
                notify(`Created ${created.name}.`);
                onClose();
              },
            },
          );
        }}
      >
        <DialogTitle id="create-title">New employee</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField size="small" label="Code" required value={code} onChange={(e) => { setCode(e.target.value); }} />
            <TextField size="small" label="Name" required value={name} onChange={(e) => { setName(e.target.value); }} />
            <TextField size="small" type="email" label="Email" required value={email} onChange={(e) => { setEmail(e.target.value); }} />
            <NativeSelect label="Role" value={role} options={ROLES} onChange={setRole} />
            <TextField size="small" type="number" label="Manager id (optional)" value={managerId} onChange={(e) => { setManagerId(e.target.value); }} />
            <PayFields value={pay} onChange={setPay} />
            <TextField
              size="small"
              type="password"
              label="Initial password"
              required
              autoComplete="new-password"
              helperText="At least 12 characters, including a letter and a digit."
              value={password}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
            />
            <ErrorAlert error={create.error} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={create.isPending}>
            Create
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

export function EmployeesAdminPage() {
  const { hasRole } = useAuth();
  const editable = hasRole('Admin');
  const [search, setSearch] = useState('');
  const [includeInactive, setIncludeInactive] = useState(false);
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useAdminEmployees(search.trim(), includeInactive, page);
  const [payFor, setPayFor] = useState<EmployeeAdmin | null>(null);
  const [editing, setEditing] = useState<EmployeeAdmin | null>(null);
  const [creating, setCreating] = useState(false);

  return (
    <Stack spacing={2}>
      <PageHeader
        title="Employees & pay"
        actions={
          editable && (
            <Button
              variant="contained"
              onClick={() => {
                setCreating(true);
              }}
            >
              New employee
            </Button>
          )
        }
      />
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <TextField
          size="small"
          label="Search"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
        />
        <FormControlLabel
          control={
            <Checkbox
              checked={includeInactive}
              onChange={(e) => {
                setIncludeInactive(e.target.checked);
                setPage(1);
              }}
            />
          }
          label="Include inactive"
        />
      </Stack>
      <ErrorAlert error={error} />
      {isLoading && <CircularProgress aria-label="Loading employees" />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small" aria-label="Employees">
            <TableHead>
              <TableRow>
                <TableCell>Code</TableCell>
                <TableCell>Name</TableCell>
                <TableCell>Email</TableCell>
                <TableCell>Role</TableCell>
                <TableCell>Pay</TableCell>
                <TableCell>Status</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.items.map((e) => (
                <TableRow key={e.id}>
                  <TableCell>{e.code}</TableCell>
                  <TableCell>{e.name}</TableCell>
                  <TableCell>{e.email}</TableCell>
                  <TableCell>{e.role}</TableCell>
                  <TableCell>{rateOf(e)}</TableCell>
                  <TableCell>{e.isActive ? 'Active' : 'Inactive'}</TableCell>
                  <TableCell align="right">
                    <Button
                      size="small"
                      aria-label={`Pay history for ${e.name}`}
                      onClick={() => {
                        setPayFor(e);
                      }}
                    >
                      Pay
                    </Button>
                    {editable && (
                      <Button
                        size="small"
                        aria-label={`Edit ${e.name}`}
                        onClick={() => {
                          setEditing(e);
                        }}
                      >
                        Edit
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
              {data.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>No employees match.</TableCell>
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
      {payFor && (
        <PayDialog
          employee={payFor}
          editable={editable}
          onClose={() => {
            setPayFor(null);
          }}
        />
      )}
      {editing && (
        <EditDialog
          employee={editing}
          onClose={() => {
            setEditing(null);
          }}
        />
      )}
      {creating && (
        <CreateDialog
          onClose={() => {
            setCreating(false);
          }}
        />
      )}
    </Stack>
  );
}
