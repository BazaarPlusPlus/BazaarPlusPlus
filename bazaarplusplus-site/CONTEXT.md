# Hero Analysis Language

在代码、文档和分析文案中统一使用以下概念：

| 概念 | 含义 | 避免使用 |
| --- | --- | --- |
| **Hero Analysis（英雄分析）** | 从英雄统计数据中得到的排行、趋势和对局结论，与页面布局无关。 | Hero read model、dashboard data |
| **Hero Metrics Dataset（英雄指标数据集）** | 可供分析的逐日原始统计及其可用日期，与 schema 版本和传输格式无关。 | analyzer payload、dashboard data |
| **Analysis Scope（分析范围）** | 参与一次分析的时间窗口与英雄分段（全部、传奇、非传奇），与 UI 和 URL 表示无关。 | filters、query params |
| **Dataset Coverage（数据集覆盖）** | 预期日期与可用/失败日期的关系；失败日期保持可见，不转成零值。 | load count、completeness flag |
