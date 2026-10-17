import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, qs } from './client';
import type {
  ApprovalStatus,
  AppNotification,
  Attribution,
  AuditEvent,
  AuditVerifyResult,
  BugDetail,
  BugStatus,
  ApproveChangeRequestInput,
  ChangeRequest,
  IncentiveAward,
  CreateChangeRequestInput,
  ChangePayInput,
  CreateEmployeeInput,
  Deduction,
  DeductionStatus,
  EmployeeAdmin,
  Holiday,
  NotificationPreference,
  NotificationType,
  OutboxMessage,
  OutboxStatus,
  OverrideAttributionInput,
  PayrollPeriod,
  PayrollRunDetail,
  PayrollRunEmployeeDetail,
  PayrollRunSummary,
  PayrollStep,
  PayslipDetail,
  PayslipSummary,
  Policy,
  ProjectWarranty,
  RateHistory,
  ReportRows,
  ResolveDisputeInput,
  ReviewBugInput,
  ScheduledJob,
  UpdateEmployeeInput,
  BugReported,
  DeductionPreview,
  Employee,
  LogTimeInput,
  PagedResult,
  ReportBugInput,
  TaskDetail,
  TaskSummary,
  TimeLog,
  WorkTaskStatus,
} from './types';

const MONTH_RE = /^\d{4}-(0[1-9]|1[0-2])$/;

export const keys = {
  tasks: (page: number, status: string) => ['tasks', page, status] as const,
  task: (id: number) => ['task', id] as const,
  queue: (page: number) => ['approval-queue', page] as const,
  myLogs: ['my-logs'] as const,
  employees: ['employees'] as const,
  preview: (employeeId: number, month: string) => ['preview', employeeId, month] as const,
};

export function useTasks(page: number, status: WorkTaskStatus | '', pageSize = 20) {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (status) {
    query.set('status', status);
  }
  return useQuery({
    queryKey: [...keys.tasks(page, status), pageSize],
    queryFn: () => api<PagedResult<TaskSummary>>(`/tasks?${query.toString()}`),
  });
}

export function useTask(id: number) {
  return useQuery({ queryKey: keys.task(id), queryFn: () => api<TaskDetail>(`/tasks/${id}`) });
}

export function useTransitionTask(id: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { targetStatus: WorkTaskStatus; reason: string | null }) =>
      api<TaskDetail>(`/tasks/${id}/transitions`, { method: 'POST', body: input }),
    onSuccess: (detail) => {
      client.setQueryData(keys.task(id), detail);
      void client.invalidateQueries({ queryKey: ['tasks'] });
    },
  });
}

export function useReportBug(taskId: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: ReportBugInput) => api<BugReported>(`/tasks/${taskId}/bugs`, { method: 'POST', body: input }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: keys.task(taskId) });
      void client.invalidateQueries({ queryKey: ['tasks'] });
    },
  });
}

export function useLogTime() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: LogTimeInput) => api<TimeLog>('/timelogs', { method: 'POST', body: input }),
    onSuccess: (log) => {
      void client.invalidateQueries({ queryKey: keys.task(log.taskId) });
      void client.invalidateQueries({ queryKey: keys.myLogs });
    },
  });
}

export function useApprovalQueue(page: number) {
  return useQuery({
    queryKey: keys.queue(page),
    queryFn: () => api<PagedResult<TimeLog>>(`/timelogs/approval-queue?page=${page}&pageSize=20`),
  });
}

export function useDecideTimeLog() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, decision, reason }: { id: number; decision: Exclude<ApprovalStatus, 'Pending'>; reason?: string }) =>
      decision === 'Approved'
        ? api<TimeLog>(`/timelogs/${id}/approve`, { method: 'POST' })
        : api<TimeLog>(`/timelogs/${id}/reject`, { method: 'POST', body: { reason: reason ?? null } }),
    onSuccess: (log) => {
      void client.invalidateQueries({ queryKey: ['approval-queue'] });
      void client.invalidateQueries({ queryKey: keys.task(log.taskId) });
    },
  });
}

export function useEmployees(enabled: boolean) {
  return useQuery({
    queryKey: keys.employees,
    queryFn: () => api<PagedResult<Employee>>('/employees?pageSize=100'),
    enabled,
  });
}

export function useDeductionPreview(employeeId: number | null, periodMonth: string) {
  const validMonth = periodMonth === '' || MONTH_RE.test(periodMonth);
  return useQuery({
    queryKey: keys.preview(employeeId ?? 0, periodMonth),
    queryFn: () => api<DeductionPreview>(`/deductions/preview${qs({ employeeId, periodMonth })}`),
    enabled: employeeId !== null && validMonth,
  });
}

