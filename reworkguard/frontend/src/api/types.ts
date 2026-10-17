export type Role = 'Employee' | 'QA' | 'TeamLead' | 'Manager' | 'Finance' | 'Admin';
export type PayMode = 'Monthly' | 'Daily' | 'Hourly';
export type WorkTaskStatus = 'Assigned' | 'InProgress' | 'Completed' | 'Reopened' | 'UnderFix' | 'Closed';
export type BugSeverity = 'Low' | 'Medium' | 'High' | 'Critical';
export type BugStatus = 'Open' | 'Fixed' | 'Verified' | 'Closed';
export type RootCause = 'OwnDefect' | 'NewRequirement' | 'SpecAmbiguity' | 'ExternalIssue' | 'QaMiss';
export type WorkType = 'Regular' | 'Corrective' | 'Enhancement';
export type ChangeRequestStatus = 'Requested' | 'Approved' | 'InProgress' | 'Completed' | 'Closed' | 'Rejected';
export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected';

export const ROLES: readonly Role[] = ['Employee', 'QA', 'TeamLead', 'Manager', 'Finance', 'Admin'];
export const PAY_MODES: readonly PayMode[] = ['Monthly', 'Daily', 'Hourly'];
export const TASK_STATUSES: readonly WorkTaskStatus[] = ['Assigned', 'InProgress', 'Completed', 'Reopened', 'UnderFix', 'Closed'];
export const BUG_SEVERITIES: readonly BugSeverity[] = ['Low', 'Medium', 'High', 'Critical'];

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Employee {
  id: number;
  code: string;
  name: string;
  email: string;
  role: Role;
  payMode: PayMode;
  isActive: boolean;
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  employee: Employee;
}

export interface Assignee {
  employeeId: number;
  name: string;
  isOriginalDeliverer: boolean;
}

export interface TaskSummary {
  id: number;
  title: string;
  moduleId: number;
  moduleName: string;
  projectId: number;
  projectName: string;
  status: WorkTaskStatus;
  committedDate: string;
  actualCompletionDate: string | null;
  reopenCount: number;
  assignees: Assignee[];
}

export interface TaskHistory {
  id: number;
  fromStatus: WorkTaskStatus;
  toStatus: WorkTaskStatus;
  changedByEmployeeId: number | null;
  changedByName: string;
  changedAtUtc: string;
  reason: string | null;
}

export interface TaskBug {
  id: number;
  severity: BugSeverity;
  description: string;
  foundDate: string;
  status: BugStatus;
  withinWarranty: boolean;
  rootCause: RootCause | null;
  causedByOriginalDelivery: boolean | null;
  isReviewed: boolean;
  isDeductible: boolean;
}

export interface TaskTimeLog {
  id: number;
  employeeId: number;
  employeeName: string;
  workDate: string;
  hours: number;
  workType: WorkType;
  bugId: number | null;
  approvalStatus: ApprovalStatus;
  note: string | null;
}

export interface TaskDetail {
  task: TaskSummary;
  warrantyDays: number;
  warrantyEndsOn: string | null;
  version: number;
  allowedTransitions: WorkTaskStatus[];
  history: TaskHistory[];
  bugs: TaskBug[];
  timeLogs: TaskTimeLog[];
  /** Set on the task that carries an approved change request; it takes Enhancement hours only. */
  changeRequestId: number | null;
}

export interface Bug {
  id: number;
  taskId: number;
  taskTitle: string;
  taskStatus: WorkTaskStatus;
  severity: BugSeverity;
  description: string;
  foundDate: string;
  status: BugStatus;
  withinWarranty: boolean;
  isDeductible: boolean;
}

export interface BugReported {
  bug: Bug;
  taskCompletionDate: string;
  warrantyEndsOn: string;
  reopenCount: number;
  originalDeliverers: Assignee[];
}

export interface TimeLog {
  id: number;
  taskId: number;
  taskTitle: string;
  taskStatus: WorkTaskStatus;
  employeeId: number;
  employeeName: string;
  workDate: string;
  hours: number;
  workType: WorkType;
  bugId: number | null;
  approvalStatus: ApprovalStatus;
  note: string | null;
}

