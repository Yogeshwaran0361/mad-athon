import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Alert, Box, Button, Paper, Stack, TextField, Typography } from '@mui/material';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { loginSchema, type LoginValues } from '../forms/schemas';

export function LoginPage() {
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [error, setError] = useState('');
  const { control, handleSubmit, formState } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  });

  const from = (location.state as { from?: string } | null)?.from ?? '/';
  if (user) {
    return <Navigate to={from} replace />;
  }

  const submit = handleSubmit(async ({ email, password }) => {
    setError('');
    try {
      await login(email, password);
      void navigate(from, { replace: true });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Sign-in failed.');
    }
  });

  return (
    <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '100vh', p: 2 }}>
      <Paper sx={{ p: 4, width: '100%', maxWidth: 400 }}>
        <Typography variant="h5" gutterBottom>
          Sign in to ReworkGuard
        </Typography>
        <Stack component="form" spacing={2} noValidate onSubmit={(e) => void submit(e)}>
          {error && <Alert severity="error">{error}</Alert>}
          <Controller
            name="email"
            control={control}
            render={({ field, fieldState }) => (
              <TextField {...field} label="Email" type="email" autoComplete="username" error={!!fieldState.error} helperText={fieldState.error?.message} />
            )}
          />
          <Controller
            name="password"
            control={control}
            render={({ field, fieldState }) => (
              <TextField
                {...field}
                label="Password"
                type="password"
                autoComplete="current-password"
                error={!!fieldState.error}
                helperText={fieldState.error?.message}
              />
            )}
          />
          <Button type="submit" variant="contained" disabled={formState.isSubmitting}>
            Sign in
          </Button>
        </Stack>
      </Paper>
    </Box>
  );
}