// ---- Bugs, review and attribution ----

export function useBugs(params: { reviewed?: boolean; status?: BugStatus; page: number }) {
  return useQuery({
    queryKey: ['bugs', params],
    queryFn: () => api<PagedResult<BugDetail>>(`/bugs${qs({ ...params, pageSize: 20 })}`),
  });
}

export function useReviewBug() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number; input: ReviewBugInput }) => api<BugDetail>(`/bugs/${id}/review`, { method: 'POST', body: input }),
    onSuccess: (bug) => {
      void client.invalidateQueries({ queryKey: ['bugs'] });
      void client.invalidateQueries({ queryKey: keys.task(bug.taskId) });
      void client.invalidateQueries({ queryKey: ['attribution', bug.id] });
    },
  });
}

export function useAttribution(bugId: number, enabled = true) {
  return useQuery({
    queryKey: ['attribution', bugId],
    queryFn: () => api<Attribution>(`/bugs/${bugId}/attribution`),
    enabled,
  });
}

export function useOverrideAttribution(bugId: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: OverrideAttributionInput) => api<Attribution>(`/bugs/${bugId}/attribution`, { method: 'PUT', body: input }),
    onSuccess: (attribution) => {
      client.setQueryData(['attribution', bugId], attribution);
      void client.invalidateQueries({ queryKey: ['deductions'] });
    },
  });
}

// ---- Deductions ----

export interface DeductionFilter {
  employeeId?: number;
  status?: DeductionStatus;
  applyMonth?: string;
  bugId?: number;
  openDispute?: boolean;
  page: number;
  pageSize?: number;
}

export function useDeductions(filter: DeductionFilter, enabled = true) {
  return useQuery({
    queryKey: ['deductions', filter],
    queryFn: () => api<PagedResult<Deduction>>(`/deductions${qs({ pageSize: 50, ...filter })}`),
    enabled,
  });
}

function useDeductionMutation<TInput, TResult>(fn: (input: TInput) => Promise<TResult>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['deductions'] });
      void client.invalidateQueries({ queryKey: ['preview'] });
    },
  });
}

export function useNotifyDeductions() {
  return useDeductionMutation((ids: number[]) => api<Deduction[]>('/deductions/notify', { method: 'POST', body: { deductionIds: ids } }));
}

export function useApproveDeductions() {
  return useDeductionMutation((ids: number[]) => api<Deduction[]>('/deductions/approve', { method: 'POST', body: { deductionIds: ids } }));
}

export function useDisputeDeduction() {
  return useDeductionMutation(({ id, reason }: { id: number; reason: string }) =>
    api<Deduction>(`/deductions/${id}/dispute`, { method: 'POST', body: { reason } }),
  );
}

export function useResolveDispute() {
  return useDeductionMutation(({ id, input }: { id: number; input: ResolveDisputeInput }) =>
    api<Deduction>(`/deductions/${id}/dispute/resolve`, { method: 'POST', body: input }),
  );
}

export function useCancelDeduction() {
  return useDeductionMutation(({ id, reason }: { id: number; reason: string }) =>
    api<Deduction>(`/deductions/${id}/cancel`, { method: 'POST', body: { reason } }),
  );
}

export function useAcknowledgeDeduction() {
  return useDeductionMutation((id: number) => api<Deduction>(`/deductions/${id}/acknowledge`, { method: 'POST' }));
}

// ---- Payroll ----

export function usePayrollRuns(periodMonth: string) {
  return useQuery({
    queryKey: ['payroll-runs', periodMonth],
    queryFn: () => api<PayrollRunSummary[]>(`/payroll/runs${qs({ periodMonth })}`),
    enabled: periodMonth === '' || MONTH_RE.test(periodMonth),
  });
}

export function usePayrollRun(id: number) {
  return useQuery({ queryKey: ['payroll-run', id], queryFn: () => api<PayrollRunDetail>(`/payroll/runs/${id}`) });
}

export function usePayrollRunEmployee(id: number, employeeId: number) {
  return useQuery({
    queryKey: ['payroll-run', id, 'employee', employeeId],
    queryFn: () => api<PayrollRunEmployeeDetail>(`/payroll/runs/${id}/employees/${employeeId}`),
  });
}

