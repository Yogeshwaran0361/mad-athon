import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError } from '../api/client';

/**
 * Copies field-level validation errors from an API problem onto the form.
 * Returns the message to show above the form for errors that are not tied to a field (409, 403...).
 */
export function applyServerErrors<T extends FieldValues>(error: unknown, fields: readonly Path<T>[], setError: UseFormSetError<T>): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : 'Something went wrong.';
  }

  let mapped = false;
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const field = fields.find((f) => f.toLowerCase() === key.toLowerCase());
    if (field && messages[0]) {
      setError(field, { type: 'server', message: messages[0] });
      mapped = true;
    }
  }
  return mapped && error.status === 400 ? '' : error.message;
}
