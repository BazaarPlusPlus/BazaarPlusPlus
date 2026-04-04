# BazaarPlusPlus

[English](README_en.md)

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 模组项目，提供战斗 UI 增强、怪物与 tooltip 预览、历史面板与战斗回放等功能。

BazaarPlusPlus 的主要开发工作由 Codex 主导，并由 Claude Code 协作完成；作者仅负责提出需求并进行监督。

官网：[BazaarPlusPlus.com](https://bazaarplusplus.com)

## 功能概览

- 战斗 UI 增强：补充战斗状态信息显示。
- 怪物与遭遇预览：查看敌方物品、技能等信息。
- Tooltip 增强：补充附魔文本，并支持升级预览。
- 历史面板与战斗回放：查看历史记录，并回放已保存的战斗。

## 仓库结构

- `bazaarplusplus-mod/`：BazaarPlusPlus mod 本体源码目录。
- `bepinex-mac-patcher/`：用于 macOS 的 patcher，以 Git submodule 形式引入。
- `bazaarplusplus-installer/`：桌面安装器源码目录，基于 Tauri、SvelteKit 和 TypeScript。

## 二次开发说明

如果你计划基于本项目或本模组进行二次开发，请务必遵循《The Bazaar》的官方 Mod Policy：
[The Bazaar Mod Policy](https://www.playthebazaar.com/mod-policy)

## 支持者

感谢所有支持 BazaarPlusPlus 的朋友。这个项目能持续迭代、维护和公开发布，离不开这些支持者的帮助与信任。

![BazaarPlusPlus Supporters](https://bpp-static.bazaarplusplus.com/supporters.jpg)

也感谢所有未署名的支持者。

## License

本项目使用 MIT License，见 `LICENSE`。

## 致谢

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
