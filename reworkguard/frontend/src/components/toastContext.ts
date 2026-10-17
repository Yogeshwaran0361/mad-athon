import { createContext, useContext } from 'react';
import type { AlertColor } from '@mui/material';

export interface ToastApi {
  notify: (text: string, severity?: AlertColor) => void;
}

export const ToastContext = createContext<ToastApi>({ notify: () => undefined });

export function useToast(): ToastApi {
  return useContext(ToastContext);
}
