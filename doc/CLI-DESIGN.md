# CLI能力分层与App对等设计

## 目标与状态

**CLI是整个App能力的可编程入口，不是为了AI另外复制一套业务命令。**
AI、人和脚本使用相同的领域操作、状态与权限；App支持的能力应能通过明确的CLI语义完成并核查效果。
目标不是为每个按钮、页面或Core方法生成一个命令，也不是靠坐标点击获得表面覆盖。

本文是当前设计约束与后续收敛方向，**不是已完成的全App覆盖清单**。
实际可执行语法仍以[CLI说明](CLI.md)、命令树和`--help`为准。
规范化命令树、`app`控制分支和Core传输/协议保留；Sprout端提前扩出的控制器及接线已移除。
当前App不注册控制宿主，未来应让页面和宿主端点共同调用App功能控制器，而不是端点调用页面控件。
Core受控回调与CLI通过不等于真实App已接入；本期不继续App/VM实施或验收。

## 1. 先按能力所有者划层

| 层 | 负责什么 | 执行与覆盖要求 |
| --- | --- | --- |
| 启动器领域 | 实例、运行环境、Java、内容、世界、配置、会话、任务、诊断 | CLI/App调用同一Core能力；没有App窗口也能执行，不能依赖当前页面或选择。 |
| AI领域 | 模型连接/绑定、本地权重与引擎、对话/回合、技能、授权工具与外部安装 | 属于`ai`；不在其中再建`ai instance`、`ai content`等启动器业务副本。 |
| App宿主 | 包偏好、外观/壁纸、窗口、页面导航、当前选择及草稿等真实界面状态 | 归明确的`app`能力；业务变化仍回到Core，只有宿主状态由App协调。 |
| 公共机制 | 发现、结果、事件、异步观察、取消、审核与错误 | 一套语义与结果协议；不是把不同生命周期强制改造成一个通用任务引擎。 |

产品仍只有CLI和Sprout两个入口。App宿主控制需要实现时，由Core层的typed契约/客户端连接App拥有的本地端点，
App调用既有功能控制器；CLI不引用Sprout，Core不引用App。不能借用`ai serve`假扮App，
也不另起常驻代理、反射远控框架或第三个产品。

App未运行时，Core业务照常可用；宿主专属动作明确返回不可用或待激活状态。
若某项偏好确实支持离线处理，需核实包存储和生效方式后单独支持，不直接猜写包数据文件。
多App实例须按包身份、PID/启动时间与数据根识别目标，歧义时不能随便选一个。

## 2. 自然命令树：领域 → 资源 → 动作

命名按玩家要操作的对象组织，而不是按实现类、页面名称或“AI想做什么”组织。
只在一个领域内存在多种资源时增加一层，不为了形式统一把所有操作强制嵌套四五层。
现有`launch`等明确的高频任务入口可以保留；不能再增加同义的另一条业务路径。

| 领域/资源 | 规范职责 | 不应混入 |
| --- | --- | --- |
| `instance`、`runtime`、`java`、`content`、`world`等 | 各自业务与完整工作流，复用当前Core | 为AI重命名复制，或另建`app content install`。 |
| `config` | Core全局配置与显式实例覆盖，保留未知字段及继承移除语义 | App包语言、窗口或壁纸偏好。 |
| `ai service` | 连接协议、地址、凭据引用和远端模型发现 | 把一次模型回答藏成连接健康检查。 |
| `ai model` | 已配置的模型绑定、默认选择和持久参数 | 混淆模型绑定ID、实际provider引用与磁盘权重。 |
| `ai local` | 本地模型资源与本runtime的引擎生命周期 | 独立rin服务管理，或另一套普通生成命令。 |
| `ai request` | 一次性生成，可明确选择服务/绑定并临时覆盖参数 | 用来改模型默认值、管理会话或隐式执行工具。 |
| `ai chat` | 持久会话、发送，以及其回合/调用/审核/归档等子资源 | 所有启动器业务、全局后台作业或App窗口生命周期。 |
| `ai repair` | 故障委托、不可变现场、会话绑定、试验收据及验证/回退；底层修改复用现有Core内容/配置/启动服务 | 不复制内容管理、下载或游戏启动业务，不替代原有`repair INSTANCE`文件修复。 |
| `ai skill`、`ai mcp`、`ai file-root`、`ai integration` | 指令目录、外部工具源、授权根与外部客户端安装 | 复制已实现的模型调用、文件业务或客户端自有配置系统。 |
| `app` | App实例、包偏好、外观与界面会话状态 | Core业务的第二份CRUD，或任意调用App私有方法。 |

