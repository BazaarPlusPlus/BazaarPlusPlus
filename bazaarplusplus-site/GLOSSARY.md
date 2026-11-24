# Hero Analysis Language

Use these concepts in code, docs, and analysis copy:

| Concept | Meaning | Avoid |
| --- | --- | --- |
| **Hero Analysis** (英雄分析) | Rankings, trends, and matchup conclusions drawn from hero statistics, independent of page layout. | Hero read model, dashboard data |
| **Hero Metrics Dataset** (英雄指标数据集) | The per-day raw statistics available for analysis and their available dates, independent of schema version and transport format. | analyzer payload, dashboard data |
| **Analysis Scope** (分析范围) | The time window and hero segment (All, Legend, Non-Legend) one analysis covers, independent of UI and URL representation. | filters, query params |
| **Dataset Coverage** (数据集覆盖) | How expected dates relate to available and failed dates; failed dates stay visible and never become zeros. | load count, completeness flag |

## Run detail

| Concept | Meaning | Avoid |
| --- | --- | --- |
| **Game Message Schema** (游戏消息结构) | The MessagePack layout of the game's network messages (member keys, union tags, enum values, by qualified type name), merged from every Game Assembly Snapshot read and recording which build each entry came from. A Run's Run Message Stream is read with it. | message map, proto |
| **Decision path** (决策路径) | The Run detail read per day and hour from the Run Message Stream: what was offered, what the player did with it and the Battle that followed. | replay, timeline (the Battle list) |
