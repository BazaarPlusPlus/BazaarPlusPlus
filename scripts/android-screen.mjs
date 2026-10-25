import fs from 'node:fs';
import path from 'node:path';

export function inspectScreen(directory, serial) {
  const read = (file) =>
    fs.readFileSync(path.join(directory, file), 'utf8').trim();
  const png = fs.readFileSync(path.join(directory, 'screen.png'));
  if (
    png.length < 24 ||
    png.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a' ||
    png.toString('ascii', 12, 16) !== 'IHDR'
  ) {
    throw new Error('screen.png is not a PNG screenshot');
  }
  const width = png.readUInt32BE(16),
    height = png.readUInt32BE(20);
  const size = read('wm-size.txt'),
    density = read('wm-density.txt');
  const screens = [
    ...read('logcat.txt').matchAll(/screen=(\d+)x(\d+)@([\d.]+)\|([\d.,-]+)/g)
  ];
  const last = screens.at(-1);
  const unity = last
    ? {
        width: Number(last[1]),
        height: Number(last[2]),
        dpi: Number(last[3]),
        safeArea: last[4].split(',').map(Number)
      }
    : null;
  const problems = [];
  if (width <= height)
    problems.push(
      'Screenshot is not landscape. Use the landscape baseline AVD.'
    );
  if (/Override/i.test(size + '\n' + density))
    problems.push(
      'Window size or density is overridden. Restore the AVD baseline before UI validation.'
    );
  if (
    !/Physical size: \d+x\d+/.test(size) ||
    !/Physical density: \d+/.test(density)
  )
    problems.push('Android display metrics are unavailable.');
  if (unity && (unity.width !== width || unity.height !== height))
    problems.push(
      'The last logged Unity screen differs from the screenshot. Reopen the panel and capture again after the display settles.'
    );
  return {
    capturedAt: new Date().toISOString(),
    serial,
    screenshot: { width, height },
    size,
    density,
    userRotation: read('user-rotation.txt'),
    accelerometerRotation: read('accelerometer-rotation.txt'),
    unity,
    problems
  };
}

if (import.meta.main) {
  try {
    const [directory, serial, mode] = process.argv.slice(2);
    const result = inspectScreen(directory, serial);
    fs.writeFileSync(
      path.join(directory, 'environment.json'),
      JSON.stringify(result, null, 2) + '\n'
    );
    if (mode === '--check' && result.problems.length) {
      console.error(result.problems.join('\n'));
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
