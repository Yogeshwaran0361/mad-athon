import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { ToastProvider } from './Toast';
import { useToast } from './toastContext';

function Trigger() {
  const { notify } = useToast();
  return (
    <button
      type="button"
      onClick={() => {
        notify('Line #3 approved.');
      }}
    >
      Go
    </button>
  );
}

describe('ToastProvider', () => {
  it('announces messages through a polite live region', async () => {
    render(
      <ToastProvider>
        <Trigger />
      </ToastProvider>,
    );
    const region = screen.getByRole('status');
    expect(region).toHaveAttribute('aria-live', 'polite');
    await userEvent.click(screen.getByRole('button', { name: 'Go' }));
    expect(region).toHaveTextContent('Line #3 approved.');
  });
});
