import fs from 'node:fs';
import path from 'node:path';

export async function withBuildLock<T>(
  cwd: string,
  action: (env: NodeJS.ProcessEnv) => T | PromiseLike<T>
): Promise<T> {
  const directory = path.resolve(
    cwd,
    'bazaarplusplus-mod/artifacts/build.lock'
  );
  if (
    process.env.BPP_MOD_BUILD_LOCK &&
    path.resolve(process.env.BPP_MOD_BUILD_LOCK) === directory
  ) {
    return action(process.env);
  }
  fs.mkdirSync(path.dirname(directory), { recursive: true });
  try {
    fs.mkdirSync(directory);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error;
    throw new Error(
      `Mod build is already locked: ${directory}. Wait for the other command; after an interrupted run, confirm its processes have stopped before removing this directory.`
    );
  }
  try {
    fs.writeFileSync(
      path.join(directory, 'owner.json'),
      JSON.stringify({ pid: process.pid, command: process.argv })
    );
    return await action({
      ...process.env,
      BPP_MOD_BUILD_LOCK: directory.split(path.sep).join('/')
    });
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
}
