#!/usr/bin/env node
// End-to-end runner: throwaway MySQL 8 container + API (Development, migrations + demo seed) + built frontend
// served by `vite preview` (with the /api and /hubs proxy), then Playwright, then teardown.
// Every credential is generated per run and lives only in this process's environment.
import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const frontend = resolve(root, 'frontend');
const apiProject = resolve(root, 'backend/src/ReworkGuard.Api');
const mysqlImage = process.env.E2E_MYSQL_IMAGE ?? 'mysql:8.0';
const container = `reworkguard-e2e-mysql-${process.pid}`;
const children = [];
let tornDown = false;

const log = (msg) => console.log(`[e2e] ${msg}`);
const secret = (bytes = 24) => randomBytes(bytes).toString('base64url');

function run(cmd, args, opts = {}) {
  const r = spawnSync(cmd, args, { stdio: opts.quiet ? 'pipe' : 'inherit', encoding: 'utf8', ...opts });
  if (r.status !== 0 && !opts.allowFail) {
    throw new Error(`${cmd} ${args.join(' ')} exited with ${r.status}\n${r.stderr ?? ''}`);
  }
  return r;
}

function start(name, cmd, args, opts) {
  const child = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], detached: true, ...opts });
  const verbose = process.env.E2E_VERBOSE === '1';
  const tail = [];
  const keep = (chunk) => {
    for (const line of chunk.toString().split('\n')) {
      if (!line) continue;
      tail.push(line);
      if (tail.length > 80) tail.shift();
      if (verbose) console.log(`[${name}] ${line}`);
    }
  };
  child.stdout.on('data', keep);
  child.stderr.on('data', keep);
  child.on('exit', (code) => {
    if (!tornDown && code !== null && code !== 0) {
      console.error(`[e2e] ${name} exited with ${code}; last output:\n${tail.join('\n')}`);
    }
  });
  children.push({ name, child, tail });
  return child;
}

function freePort() {
  return new Promise((ok, fail) => {
    const srv = createServer();
    srv.unref();
    srv.on('error', fail);
    srv.listen(0, '127.0.0.1', () => {
      const { port } = srv.address();
      srv.close(() => ok(port));
    });
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function waitFor(label, check, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if (await check()) {
        log(`${label} is ready`);
        return;
      }
    } catch {
      // not ready yet
    }
    await sleep(1000);
  }
  const proc = children.find((c) => label.toLowerCase().includes(c.name));
  throw new Error(`${label} did not become ready within ${timeoutMs / 1000}s${proc ? `\n${proc.tail.join('\n')}` : ''}`);
}

function teardown() {
  if (tornDown) return;
  tornDown = true;
  for (const { child } of children.reverse()) {
    try {
      process.kill(-child.pid, 'SIGTERM');
    } catch {
      // already gone
    }
  }
  if (process.env.E2E_KEEP_DB !== '1') {
    spawnSync('docker', ['rm', '-f', '-v', container], { stdio: 'ignore' });
  }
  log('torn down');
}

process.on('SIGINT', () => {
  teardown();
  process.exit(130);
});
process.on('SIGTERM', () => {
  teardown();
  process.exit(143);
});

async function main() {
  const started = Date.now();
  const mysqlPassword = secret();
  const seedPassword = `E2e-${secret(12)}`;
  const apiPort = Number(process.env.E2E_API_PORT ?? (await freePort()));
  const webPort = Number(process.env.E2E_WEB_PORT ?? (await freePort()));

  log(`starting ${mysqlImage} as ${container}`);
  run('docker', [
    'run', '-d', '--rm', '--name', container,
    '-e', `MYSQL_ROOT_PASSWORD=${mysqlPassword}`, '-e', 'MYSQL_DATABASE=reworkguard',
    '-p', '127.0.0.1::3306', mysqlImage, '--log-bin-trust-function-creators=1',
  ], { quiet: true });
  const mapped = run('docker', ['port', container, '3306/tcp'], { quiet: true }).stdout.trim().split('\n')[0];
  const dbPort = Number(mapped.split(':').pop());

  // Build both sides while MySQL initialises.
  if (!existsSync(resolve(frontend, 'node_modules'))) {
    log('installing frontend dependencies');
    run('npm', ['ci', '--no-audit', '--no-fund'], { cwd: frontend });
  }
  if (process.env.E2E_SKIP_BUILD !== '1') {
    log('building frontend');
    run('npm', ['run', 'build'], { cwd: frontend, quiet: true });
    log('building API');
    run('dotnet', ['build', apiProject, '-c', 'Debug', '-v', 'q', '-nologo'], { quiet: true });
  }

  // TCP on 127.0.0.1 only answers once the post-initialisation server is up.
  await waitFor('MySQL', () => {
    const r = spawnSync('docker', ['exec', container, 'mysql', '-h127.0.0.1', '-uroot', `-p${mysqlPassword}`, '-e', 'SELECT 1'], { stdio: 'ignore' });
    return r.status === 0;
  }, 120_000);

  const apiUrl = `http://127.0.0.1:${apiPort}`;
  start('api', 'dotnet', ['run', '--no-build', '--no-launch-profile', '--project', apiProject], {
    cwd: root,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: apiUrl,
      ConnectionStrings__Default: `Server=127.0.0.1;Port=${dbPort};Database=reworkguard;User=root;Password=${mysqlPassword};`,
      Jwt__SigningKey: secret(48),
      Database__MigrateOnStartup: 'true',
      Database__SeedOnStartup: 'true',
      Database__SeedPassword: seedPassword,
      Email__Provider: 'Log',
      Cors__AllowedOrigins__0: `http://127.0.0.1:${webPort}`,
    },
  });
  await waitFor('API', async () => (await fetch(`${apiUrl}/health`)).ok, 180_000);

  const webUrl = `http://127.0.0.1:${webPort}`;
  start('web', 'npx', ['vite', 'preview', '--host', '127.0.0.1', '--port', String(webPort), '--strictPort'], {
    cwd: frontend,
    env: { ...process.env, VITE_API_PROXY_TARGET: apiUrl },
  });
  await waitFor('web', async () => (await fetch(`${webUrl}/login`)).ok, 60_000);

  log(`stack ready in ${Math.round((Date.now() - started) / 1000)}s; running Playwright`);
  const pw = spawnSync('npx', ['playwright', 'test', ...process.argv.slice(2)], {
    cwd: here,
    stdio: 'inherit',
    env: { ...process.env, E2E_BASE_URL: webUrl, E2E_API_URL: apiUrl, E2E_PASSWORD: seedPassword },
  });
  log(`finished in ${Math.round((Date.now() - started) / 1000)}s`);
  return pw.status ?? 1;
}

main()
  .then((code) => {
    teardown();
    process.exit(code);
  })
  .catch((error) => {
    console.error(error);
    teardown();
    process.exit(1);
  });
