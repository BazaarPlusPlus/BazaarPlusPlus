---
name: qq-announce
description: Post a promoted BazaarPlusPlus release to the QQ groups through the QQ official Bot — the release notes as Markdown, then BPP_Win_<v>.exe, BPP_Mac_<v>.dmg and BPP_Android_<v>.apk. Previews in the test group first, then sends to every release group. Run by hand after promote.
disable-model-invocation: true
argument-hint: [release notes]
---

# Announce a release in the QQ groups

`scripts/announce.ts` does the work; run it from the repository root. It sends through the shared client `scripts/qqbot/client.ts`, which reads the bot's AppID and AppSecret from `config.ini` `[qqbot]`; the `qqbot` profile injects them, so a `--send` run starts with `bun scripts/workspace.ts run qqbot --`. Every message it sends is public at once and cannot be recalled by this skill, so each send waits for the user's yes.

## Groups

`scripts/qqbot/groups.json` lists the groups by name and `group_openid`:

| Role    | Group                                                               |
| ------- | ------------------------------------------------------------------- |
| test    | BPP Bot 开发测试 (2 members)                                        |
| release | Bazaar++内测, 巴扎加加 2 群, 巴扎加加 3 群, BazaarPlusPlus 安卓测试 |

Each group gets the same messages, in this order: notes, Win, Mac, Android. `heroReport` in the same file names the groups that get the daily report (`.github/workflows/daily-report.yml`).

## Steps

1. **Versions.** Run the dry run (step 3) without `--notes` first: it prints each platform's published version. Desktop and Android can differ; Android may also lag behind. Ask the user which platforms this announcement covers, and leave the others out with `--platforms` (`win`, `mac`, `android`), so the groups do not get an installer they already have.

2. **Notes.** The user writes the release notes. Put their items into `assets/notes-template.md`: fill `{version}` with the desktop version, followed by `（安卓 <v>）` when Android's differs, and today's `{date}` (YYYY-MM-DD), sort each item under 新增, 修复 or 改进, and drop a heading that has no items. `{highlight}` is one line the user gives as the release's headline; without one, drop the quote line rather than write one. Keep the user's wording; do not add, merge or rephrase items. Ask where an item belongs if it is not clear. Write the result to the scratchpad as `notes-<version>.md`.

3. **Dry run.** Run without `--send` and show the user the notes and the printed plan:

   ```bash
   bun .agents/skills/qq-announce/scripts/announce.ts --target test --notes <notes file> [--platforms ...]
   ```

   Check that each file's version matches the title of the notes.

4. **Preview in the test group.** After the user's yes, prefix `bun scripts/workspace.ts run qqbot --` and add `--send`. Ask the user to check the test group: the notes render as Markdown and the file names are right.

5. **Release.** After the user says the preview is good, run the dry run with `--target release`, show the plan, and on their yes run it again through `bun scripts/workspace.ts run qqbot --` with `--send`. The script sends to the release groups one after another.

6. **Report** what the script printed: the groups done, or the error and the resume commands.

## Failures

- If the script stops partway, it prints what each group got and the `--groups`, `--platforms` and `--notes` to resume with. Use exactly those, so no group gets a message twice.
- `40034105`: that group has not allowed proactive messages. The group owner must remove the bot, re-add it from mobile QQ, and enable 「设置主动推送消息能力」 under 「设置权限」.
- `40093002`: the daily file upload quota is used up. Resume the next day.

## Adding a group

The QQ API has no call that lists the bot's groups, and a QQ group number is not a `group_openid`. A new group's openid only arrives in an event: add the bot to the group (with 「接收所有消息」 and 「设置主动推送消息能力」 on) while a gateway listener runs, then add `{ "name", "openid" }` to `scripts/qqbot/groups.json`. `GET /v2/groups/{openid}/info` returns the group name and member count.