export function usePayrollPeriods() {
  return useQuery({ queryKey: ['payroll-periods'], queryFn: () => api<PayrollPeriod[]>('/payroll/periods') });
}

export function useGeneratePayrollRun() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (periodMonth: string) => api<PayrollRunDetail>('/payroll/runs', { method: 'POST', body: { periodMonth } }),
    onSuccess: (detail) => {
      client.setQueryData(['payroll-run', detail.run.id], detail);
      void client.invalidateQueries({ queryKey: ['payroll-runs'] });
    },
  });
}

export function usePayrollStep(id: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ step, expectedVersion }: { step: PayrollStep; expectedVersion: number }) =>
      api<PayrollRunDetail>(`/payroll/runs/${id}/${step}`, { method: 'POST', body: { expectedVersion } }),
    onSuccess: (detail) => {
      client.setQueryData(['payroll-run', id], detail);
      void client.invalidateQueries({ queryKey: ['payroll-runs'] });
      void client.invalidateQueries({ queryKey: ['payroll-periods'] });
    },
  });
}

// ---- Payslips ----

export function usePayslips(params: { employeeId?: number | null; periodMonth?: string }) {
  return useQuery({
    queryKey: ['payslips', params],
    queryFn: () => api<PayslipSummary[]>(`/payslips${qs(params)}`),
  });
}

export function usePayslip(id: number | null) {
  return useQuery({
    queryKey: ['payslip', id],
    queryFn: () => api<PayslipDetail>(`/payslips/${id ?? 0}`),
    enabled: id !== null,
  });
}

// ---- Reports ----

export type ReportName = 'rework-summary' | 'by-employee' | 'by-module' | 'payroll-register';

export function useReport<T>(name: ReportName, params: Record<string, string>, enabled: boolean) {
  return useQuery({
    queryKey: ['report', name, params],
    queryFn: () => api<ReportRows<T>>(`/reports/${name}${qs(params)}`),
    enabled,
  });
}

// ---- Notifications ----

export function useUnreadCount(enabled: boolean) {
  return useQuery({
    queryKey: ['notifications', 'unread'],
    queryFn: () => api<{ unread: number }>('/notifications/unread-count'),
    enabled,
    refetchInterval: 60_000,
  });
}

export function useNotifications(page: number, unreadOnly: boolean) {
  return useQuery({
    queryKey: ['notifications', 'list', page, unreadOnly],
    queryFn: () => api<PagedResult<AppNotification>>(`/notifications${qs({ page, pageSize: 20, unreadOnly })}`),
  });
}

export function useMarkNotificationRead() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: number | 'all') =>
      id === 'all' ? api<unknown>('/notifications/read-all', { method: 'POST' }) : api<unknown>(`/notifications/${id}/read`, { method: 'POST' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['notifications'] });
    },
  });
}

export function useNotificationPreferences() {
  return useQuery({ queryKey: ['notification-preferences'], queryFn: () => api<NotificationPreference[]>('/notifications/preferences') });
}

export function useUpdateNotificationPreferences() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (preferences: { type: NotificationType; emailEnabled: boolean }[]) =>
      api<NotificationPreference[]>('/notifications/preferences', { method: 'PUT', body: { preferences } }),
    onSuccess: (prefs) => {
      client.setQueryData(['notification-preferences'], prefs);
    },
  });
}

// ---- Admin ----

export function usePolicy() {
  return useQuery({ queryKey: ['admin', 'policy'], queryFn: () => api<Policy>('/admin/policies') });
}

export function useUpdatePolicy() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (policy: Policy) => api<Policy>('/admin/policies', { method: 'PUT', body: policy }),
    onSuccess: (policy) => {
      client.setQueryData(['admin', 'policy'], policy);
    },
  });
}

export function useProjectWarranties() {
  return useQuery({ queryKey: ['admin', 'projects'], queryFn: () => api<ProjectWarranty[]>('/admin/projects') });
}

export function useUpdateWarranty() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, warrantyDays }: { id: number; warrantyDays: number }) =>
      api<ProjectWarranty>(`/admin/projects/${id}/warranty`, { method: 'PUT', body: { warrantyDays } }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'projects'] });
    },
  });
}

export function useHolidays(year: number) {
  return useQuery({ queryKey: ['admin', 'holidays', year], queryFn: () => api<Holiday[]>(`/admin/holidays?year=${year}`) });
}

export function useAddHoliday() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { date: string; name: string }) => api<Holiday>('/admin/holidays', { method: 'POST', body: input }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'holidays'] });
    },
  });
}