`ai chat`优先按真实子资源收敛，而不是持续在同一级堆动词。例如目标组织可以是：

```text
ai chat create / list / show / update / export / remove
ai chat send CONVERSATION ...
ai chat turn show / wait / events / cancel / retry ...
ai chat confirmation show / decide ...
ai chat effects check / acknowledge ...
ai chat archive read ...
```

上面的资源组织已落实为当前命令；具体参数以`--help`为准。
发送只保留一个创建回合入口，不同时保留平铺和嵌套两套长期路径。
App控制分为实例发现/状态、preferences、navigation、window、wallpaper与图像cache，
没有为每个菜单按钮建命令。App的cache与Core依赖缓存不同，不混为一个清理入口。

## 3. 已核对的重复点与非重复点

种子探索的业务所有者为Core `Worlds/Seeds`，规范入口仅有`world seed`，不增加`app seed`或AI专用筛种副本。它查询指定版本新世界的原版生成结果；既有`world list/import/export/backup`管理玩家实际存档，所有权与副作用不同。地图/搜索/收藏/PNG及`world seed structures`、`world seed region`由同一Core提供，区域分析只生成内部隔离世界，不打开玩家存档；窗口平移、缩放和Popup开合属于App视图状态；CLI不模拟这些控件。原版资源准备复用安装/Java流程，后台执行复用typed任务及原命令参数映射。新增命令不会自动加入内置AI工具许可目录，通用MCP execute仍按实际命令与现有权限检查执行。

| 已核对入口 | 源码事实 | 当前收敛 |
| --- | --- | --- |
| `ai request`与旧`ai service test` | 原来均调用`Assistant.GenerateAsync` | 已合并为`ai request --service ID`；不再提供默认问候式“健康检查”或旧别名。 |
| `ai skill list`与旧`ai skill check` | 原来均调用相同`ListSkillsAsync` | 已只保留`ai skill list`，包括真实逐文件警告。 |
| `version`与旧`self version` | 原来均返回`LauncherClient.GetVersion()` | 已只保留根`version`，不影响独立的`self check-update`。 |
| `ai service models`与`ai local models` | 一个以配置连接为范围，一个不要求先建连接而列本地模型库 | 先保留不同发现范围；只有来源与目标语义统一后才合并，不因都返回列表就删除。 |
| `config set`与`config patch` | 单值与批量改动/移除；后者不能用多次独立写入等价替代 | 保留明确的便利写入与事务写入，不复制设置业务。 |
| `chat wait`、`job`与游戏`process` | AI回合、持久任务和实际游戏进程是不同资源 | 可共用观察/错误表达，但取消一个不能暗中取消其他资源。 |
| MCP具名工具与CLI命令 | 是不同传输表示，具名工具与内置AI已复用Core typed入口 | 不算重复业务；不为覆盖每条命令机械新增一个具名工具。 |

旧同义命令已删除而非隐藏，调用点与正式测试使用当前符号/语法。
会话rename/mode/access合并为`chat update`一次修订提交；空改动与陈旧修订均拒绝。
继续扩展时同步实际符号、MCP、内部调用、产品技能、测试和文档，不引入兼容框架。

## 4. AI可用性必须单独闭环

**App有能力、Core有API、CLI有命令、AI获准执行，是四件不同的事。**
外部MCP保留CLI专属`execute`和命令帮助；内置AI直接调用Core/Tools，不启动CLI。输入契约、schema、分页/结果保留和工具处理归Core，正常CLI命令仍由System.CommandLine直接绑定Core业务与typed任务，不复制事务业务。

内置AI按领域渐进发现工具、按需读取技能和绑定状态，关闭工具时只进行普通对话。发现与执行许可分开：安全模式仍由Core暂停写入等待授权，允许全部也不绕过范围、冻结审核与效果检查。对原先依赖通用CLI执行的长工作流，改为既有Core typed任务的具体工具与job观察；广域任务要求无实例绑定、操作许可及文件/网络开关，不假称具备普通文件工具的目录沙箱。

后续工具继续声明真实目标、读取/写入、文件/网络效果、审核、幂等/未知效果与结果；这些语义由操作所有者维护。App状态/壁纸/图像缓存与typed控制继续经选择的真实App宿主，存在目录不等于宿主正在运行。

发现结果应区分已实现、所需目标/权限、宿主未运行和确切前置条件，而不是统一伪装成“不支持模型”。

权限仍分层：可操作不等于已获授权；启动器确认、存档保护和撤销仍由真实Core/App执行。
凭据可以通过安全流程配置，但不能因追求覆盖就向模型回传秘密。
AI配置更新影响后续请求，在途绑定不被改写；同数据根的嵌套生成不能借控制接口绕过并发所有权或造成等待自身。
App的脏草稿、未提交选择和运行中任务也不能被后台命令无声覆盖。

