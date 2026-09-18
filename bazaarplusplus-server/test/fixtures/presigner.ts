import type { BundleDownloadSigner, SignedBundleDownload } from "../../src/presigner";

export class RecordingBundleDownloadSigner implements BundleDownloadSigner {
  readonly calls: Array<{ objectKey: string; issuedAtMs: number }> = [];

  async sign(objectKey: string, issuedAtMs: number): Promise<SignedBundleDownload> {
    this.calls.push({ objectKey, issuedAtMs });
    return {
      url: `https://fake.invalid/${encodeURIComponent(objectKey)}?method=GET&expires=604800`,
      expiresAtMs: issuedAtMs + 604_800_000,
    };
  }
}

export class RejectingBundleDownloadSigner implements BundleDownloadSigner {
  readonly calls: Array<{ objectKey: string; issuedAtMs: number }> = [];

  async sign(objectKey: string, issuedAtMs: number): Promise<SignedBundleDownload> {
    this.calls.push({ objectKey, issuedAtMs });
    throw new Error("injected signing failure");
  }
}