export type PayrollItemKind = 'CarriedIn' | 'Current' | 'Refund';
export type DeductionKind = 'Standard' | 'Compensating';
export type DeductionStatus = 'Draft' | 'Notified' | 'Disputed' | 'Resolved' | 'Approved' | 'Locked' | 'Applied' | 'Cancelled' | 'Reversed';
export type DisputeOutcome = 'Upheld' | 'Rejected';
export type PayrollRunStatus = 'Preview' | 'Reviewed' | 'Approved' | 'Finalized' | 'Locked' | 'Superseded';
export type NotificationChannel = 'InApp' | 'Email';
export type OutboxStatus = 'Pending' | 'Sent' | 'Skipped' | 'DeadLettered';
export type NotificationType =
  | 'DeductionNotified'
  | 'DisputeWindowClosing'
  | 'DisputeRaised'
  | 'DisputeResolved'
  | 'DeductionApproved'
  | 'DeductionCancelled'
  | 'CompensationRaised'
  | 'BugReported'
  | 'TimeLogRejected'
  | 'PayrollRunAwaitingApproval'
  | 'PayrollRunFinalized'
  | 'PayslipPublished'
  | 'DeductionsReadyForApproval'
  | 'DisputeAwaitingDecision'
  | 'TimeLogsAwaitingApproval'
  | 'DraftDeductionsPending';

export const ROOT_CAUSES: readonly RootCause[] = ['OwnDefect', 'NewRequirement', 'SpecAmbiguity', 'ExternalIssue', 'QaMiss'];
export const DEDUCTION_STATUSES: readonly DeductionStatus[] = ['Draft', 'Notified', 'Disputed', 'Resolved', 'Approved', 'Locked', 'Applied', 'Cancelled', 'Reversed'];

export interface DeductionPreviewLine {
  deductionId: number;
  kind: PayrollItemKind;
  status: DeductionStatus;
  applyMonth: string;
  workDate: string;
  bugId: number;
  taskId: number;
  taskTitle: string;
  moduleName: string;
  hours: number;
  hourlyRate: number;
  sharePercent: number;
  due: number;
  applied: number;
  carriedForward: number;
}

export interface DeductionPreview {
  employeeId: number;
  employeeName: string;
  payMode: PayMode;
  periodMonth: string;
  source: string;
  payrollRunId: number | null;
  salaryBase: number;
  salaryBasis: string;
  capPercent: number;
  capAmount: number;
  carriedIn: number;
  currentDue: number;
  refunds: number;
  applied: number;
  carriedForward: number;
  settledManually: number;
  netAdjustment: number;
  lines: DeductionPreviewLine[];
  pending: DeductionPreviewLine[];
  earnings: number;
  netPay: number;
  earningLines: EarningLine[] | null;
}

/** A change-request profit share paid as a separate earning line. */
export interface EarningLine {
  awardId: number;
  changeRequestId: number;
  changeRequestTitle: string;
  hours: number;
  sharePercent: number;
  amount: number;
}

export interface DeductionDispute {
  id: number;
  reason: string;
  raisedAtUtc: string;
  outcome: DisputeOutcome | null;
  resolutionJustification: string | null;
  resolvedByEmployeeId: number | null;
  resolvedAtUtc: string | null;
  replacementDeductionId: number | null;
}

export interface Deduction {
  id: number;
  kind: DeductionKind;
  status: DeductionStatus;
  employeeId: number;
  employeeName: string;
  bugId: number;
  bugDescription: string;
  taskId: number;
  taskTitle: string;
  moduleId: number;
  moduleName: string;
  timeLogId: number;
  workDate: string;
  sourceMonth: string;
  applyMonth: string;
  routedFromLockedMonth: boolean;
  hours: number;
  hourlyRateUsed: number;
  sharePercent: number;
  amount: number;
  reversesDeductionId: number | null;
  amountSetByDispute: boolean;
  createdAtUtc: string;
  notifiedAtUtc: string | null;
  disputeWindowEndsAtUtc: string | null;
  disputeWindowOpen: boolean;
  acknowledgedAtUtc: string | null;
  approvedByEmployeeId: number | null;
  approvedAtUtc: string | null;
  payrollRunId: number | null;
  cancellationReason: string | null;
  note: string | null;
  dispute: DeductionDispute | null;
  version: number;
  /** The manager can approve the line now (the server applies the dispute-window setting). */
  approvableNow: boolean;
}

export interface ResolveDisputeInput {
  outcome: DisputeOutcome;
  justification: string;
  newHours: number | null;
  newSharePercent: number | null;
}

export interface BugDetail extends Bug {
  reportedByEmployeeId: number;
  rootCause: RootCause | null;
  causedByOriginalDelivery: boolean | null;
  reviewedByEmployeeId: number | null;
  reviewJustification: string | null;
  reviewedAtUtc: string | null;
  regressionOfBugId: number | null;
  version: number;
}

