import { chmod, mkdtemp, rm, stat } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(webRoot, '../../..');
const stateDirectory = await mkdtemp(join(tmpdir(), 'iiot-cloud-web-e2e-'));
await chmod(stateDirectory, 0o700);
const statePath = resolve(stateDirectory, 'state.json');

const waitForExit = (child, name) => new Promise((resolveExit, rejectExit) => {
  child.once('error', (error) => rejectExit(new Error(`${name} failed to start.`, { cause: error })));
  child.once('exit', (code, signal) => resolveExit({ code, signal }));
});

let host;
let hostExit;
let testExitCode = 1;
try {
  host = spawn('dotnet', [
    'run',
    '--project', resolve(repoRoot, 'src/testing/IIoT.CloudPlatform.WebE2ETestKit/IIoT.CloudPlatform.WebE2ETestKit.csproj'),
    '--configuration', 'Release',
    '--', statePath,
  ], { cwd: repoRoot, stdio: ['pipe', 'inherit', 'inherit'] });
  hostExit = waitForExit(host, 'Cloud Web E2E host');

  const deadline = Date.now() + 4 * 60_000;
  let ready = false;
  while (Date.now() < deadline) {
    const status = await Promise.race([
      hostExit.then((result) => ({ exited: true, result })),
      new Promise((resolveWait) => setTimeout(() => resolveWait({ exited: false }), 250)),
    ]);
    if (status.exited) {
      throw new Error(`Cloud Web E2E host exited before readiness: ${JSON.stringify(status.result)}`);
    }
    try {
      await stat(statePath);
      ready = true;
      break;
    } catch (error) {
      if (error?.code !== 'ENOENT') throw error;
    }
  }
  if (!ready) {
    throw new Error('Cloud Web E2E host did not become ready within four minutes.');
  }

  const playwright = spawn(
    process.platform === 'win32' ? 'npx.cmd' : 'npx',
    ['playwright', 'test', '--config', 'playwright.e2e.config.ts', '--reporter=json'],
    {
      cwd: webRoot,
      stdio: 'inherit',
      env: { ...process.env, CLOUD_WEB_E2E_STATE: statePath },
    },
  );
  const playwrightExit = await waitForExit(playwright, 'Playwright real E2E');
  testExitCode = playwrightExit.code ?? 1;
} finally {
  if (host) {
    if (host.exitCode === null && host.signalCode === null) {
      host.stdin.end();
      const shutdown = await Promise.race([
        hostExit.catch(() => null),
        new Promise((resolveWait) => setTimeout(() => resolveWait(null), 120_000)),
      ]);
      if (shutdown === null && host.exitCode === null && host.signalCode === null) {
        host.kill('SIGTERM');
        const terminated = await Promise.race([
          hostExit.catch(() => null),
          new Promise((resolveWait) => setTimeout(() => resolveWait(null), 10_000)),
        ]);
        if (terminated === null && host.exitCode === null && host.signalCode === null) {
          if (!host.kill('SIGKILL')) {
            throw new Error('Cloud Web E2E host rejected the final SIGKILL cleanup.');
          }
          const killed = await Promise.race([
            hostExit.catch(() => null),
            new Promise((resolveWait) => setTimeout(() => resolveWait(null), 10_000)),
          ]);
          if (killed === null && host.exitCode === null && host.signalCode === null) {
            throw new Error('Cloud Web E2E host remained alive after SIGKILL.');
          }
        }
      }
    }
  }
  // The state contains a one-run seed password; the private OS temp directory is removed as one unit.
  await rm(stateDirectory, { recursive: true, force: true });
}

process.exitCode = testExitCode;