## 5. App对等的检查与验收

对每个真实玩家意图记录：**能力所有者 → Core/App契约 → 唯一CLI路径 → MCP/AI可发现与授权 →
执行/审核 → 真实状态回读 → 失败/取消/恢复**。沿用[能力映射](CORE_API.md)与[对齐表](ALIGNMENT.md)，
不维护另一份靠命令数量判定完成的目录。

已抽样确认的缺口：App的语言/主题、启动后与关闭行为、数据根偏好和壁纸在包`LocalSettings`及App功能中，
这些领域不由`config set`管理。Core保留当前用户命名管道与`app`契约，但此前直接依赖页面的Sprout控制器已移除，
当前没有真实前台宿主接线；未来仍由App功能所有者执行，不能由CLI复制业务。
发现记录存放在所选数据根的`app/hosts`，避免跨包AppData虚拟化导致双方看见不同目录；
实际PID/启动时间、OS包身份与管道服务进程均核对，nonce不经公开结果或文件快照暴露。
窗口状态/导航也不是Core配置。草稿保护仅作用于会丢弃草稿的选择/导航，不阻止无关窗口操作。
App设置中的Java/网络/内存等则复用Core配置，不应再做一套App命令。
App全部操作的CLI映射尚未逐项验证，不能标记“整个App已可CLI操控”。

验收以完整意图而非命令数为单位：

- 无App窗口导入实例、准备运行环境、审核/应用内容、观察任务并启动游戏，使用原领域命令。
- 在明确App实例中修改外观或导航，核查实际可见状态和持久值；重启生效项明确报告，不只返回写入成功。
- 使用已有模型引用或GGUF绝对路径建立绑定，生成/多轮/取消并卸载；加载由Inference完成，不在启动器另建导入体系。
- 查询、授权、确认、执行与回读走同一工作流；权限撤销、陈旧审核、部分失败和未知效果不得被新入口绕过。

常规契约与恢复沉淀在正式[test](../test/README.md)；涉及App宿主时增加实际packaged NativeAOT验证，
不把Core夹具、UI点击或旧构建证据当作App对等完成。实现按独立纵切推进，不以整棵树重写作为前置条件。

## 定位依据

- [命令组合](../src/Rodel.MinecraftLauncher.Cli/Commands/LauncherCommands.cs)、[服务/模型命令](../src/Rodel.MinecraftLauncher.Cli/Commands/Assistant/ServiceCommands.cs)、[技能等扩展命令](../src/Rodel.MinecraftLauncher.Cli/Commands/Assistant/ExtensionCommands.cs)。
- [Core配置命令](../src/Rodel.MinecraftLauncher.Cli/Commands/Groups/ConfigurationCommands.cs)、[信息发现](../src/Rodel.MinecraftLauncher.Cli/Commands/Groups/InformationCommands.cs)、[内置AI工具策略](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiToolPolicy.cs)。
- [App包偏好](../src/Rodel.MinecraftLauncher.App/Settings/AppSettings.cs)、[Core设置控制器](../src/Rodel.MinecraftLauncher.App/Features/Settings/Preferences/CorePreferencesStore.cs)、[壁纸服务](../src/Rodel.MinecraftLauncher.App/Features/Wallpapers/WallpaperService.cs)、[窗口偏好](../src/Rodel.MinecraftLauncher.App/Features/Shell/WindowPreferences.cs)。

## 游戏桥命令归属

实时数据以已运行的session为目标，归既有`process`：`bridge show`、`game snapshot/region/query/events/edit`、`game operation show/cancel`，不另建同义session根或复制实例/世界文件业务。`launch --game-bridge`控制下一次启动，配置归实例，App只持有展示状态。具名MCP与execute复用实际命令符号及Core源生成请求，AI许可和世界所有权分别检查；端到端使用见[游戏桥](JAVA-GAME-BRIDGE.md)。
## 联机能力所有权

`multiplayer` 管理短期房间，`server` 管理长期地址，实例/版本/整合包准备仍复用原领域。创建/加入、读状态、自动或显式启动、按游戏与世界收尾、重试、改码、备用地址和日志导出均调用 Core 同一服务；原生弹窗仅保留草稿和任务观察，不把协议/恢复/内容事务移到 UI。`pack export --for-multiplayer`、`pack stage-file` 和 `versions list --refresh` 是既有领域的规范入口，不另建联机版安装器或缓存。网络就绪不等于游戏入服；当前没有标准环境元数据交换或自动隧道。
