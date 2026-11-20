import { launch, type Browser } from '@cloudflare/puppeteer';

/** Started as soon as a command will reply with an image, so the launch overlaps the data work. */
export const openBrowser = (binding: Fetcher): Promise<Browser> =>
  launch(binding);

export async function render(
  html: string,
  browser: Promise<Browser>,
  mark: (step: string) => void = () => {}
): Promise<Uint8Array> {
  const page = await (await browser).newPage();
  page.setDefaultTimeout(20_000);
  await page.setViewport({ width: 720, height: 100, deviceScaleFactor: 2 });
  await page.setJavaScriptEnabled(false);
  await page.setContent(html, { waitUntil: 'load', timeout: 20_000 });
  await page.evaluate(async () => {
    await document.fonts.ready;
  });
  mark('content');
  // JPEG keeps the QQ upload under 1 MB, several times smaller than PNG.
  const main = await page.$('main');
  if (!main) throw new Error('The report has no main element');
  return new Uint8Array(await main.screenshot({ type: 'jpeg', quality: 85 }));
}