export function useRemoveHoliday() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api<undefined>(`/admin/holidays/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'holidays'] });
    },
  });
}

export function useAdminEmployees(search: string, includeInactive: boolean, page: number) {
  return useQuery({
    queryKey: ['admin', 'employees', search, includeInactive, page],
    queryFn: () => api<PagedResult<EmployeeAdmin>>(`/admin/employees${qs({ search, includeInactive, page, pageSize: 20 })}`),
  });
}

export function useCreateEmployee() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateEmployeeInput) => api<EmployeeAdmin>('/admin/employees', { method: 'POST', body: input }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'employees'] });
    },
  });
}

export function useUpdateEmployee() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number; input: UpdateEmployeeInput }) => api<EmployeeAdmin>(`/admin/employees/${id}`, { method: 'PUT', body: input }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'employees'] });
    },
  });
}

export function usePayHistory(id: number | null) {
  return useQuery({
    queryKey: ['admin', 'pay', id],
    queryFn: () => api<RateHistory[]>(`/admin/employees/${id ?? 0}/pay`),
    enabled: id !== null,
  });
}

export function useChangePay(id: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: ChangePayInput) => api<RateHistory[]>(`/admin/employees/${id}/pay`, { method: 'POST', body: input }),
    onSuccess: (history) => {
      client.setQueryData(['admin', 'pay', id], history);
      void client.invalidateQueries({ queryKey: ['admin', 'employees'] });
    },
  });
}

export function useOutbox(status: OutboxStatus | '', page: number) {
  return useQuery({
    queryKey: ['admin', 'outbox', status, page],
    queryFn: () => api<PagedResult<OutboxMessage>>(`/admin/outbox${qs({ status, page, pageSize: 20 })}`),
  });
}

export function useRetryOutbox() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api<undefined>(`/admin/outbox/${id}/retry`, { method: 'POST' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'outbox'] });
    },
  });
}

export function useJobs() {
  return useQuery({ queryKey: ['admin', 'jobs'], queryFn: () => api<ScheduledJob[]>('/admin/jobs') });
}

export function useTriggerJob() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api<{ jobId: string }>(`/admin/jobs/${encodeURIComponent(id)}/trigger`, { method: 'POST' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'jobs'] });
    },
  });
}

export function useDashboardSession() {
  return useMutation({ mutationFn: () => api<undefined>('/admin/jobs/dashboard-session', { method: 'POST' }) });
}

// ---- Audit ----

export function useAuditEvents(entityType: string, entityId: string, page: number) {
  return useQuery({
    queryKey: ['audit', entityType, entityId, page],
    queryFn: () => api<PagedResult<AuditEvent>>(`/audit-events${qs({ entityType, entityId, page, pageSize: 50 })}`),
  });
}

export function useVerifyAudit() {
  return useMutation({ mutationFn: () => api<AuditVerifyResult>('/audit/verify') });
}

export function useChangeRequests() {
  return useQuery({ queryKey: ['change-requests'], queryFn: () => api<ChangeRequest[]>('/change-requests') });
}

export function useCreateChangeRequest() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateChangeRequestInput) => api<ChangeRequest>('/change-requests', { method: 'POST', body: input }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['change-requests'] });
    },
  });
}

export function useApproveChangeRequest() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number; input: ApproveChangeRequestInput }) =>
      api<ChangeRequest>(`/change-requests/${id}/approve`, { method: 'POST', body: input }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['change-requests'] });
    },
  });
}

export function useCloseChangeRequest() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, receivedAmountExTax }: { id: number; receivedAmountExTax: number }) =>
      api<ChangeRequest>(`/change-requests/${id}/close`, { method: 'POST', body: { receivedAmountExTax } }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['change-requests'] });
      void client.invalidateQueries({ queryKey: ['incentive-awards'] });
    },
  });
}

export function useIncentiveAwards() {
  return useQuery({ queryKey: ['incentive-awards'], queryFn: () => api<IncentiveAward[]>('/incentive-awards') });
}

export function useDecideAward() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, decision, reason }: { id: number; decision: 'Approved' | 'Rejected'; reason?: string }) =>
      decision === 'Approved'
        ? api<IncentiveAward>(`/incentive-awards/${id}/approve`, { method: 'POST' })
        : api<IncentiveAward>(`/incentive-awards/${id}/reject`, { method: 'POST', body: { reason: reason ?? '' } }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['incentive-awards'] });
    },
  });
}
