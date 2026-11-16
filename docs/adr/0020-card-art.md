# 从 Steam depot 提取卡图，公开维护一套

状态：已接受（2026-11-16）

## 背景

QQ 机器人的战绩图要画出玩家的最终阵容。只写卡名不直观，玩家认卡靠卡面。Bundle 的结算截图有卡面，但只有约 34% 的 Bundle 带截图，而且截图只有最后一局。游戏更新会加卡、改卡面，所以卡图要跟着游戏自动更新。

用户决定：我们自己维护一套卡图，放在公开桶里。

## 决定

- 来源：
  - 卡面和技能图标来自 Steam `online` 分支 Windows depot 的 `StreamingAssets/aa`：Addressables 目录 `catalog.bin`，以及 `card_*_assets_all.bundle`（不含 `card_vfx`）和 `skills_assets_all.bundle`，约 1.5 GB。
  - `bazaarplusplus-mod/tools/SteamSnapshot` 的 `card-art` 命令只下载这些文件。depot 和分支仍由 `STEAM_DEPOTS` 与 `STEAM_BRANCHES` 决定。
  - TemplateId 到 ArtKey 的对应来自游戏自己下载的 GameData（`https://data.playthebazaar.com/static/GameData.db.zip`）。depot 里的 GameData 比它旧几个月，少约 300 个物品。
- 提取：
  - `bazaarplusplus-bot/tools/card-art/extract.py` 按游戏自己的方式解析：ArtKey 经目录得到资源路径。物品走 CardData → 材质 → `_MainTex`，技能是图标 Sprite。
  - 依赖由 `extract.py.lock` 锁定。
  - 2026-11-16 的覆盖率：物品 1,428/1,432，技能 568/577。物品低于 95% 或技能低于 90% 时不发布。
- 发布：
  - 公开桶 `bazaarplusplus-public` 的 `card-art/` 前缀，由 `release/card-art.ts` 写入。
  - 文件名是内容摘要，只增不删。上一版 index 中已移除的卡保留原条目，这样游戏更新前的对局仍有卡图。
  - `index.json` 最后写入。
- 更新：
  - `.github/workflows/card-art.yml` 每 6 小时比较 Steam buildid 和 GameData 的 ETag 与已发布的 index，有变化才下载和发布。
  - 失败只影响这个工作流，下次运行重试，不影响 Steam 快照工作流。

## 否决的方案

- 只用 Bundle 截图：覆盖 34%，只有最后一局，热门阵容和英雄报告用不上。
- 在游戏里用 mod 导出卡图：需要运行游戏，不能无人值守更新。
- 引用第三方站点的卡图：依赖外部服务的地址和可用性。
- 挂在 `steam-snapshot-channel.yml` 后面运行：提取失败时每小时重下 1.5 GB，还会让快照工作流变红，掩盖登录失效等问题。

## 代价

- 公开桶第一次放入游戏美术素材，这是 [ADR 0007](0007-one-public-bucket-and-domain.md) "游戏本体文件不能进公开桶"之外的决定。合作授权（[ADR 0012](0012-android-app.md)）只覆盖 interop，不覆盖卡图。卡图的分发由本决定负责。
- 游戏改变 Addressables 目录格式或资源结构时，覆盖率检查会使发布失败，需要人工更新提取脚本。
- `catalog.bin` 的解析是自己写的（Addressables v2 二进制目录），Unity 升级可能改变格式。
