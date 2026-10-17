import type { Role } from '../api/types';

export interface NavItem {
  to: string;
  label: string;
  /** Roles that see the entry; undefined means everyone signed in. */
  roles?: Role[];
  group: 'Work' | 'Pay' | 'Insights' | 'Admin';
}

export const NAV_ITEMS: NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', group: 'Work', roles: ['TeamLead', 'Manager', 'Finance', 'Admin'] },
  { to: '/tasks', label: 'Tasks', group: 'Work' },
  { to: '/board', label: 'Board', group: 'Work' },
  { to: '/approvals', label: 'Time approvals', group: 'Work', roles: ['TeamLead', 'Manager'] },
  { to: '/change-requests', label: 'Feature requests', group: 'Work', roles: ['QA', 'TeamLead', 'Manager', 'Finance', 'Admin'] },
  { to: '/bug-review', label: 'Bug review', group: 'Work', roles: ['Manager'] },
  { to: '/my-deductions', label: 'My deductions', group: 'Pay' },
  { to: '/deductions', label: 'Deduction preview', group: 'Pay' },
  { to: '/awards', label: 'Profit-share awards', group: 'Pay' },
  { to: '/deduction-queue', label: 'Deduction queue', group: 'Pay', roles: ['Manager'] },
  { to: '/payroll', label: 'Payroll', group: 'Pay', roles: ['Manager', 'Finance', 'Admin'] },
  { to: '/payslips', label: 'Payslips', group: 'Pay' },
  { to: '/reports', label: 'Reports', group: 'Insights', roles: ['TeamLead', 'Manager', 'Finance', 'Admin'] },
  { to: '/audit', label: 'Audit trail', group: 'Insights', roles: ['Manager', 'Admin'] },
  { to: '/admin', label: 'Settings', group: 'Admin', roles: ['Manager', 'Finance', 'Admin'] },
  { to: '/admin/employees', label: 'Employees & pay', group: 'Admin', roles: ['Finance', 'Admin'] },
  { to: '/admin/operations', label: 'Jobs & outbox', group: 'Admin', roles: ['Admin'] },
];

export function navFor(role: Role | undefined): NavItem[] {
  if (!role) {
    return [];
  }
  return NAV_ITEMS.filter((item) => !item.roles || item.roles.includes(role));
}