export interface ReviewBugInput {
  rootCause: RootCause;
  causedByOriginalDelivery: boolean;
  justification: string | null;
  regressionOfBugId: number | null;
}

export interface AttributionShare {
  employeeId: number;
  employeeName: string;
  basisHours: number;
  sharePercent: number;
}

export interface Attribution {
  bugId: number;
  taskId: number;
  isDeductible: boolean;
  source: 'Computed' | 'ManagerOverride';
  basis: 'RegularHours' | 'FixHoursOnEarlierBug';
  regressionOfBugId: number | null;
  shares: AttributionShare[];
  overrideJustification: string | null;
  overriddenByEmployeeId: number | null;
  overriddenAtUtc: string | null;
}

export interface OverrideAttributionInput {
  shares: { employeeId: number; sharePercent: number }[];
  justification: string;
}

export interface PayrollRunSummary {
  id: number;
  periodMonth: string;
  runVersion: number;
  status: PayrollRunStatus;
  inputsHash: string;
  capPercent: number;
  employeeCount: number;
  totalApplied: number;
  totalCarriedForward: number;
  totalRefunds: number;
  totalSettledManually: number;
  generatedByEmployeeId: number;
  generatedAtUtc: string;
  reviewedByEmployeeId: number | null;
  reviewedAtUtc: string | null;
  approvedByEmployeeId: number | null;
  approvedAtUtc: string | null;
  finalizedByEmployeeId: number | null;
  finalizedAtUtc: string | null;
  lockedByEmployeeId: number | null;
  lockedAtUtc: string | null;
  supersededAtUtc: string | null;
  version: number;
}

export interface PayrollRunEmployee {
  id: number;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  payMode: PayMode;
  salaryBasis: string;
  manualSettlement: boolean;
  salaryBase: number;
  capAmount: number;
  carriedIn: number;
  currentDue: number;
  refunds: number;
  cancelledByCompensation: number;
  applied: number;
  carriedForward: number;
  settledManually: number;
  netAdjustment: number;
}

export interface PayrollRunItem {
  deductionId: number;
  kind: PayrollItemKind;
  originalApplyMonth: string;
  workDate: string;
  bugId: number;
  taskId: number;
  taskTitle: string;
  due: number;
  applied: number;
  carriedForward: number;
  settledManually: number;
  cancelledByCompensation: number;
}

export interface ReconciliationCheck {
  name: string;
  passed: boolean;
  detail: string;
}

export interface PayrollRunDetail {
  run: PayrollRunSummary;
  employees: PayrollRunEmployee[];
  reconciliation: ReconciliationCheck[] | null;
}

export interface PayrollRunEmployeeDetail {
  employee: PayrollRunEmployee;
  items: PayrollRunItem[];
}

export interface PayrollPeriod {
  month: string;
  isLocked: boolean;
  finalizedRunId: number | null;
  lockedAtUtc: string | null;
}

export type PayrollStep = 'review' | 'approve' | 'finalize' | 'lock';

export interface PayslipSummary {
  id: number;
  employeeId: number;
  employeeName: string;
  periodMonth: string;
  payrollRunId: number;
  salaryBase: number;
  applied: number;
  refunds: number;
  netAdjustment: number;
  publishedAtUtc: string;
  earnings: number;
  netPay: number;
}

export interface PayslipDetail {
  payslip: PayslipSummary;
  items: PayrollRunItem[];
  earningLines: EarningLine[] | null;
}

export interface ReportRows<T> {
  rows: T[];
}

export interface ReworkSummaryRow {
  month: string;
  regularHours: number;
  correctiveHours: number;
  reworkPercent: number;
  bugsReported: number;
  bugsDeductible: number;
  deductionsApplied: number;
  refunds: number;
}

export interface ByEmployeeRow {
  employeeId: number;
  employeeName: string;
  correctiveHours: number;
  deductionsApplied: number;
  carriedForward: number;
  disputesRaised: number;
  disputesUpheld: number;
}

export interface ByModuleRow {
  projectName: string;
  moduleName: string;
  bugs: number;
  reopens: number;
  correctiveHours: number;
  deductionAmount: number;
}

export interface PayrollRegisterRow {
  employeeCode: string;
  employeeName: string;
  payMode: PayMode;
  salaryBase: number;
  capAmount: number;
  carriedIn: number;
  currentDue: number;
  refunds: number;
  applied: number;
  carriedForward: number;
  settledManually: number;
  netAdjustment: number;
}

