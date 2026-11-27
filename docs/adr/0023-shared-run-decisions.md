# 对局决策由网站和安装器共用

网站从 Run segment 的 key 8 读取消息，安装器从本地历史数据库读取同一局的消息。两端需要按相同的游戏状态、响应序号和战斗记录还原拜访与决策。

根目录 `runs/` 持有累积的 Game Message Schema、消息解码、未知字段的原始值、决策和拜访投影，以及展示动作时的合并规则。两个产品直接编译这些 TypeScript 源文件。模块使用相对导入，没有 package.json、独立构建、React 或产品依赖。含 schema 的 decision-path 入口按需加载。纯投影测试由根测试运行器执行，网站用实际 Run segment 验证解码集成。

mod 持有本地事件行和 key 8 的写入契约。网站负责解压与账户传输，安装器负责 SQLite 与 IPC。产品提供文案、卡牌查询及操作，DayCard 与 RunReplay 的 React 实现按 ADR-0018 归 design/ui。

2026-11-27 注：回放接入 `DecisionReplay`、卡牌查询和游戏术语也归 design/ui，产品只提供这一局的数据，见 [ADR 0025](0025-card-catalog-in-the-ui-kit.md)。

2026-11-27 注：`runs/` 已迁至 `packages/runs/`（内部 workspace 包 `@bpp/runs`），`DecisionReplay` 已通过结构化类型与 `@bpp/runs` 解耦，见 [ADR 0026](0026-standalone-ui-and-runs-packages.md)。
