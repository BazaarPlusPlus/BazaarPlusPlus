import type { Env } from "../env";
import { type BundleDownloadSigner, createBundleDownloadSigner } from "../presigner";

export interface HandlerDeps {
  readonly signer: BundleDownloadSigner;
  now(): number;
}

export function createHandlerDeps(env: Env): HandlerDeps {
  let cached: BundleDownloadSigner | undefined;
  return {
    get signer() {
      cached ??= createBundleDownloadSigner(env);
      return cached;
    },
    now: () => Date.now(),
  };
}