export interface Policy {
  standardHoursPerMonth: number;
  dailyHours: number;
  deductionCapPercent: number;
  defaultWarrantyDays: number;
  disputeWindowDays: number;
  qaMissDeductible: boolean;
  qaMissSharePercent: number;
  requireEmployeeAcknowledgement: boolean;
}

export interface ProjectWarranty {
  projectId: number;
  projectName: string;
  warrantyDays: number;
}

export interface Holiday {
  id: number;
  date: string;
  name: string;
}

export interface EmployeeAdmin {
  id: number;
  code: string;
  name: string;
  email: string;
  role: Role;
  managerId: number | null;
  isActive: boolean;
  payMode: PayMode;
  monthlyGross: number | null;
  dailyRate: number | null;
  hourlyRate: number | null;
}

export interface RateHistory {
  id: number;
  effectiveFrom: string;
  payMode: PayMode;
  monthlyGross: number | null;
  dailyRate: number | null;
  hourlyRate: number | null;
  recordedByEmployeeId: number | null;
  recordedAtUtc: string | null;
}

export interface ChangePayInput {
  payMode: PayMode;
  monthlyGross: number | null;
  dailyRate: number | null;
  hourlyRate: number | null;
  effectiveFrom: string;
}

export interface CreateEmployeeInput extends Omit<ChangePayInput, 'effectiveFrom'> {
  payEffectiveFrom: string;
  code: string;
  name: string;
  email: string;
  role: Role;
  managerId: number | null;
  password: string;
}

export interface UpdateEmployeeInput {
  name: string;
  role: Role;
  managerId: number | null;
  isActive: boolean;
}

export interface ScheduledJob {
  id: string;
  cron: string;
  timeZone: string;
  nextRunUtc: string | null;
  lastRunUtc: string | null;
  lastState: string | null;
  error: string | null;
}

export interface OutboxMessage {
  id: number;
  notificationId: number;
  channel: NotificationChannel;
  status: OutboxStatus;
  attempts: number;
  recipientEmployeeId: number;
  type: NotificationType;
  createdAtUtc: string;
  nextAttemptAtUtc: string;
  processedAtUtc: string | null;
  lastError: string | null;
}

export interface AppNotification {
  id: number;
  type: NotificationType;
  title: string;
  body: string;
  link: string | null;
  entityType: string | null;
  entityId: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface NotificationPreference {
  type: NotificationType;
  emailEnabled: boolean;
  inAppEnabled: boolean;
  emailMandatory: boolean;
}

export interface AuditEvent {
  id: number;
  occurredAtUtc: string;
  actorEmployeeId: number | null;
  entityType: string;
  entityId: string;
  action: string;
  detailsJson: string;
}

export interface AuditVerifyResult {
  valid: boolean;
  checkedCount: number;
  firstBrokenId: number | null;
}

export interface ReportBugInput {
  severity: BugSeverity;
  description: string;
  foundDate: string;
}

export interface LogTimeInput {
  taskId: number;
  workDate: string;
  hours: number;
  workType: WorkType;
  bugId: number | null;
  note: string | null;
}

export interface ChangeRequest {
  id: number;
  projectId: number;
  projectName: string;
  moduleId: number;
  moduleName: string;
  title: string;
  customerName: string;
  clientReference: string;
  /** Null for roles that may not see revenue. */
  quotedAmountExTax: number | null;
  approvedHours: number;
  receivedAmountExTax: number | null;
  status: ChangeRequestStatus;
  createdAtUtc: string;
  approvedByEmployeeId: number | null;
  rejectionReason: string | null;
  taskId: number | null;
  version: number;
}

export interface CreateChangeRequestInput {
  projectId: number;
  moduleId: number;
  title: string;
  customerName: string;
  clientReference: string;
  quotedAmountExTax: number;
}

export interface ApproveChangeRequestInput {
  approvedHours: number;
  assigneeIds: number[];
  committedDate: string | null;
}

export type IncentiveAwardStatus = 'Pending' | 'Approved' | 'Rejected' | 'Paid';

/** An employee's profit-share award. Carries no revenue, profit or rates. */
export interface IncentiveAward {
  id: number;
  changeRequestId: number;
  changeRequestTitle: string;
  employeeId: number;
  employeeName: string;
  hours: number;
  sharePercent: number;
  amount: number;
  status: IncentiveAwardStatus;
  applyMonth: string;
  decidedByEmployeeId: number | null;
  rejectionReason: string | null;
  payrollRunId: number | null;
  version: number;
}
