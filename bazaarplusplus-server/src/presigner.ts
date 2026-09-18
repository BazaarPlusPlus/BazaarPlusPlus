import { AwsClient } from "aws4fetch";
import { validObjectKey } from "./bundle/manifest";
import type { Env } from "./env";
import { HttpError } from "./errors";
import { PRESIGNED_GET_TTL_SECONDS } from "./limits";

export interface SignedBundleDownload {
  url: string;
  expiresAtMs: number;
}

export interface BundleDownloadSigner {
  sign(objectKey: string, issuedAtMs: number): Promise<SignedBundleDownload>;
}

export async function signDownloadPage(
  signer: BundleDownloadSigner,
  objectKeys: readonly string[],
  issuedAtMs: number,
  failureMessage: string,
): Promise<SignedBundleDownload[]> {
  const signedByKey = new Map<string, Promise<SignedBundleDownload>>();
  try {
    // Complete one real signature before fan-out so aws4fetch's date-scoped
    // signing-key cache is populated instead of derived once per distinct key.
    const firstKey = objectKeys[0];
    if (firstKey !== undefined) {
      signedByKey.set(firstKey, Promise.resolve(await signer.sign(firstKey, issuedAtMs)));
    }
    return await Promise.all(
      objectKeys.map((objectKey) => {
        let signed = signedByKey.get(objectKey);
        if (signed === undefined) {
          signed = signer.sign(objectKey, issuedAtMs);
          signedByKey.set(objectKey, signed);
        }
        return signed;
      }),
    );
  } catch {
    throw new HttpError(503, "storage_unavailable", failureMessage, true);
  }
}

function sigV4Date(timestamp: number): string {
  return new Date(timestamp).toISOString().replace(/[:-]|\.\d{3}/g, "");
}

export function createBundleDownloadSigner(env: Env): BundleDownloadSigner {
  if (
    env.BUNDLE_BUCKET_NAME !== "bazaarplusplus-bundle-v5" ||
    env.R2_ACCOUNT_ID.length === 0 ||
    env.R2_PRESIGN_ACCESS_KEY_ID.length === 0 ||
    env.R2_PRESIGN_SECRET_ACCESS_KEY.length === 0
  ) {
    throw new Error("R2 presigner configuration is invalid");
  }
  const client = new AwsClient({
    accessKeyId: env.R2_PRESIGN_ACCESS_KEY_ID,
    secretAccessKey: env.R2_PRESIGN_SECRET_ACCESS_KEY,
    service: "s3",
    region: "auto",
    retries: 0,
  });

  return {
    async sign(objectKey, issuedAtMs) {
      if (!validObjectKey(objectKey) || !Number.isSafeInteger(issuedAtMs) || issuedAtMs < 0) {
        throw new Error("R2 object key or signing time is invalid");
      }
      const url = new URL(
        `https://${env.BUNDLE_BUCKET_NAME}.${env.R2_ACCOUNT_ID}.r2.cloudflarestorage.com/${objectKey}`,
      );
      url.searchParams.set("X-Amz-Expires", String(PRESIGNED_GET_TTL_SECONDS));
      const request = await client.sign(url, {
        method: "GET",
        aws: {
          signQuery: true,
          datetime: sigV4Date(issuedAtMs),
          service: "s3",
          region: "auto",
        },
      });
      return {
        url: request.url,
        expiresAtMs: issuedAtMs + PRESIGNED_GET_TTL_SECONDS * 1_000,
      };
    },
  };
}
