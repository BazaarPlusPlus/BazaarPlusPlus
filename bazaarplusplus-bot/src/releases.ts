import {
  ANDROID_GAME_STATUS_URL,
  DOWNLOAD_PLATFORM_KEYS,
  GAME_STATUS_URL,
  RELEASE_BASE_URL,
  decodeAndroidDownload,
  decodeAndroidGameStatus,
  decodeDesktopGameRelease,
  decodeGameStatus,
  decodeMainlandDownloadUrl,
  platformManifestPath
} from '../../release/downloads';
import { json } from './data';

export async function releaseMessage(
  status: boolean,
  signal: AbortSignal
): Promise<string> {
  const platforms = ['windows', 'macos', 'android'] as const;
  const fetched = await Promise.allSettled(
    platforms.map((p) =>
      json<Record<string, unknown>>(
        `${RELEASE_BASE_URL}/${platformManifestPath(DOWNLOAD_PLATFORM_KEYS[p === 'macos' ? 'mac' : p])}`,
        signal
      )
    )
  );
  const online = status
    ? await Promise.allSettled([
        json(GAME_STATUS_URL, signal),
        json(ANDROID_GAME_STATUS_URL, signal)
      ])
    : [];
  const desktop =
    online[0]?.status === 'fulfilled'
      ? decodeGameStatus(online[0].value)
      : null;
  const android =
    online[1]?.status === 'fulfilled'
      ? decodeAndroidGameStatus(online[1].value)
      : null;
  return platforms
    .map((p, i) => {
      const label = { windows: 'Windows', macos: 'macOS', android: 'Android' }[
        p
      ];
      const entry = fetched[i];
      if (entry.status !== 'fulfilled') return `${label}：暂时无法获取版本`;
      const manifest = entry.value;
      const key = DOWNLOAD_PLATFORM_KEYS[p === 'macos' ? 'mac' : p];
      if (p === 'android') {
        const release = decodeAndroidDownload(manifest);
        if (!release) return `${label}：暂时无法获取版本`;
        const compatible = android
          ? release.game.il2cppSha256 === android.il2cppSha256 &&
            release.game.metadataSha256 === android.metadataSha256
          : null;
        return status
          ? `${label}：${compatible === null ? '游戏状态未知' : compatible ? '已适配' : '等待适配'}\n官网游戏 ${android?.gameVersion ?? '未知'} · BPP ${release.version}`
          : `${label} ${release.version}\n${release.app.url}${release.mainlandUrl ? `\n国内镜像 ${release.mainlandUrl}` : ''}`;
      }
      const game = decodeDesktopGameRelease(manifest, key);
      const current = desktop?.[p];
      if (status)
        return `${label}：${!game || !current ? '游戏状态未知' : game.managedSha256 === current.managedSha256 ? '已适配' : '等待适配'}\n游戏 ${current?.gameVersion ?? '未知'} · BPP ${typeof manifest.version === 'string' ? manifest.version : '未知'}`;
      const downloads = manifest.downloads as Record<string, { url: string }>;
      const mirror = decodeMainlandDownloadUrl(manifest, key);
      return `${label} ${typeof manifest.version === 'string' ? manifest.version : '未知'}\n${downloads?.[key]?.url ?? '暂无下载'}${mirror ? `\n国内镜像 ${mirror}` : ''}`;
    })
    .join('\n\n');
}
