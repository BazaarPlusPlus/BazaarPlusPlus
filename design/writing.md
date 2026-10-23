# Voice and writing

BazaarPlusPlus speaks like a calm, exact tool. It says what happened, what it means for the player's data and what to do next. The installer and the site use the same words for the same things, in both languages.

## Voice

- **Plain and exact.** Name the thing and the action: "删除本地数据", never "清理一下". State facts, not feelings.
- **Calm.** No exclamation marks, no emoji, no internet slang and no hype in product UI. Warmth belongs only in support copy ("请 Bazaar++ 喝一杯").
- **Never blame the player.** Say the cause, not the fault: "The Bazaar 仍在运行", not "你没有关闭游戏".
- **Address the player only when they must act.** In Chinese, prefer no pronoun; use "你" when needed and never "您". In English, use "you".
- **Say what is kept.** Any destructive action says what it deletes and what survives.

## Say less

Let the layout do the explaining. Position, grouping and the control itself already tell the player a lot; text repeats it only when it would otherwise be missed.

- **One label per thing.** A title, a field or a menu item gets one line. Add a second only when the first cannot stand alone.
- **No echoes.** A subtitle never restates its title, a hint never restates its label, and a description never restates the button beside it.
- **Context names the object.** Inside a row, panel or dialog that already names the thing, a button is just the verb: in the 本地数据 row, the button is 删除, not 删除本地数据. A standalone button still takes verb + object.
- **Show values, not explanations.** Write "4.2 GB" and "128 局" instead of a sentence about how much is stored.
- **Instructions only where a step is not obvious.** "请先退出游戏" earns its place because the action would fail otherwise; "点击下方按钮" never does.
- **Empty states keep one sentence** that says what will fill the space. Errors keep one sentence of cause; the retry button carries the action, so the sentence doesn't repeat it.

## One language per screen

- Chinese (zh-CN) is the source and English mirrors it key for key. Both ship with every change.
- A screen is in one language. Inside Chinese, only proper nouns stay in English: The Bazaar, BazaarPlusPlus, BepInEx, Steam, OBS, V5, V6.
- The language toggle names the other language: "EN" in Chinese, "中文" in English.

## Glossary

Use exactly these words. A term not listed here gets added before it ships.

| 中文 | English | Meaning and rules |
|---|---|---|
| BazaarPlusPlus | BazaarPlusPlus | The product. Never translated or abbreviated in UI; "Bazaar++" only in support copy. |
| The Bazaar | The Bazaar | The game. Kept in English in Chinese copy; "游戏" once the context is clear. |
| 插件 | plugin | BazaarPlusPlus as installed into the game. Not 模组 / mod. |
| BepInEx | BepInEx | The plugin loader. Always this spelling. |
| 游戏目录 | game directory | The game's install folder. Not 安装目录, folder or path. |
| 本地数据 | local data | Everything BazaarPlusPlus stores on this computer: the database, screenshots and replays. |
| 战绩 | History | The page and the record of past runs. Not 历史记录. |
| 对局 | run | One game from hero pick to the end. Counted "{n} 局" / "{n} runs". |
| 战斗 | battle | One fight inside a run. |
| 回放 | replay | A recorded battle. Not 回放视频 or 战斗回放视频. |
| 截图 | screenshot | The end-of-run board image. |
| 英雄 | hero | A playable character. |
| 排位 / 匹配 / 普通 | Ranked / Unranked / Normal | Run modes. |
| 十胜 | 10 wins | A run that reached 10 wins. |
| 直播 | Stream | The OBS overlay page and service. |
| 更新 / 版本 | update / version | A version is written "v6.2.0" in UI chrome. |
| 安装 / 重新安装 / 修复 / 卸载 | Install / Reinstall / Repair / Uninstall | Plugin actions. Uninstall removes the plugin and keeps local data. |
| 删除 | delete | Removes files for good. Not 清除, 清理 or 移除. |

## Capitalisation and punctuation

**English**

- Use sentence case for everything: buttons, titles, menu items, badges and tabs. Write "Choose game directory", "Delete local data", "Not installed".
- Never use ALL CAPS. A run result reads "Gold victory".
- Buttons, titles, labels, badges and one-phrase toasts take no period. Full sentences in descriptions, banners and dialogs end with one.
- Use American spelling ("canceled") and a single "…" character for ongoing progress.

**中文**

- Use full-width punctuation: ，。：；？（）. Use a single "…" for ongoing progress, the same as English.
- Put a space between Chinese and Latin letters or digits: "The Bazaar 与 BazaarPlusPlus", "有 3 局". Put no space before full-width punctuation, or between a number and %.
- Buttons, titles, badges and one-phrase toasts take no "。". Full sentences end with "。".
- Use "请" once, at the start of an instruction: "请先退出游戏，再安装 BazaarPlusPlus。"

## Message patterns

| Pattern | 中文 | English |
|---|---|---|
| Button: verb + object | 选择游戏目录 | Choose game directory |
| Page title: a noun | 战绩 | History |
| Dialog title: the action, same words as its confirm button | 删除本地数据 | Delete local data |
| Status: a short state | 已安装 · 需要重新安装 | Installed · Reinstall required |
| Progress: 正在 + verb + … | 正在卸载… | Uninstalling… |
| Success: object + 已 + verb | 本地数据已删除 | Local data deleted |
| Failure: what failed, plus a step only if the button can't carry it | 未能读取战绩。请先关闭游戏。 + 重试 | Couldn't read History. Quit the game first. + Retry |
| Blocked: the cause, then the action | The Bazaar 仍在运行。请先退出游戏，再删除本地数据。 | The Bazaar is still running. Quit the game, then delete local data. |
| Empty: what will fill the space | 还没有战绩 · 完成一局游戏后，战绩会出现在这里。 | No runs yet · Finish a run and it appears here. |
| Action inside a named row | 本地数据 ··· 删除 | Local data ··· Delete |
| Confirmation target | 目标：{path} 内的 BepInEx 文件夹 | Target: the BepInEx folder in {path} |
| Acknowledgement | 我知道这些本地数据会被删除。 | I understand this local data will be deleted. |

- Failures say "未能…" / "Couldn't…". Reserve "无法…" / "can't…" for what is impossible, such as "完成前无法取消".
- A retry button reads "重试" / "Retry". In a sentence, end with "请重试。" / "Try again." (never "Please retry"). The secondary dismiss is "取消" / "Cancel", or "稍后" / "Later" for an offer.
- A confirmation dialog names the target, says what is deleted and what is kept, and asks for the acknowledgement before its confirm button enables.

## Numbers, dates and placeholders

- Format numbers and dates with `Intl` in the UI locale, and set them in tabular figures.
- Counts: "{n} 局", "{n} 个文件". English plurals go through `{count|run|runs}` and are never built by hand.
- Percentages have one decimal and no space: "48.0%". Sizes have one decimal and a space: "4.2 GB".
- Dates use the locale's medium date and short time: "2026年10月23日 21:30" / "Oct 23, 2026, 9:30 PM".
- Insert values only through named placeholders such as `{path}`, `{count}` and `{version}`. Never concatenate translated fragments.

## Exceptions

- **Data, not copy.** Credit roles on the About page ("AUTHOR", "CO-AUTHOR") come from the app bootstrap data the server publishes, not from UI strings; they follow this standard when that data changes.
- **In-game names.** Site copy that names a panel inside the game (the F8 "历史记录" panel) uses the game-side name the player sees.
