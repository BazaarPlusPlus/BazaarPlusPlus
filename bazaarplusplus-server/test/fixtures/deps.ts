import type { HandlerDeps } from "../../src/http/deps";
import type { BundleDownloadSigner } from "../../src/presigner";
import { RecordingBundleDownloadSigner } from "./presigner";

interface TestDepsOptions {
  signer?: BundleDownloadSigner;
  now?: () => number;
}

export function createTestDeps(options: TestDepsOptions = {}): HandlerDeps {
  return {
    signer: options.signer ?? new RecordingBundleDownloadSigner(),
    now: options.now ?? (() => Date.now()),
  };
}
