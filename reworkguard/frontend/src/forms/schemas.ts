import { z } from 'zod';
import { BUG_SEVERITIES } from '../api/types';
import { todayUtc } from './dates';

const isoDate = z
  .string()
  .regex(/^\d{4}-\d{2}-\d{2}$/, 'Pick a date')
  .refine((value) => value <= todayUtc(), 'The date cannot be in the future');

export const loginSchema = z.object({
  email: z.string().trim().email('Enter a valid email address'),
  password: z.string().min(1, 'Enter your password'),
});
export type LoginValues = z.infer<typeof loginSchema>;

/** Any found date is accepted as long as it is not before the task was completed (yyyy-MM-dd, when known). */
export function bugReportSchema(completedOn?: string | null) {
  return z.object({
    severity: z.enum(BUG_SEVERITIES as [string, ...string[]], { message: 'Choose a severity' }),
    description: z
      .string()
      .trim()
      .min(5, 'Describe the defect in at least 5 characters')
      .max(4000, 'Keep the description under 4000 characters'),
    foundDate: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/, 'Pick a date')
      .refine((value) => !completedOn || value >= completedOn, `The date cannot be before the task was completed (${completedOn ?? ''})`),
  });
}
export type BugReportValues = z.infer<ReturnType<typeof bugReportSchema>>;

export const timeLogSchema = z
  .object({
    workType: z.enum(['Regular', 'Corrective', 'Enhancement']),
    bugId: z.string(),
    workDate: isoDate,
    hours: z
      .string()
      .trim()
      .min(1, 'Enter the hours worked')
      .refine((v) => /^\d+(\.\d{1,2})?$/.test(v), 'Use a number with at most two decimals')
      .refine((v) => Number(v) > 0, 'Hours must be more than 0')
      .refine((v) => Number(v) <= 24, 'At most 24 hours in a day'),
    note: z.string().max(1000, 'Keep the note under 1000 characters'),
  })
  .superRefine((value, ctx) => {
    if (value.workType === 'Corrective' && value.bugId === '') {
      ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['bugId'], message: 'Pick the bug you are fixing' });
    }
  });
export type TimeLogValues = z.infer<typeof timeLogSchema>;
