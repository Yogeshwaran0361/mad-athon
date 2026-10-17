import { Navigate, Route, Routes } from 'react-router-dom';
import { RequireAuth } from './auth/RequireAuth';
import { useAuth } from './auth/AuthContext';
import { Layout } from './components/Layout';
import { EmployeesAdminPage } from './pages/admin/EmployeesAdminPage';
import { OperationsPage } from './pages/admin/OperationsPage';
import { SettingsPage } from './pages/admin/SettingsPage';
import { ApprovalQueuePage } from './pages/ApprovalQueuePage';
import { AuditPage } from './pages/AuditPage';
import { BoardPage } from './pages/BoardPage';
import { AwardsPage } from './pages/AwardsPage';
import { BugReviewPage } from './pages/BugReviewPage';
import { ChangeRequestsPage } from './pages/ChangeRequestsPage';
import { DashboardPage } from './pages/DashboardPage';
import { DeductionPreviewPage } from './pages/DeductionPreviewPage';
import { DeductionQueuePage } from './pages/DeductionQueuePage';
import { LoginPage } from './pages/LoginPage';
import { MyDeductionsPage } from './pages/MyDeductionsPage';
import { NotificationsPage } from './pages/NotificationsPage';
import { PayrollPage } from './pages/PayrollPage';
import { PayrollRunEmployeePage } from './pages/PayrollRunEmployeePage';
import { PayrollRunPage } from './pages/PayrollRunPage';
import { PayslipsPage } from './pages/PayslipsPage';
import { ReportsPage } from './pages/ReportsPage';
import { TaskDetailPage } from './pages/TaskDetailPage';
import { TaskListPage } from './pages/TaskListPage';

function Home() {
  const { hasRole } = useAuth();
  return <Navigate to={hasRole('TeamLead', 'Manager', 'Finance', 'Admin') ? '/dashboard' : '/board'} replace />;
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route path="/" element={<Home />} />
          <Route path="/tasks" element={<TaskListPage />} />
          <Route path="/tasks/:id" element={<TaskDetailPage />} />
          <Route path="/board" element={<BoardPage />} />
          <Route path="/deductions" element={<DeductionPreviewPage />} />
          <Route path="/my-deductions" element={<MyDeductionsPage />} />
          <Route path="/payslips" element={<PayslipsPage />} />
          <Route path="/awards" element={<AwardsPage />} />
          <Route path="/notifications" element={<NotificationsPage />} />
          <Route element={<RequireAuth roles={['TeamLead', 'Manager']} />}>
            <Route path="/approvals" element={<ApprovalQueuePage />} />
          </Route>
          <Route element={<RequireAuth roles={['QA', 'TeamLead', 'Manager', 'Finance', 'Admin']} />}>
            <Route path="/change-requests" element={<ChangeRequestsPage />} />
          </Route>
          <Route element={<RequireAuth roles={['Manager']} />}>
            <Route path="/bug-review" element={<BugReviewPage />} />
            <Route path="/deduction-queue" element={<DeductionQueuePage />} />
          </Route>
          <Route element={<RequireAuth roles={['Manager', 'Finance', 'Admin']} />}>
            <Route path="/payroll" element={<PayrollPage />} />
            <Route path="/payroll/runs/:id" element={<PayrollRunPage />} />
            <Route path="/payroll/runs/:id/employees/:employeeId" element={<PayrollRunEmployeePage />} />
            <Route path="/admin" element={<SettingsPage />} />
          </Route>
          <Route element={<RequireAuth roles={['TeamLead', 'Manager', 'Finance', 'Admin']} />}>
            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/reports" element={<ReportsPage />} />
          </Route>
          <Route element={<RequireAuth roles={['Manager', 'Admin']} />}>
            <Route path="/audit" element={<AuditPage />} />
          </Route>
          <Route element={<RequireAuth roles={['Finance', 'Admin']} />}>
            <Route path="/admin/employees" element={<EmployeesAdminPage />} />
          </Route>
          <Route element={<RequireAuth roles={['Admin']} />}>
            <Route path="/admin/operations" element={<OperationsPage />} />
          </Route>
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
