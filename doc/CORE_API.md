# Sprout 直接使用 Core

`LauncherClient.EnsureInstanceIdle`为变更前检查提供同一游戏目录／完整进程身份／任务依据。冲突仍使用`INSTANCE_BUSY`，`data`按源生成的`InstanceActivityConflict`携带`reason`（`game`、`task`、`content-download`、`unknown-task`）及可用的`jobId`，供CLI诊断和App本地化；不把后台下载说成游戏未退出。允许独立在线内容任务时仍保留提交时漂移检查，该方法不取消任务、不清理玩家记录，也不代替实际写入事务的保护。

## 联机与共享版本目录

`LauncherClient.Multiplayer` 是 Core 持有的房间服务；CLI 和 Sprout 调用同一 typed `MultiplayerTask`，App 不依赖 CLI。`PrepareAsync` 在任务持久化前保护邀请；`RunAsync` 持有数据根独占租约、所选游戏及组件身份，`Get` 读取可观察快照，`EndAsync(roomId, MultiplayerEndMode)` 明确区分 KeepGame 与 ExitGame，`ControlAsync` 接受 stop/retry/launch/fallback，`ChangeInvitationAsync` 使用独立受保护输入，`ExportLogAsync` 仅导出有界状态/错误记录。接口模型和 NativeAOT 元数据归 `MultiplayerJsonContext`。

世界关闭／切换或游戏退出后，房间清理并变为非活动终态，不保留空活动房间。加入默认在固定网络入口就绪后启动；StartGame=false 保留网络专用模式，已有其它目标游戏只有 RestartGame=true 才可正常退出并重新启动。多个运行会话要求 SessionId，显式世界启动与运行会话选择互斥。结束控制冻结房间、会话、PID和启动时间，已有世界还须核对真实 local world epoch；正常窗口关闭意图先持久化，再发送一次，随后只观察进程退出。ending／exit-pending 不允许网络重连或新游戏操作，未决结束不能被新房间覆盖。无法确认或正常退出超时保留恢复入口，绝不隐式强杀；已准备实例不删除。启动前即登记本次新会话，准备中的结束可取消自己的启动，并等待真实会话结果。

网络、世界、游戏进程分别观察；`Members` 是网络成员而不是实际在线玩家。原生协议未提供 UUID/皮肤，不能按姓名推断头像。固定未修改陶瓦0.4.2随 CLI/App 正常打包，短 TEMP 按数据根隔离，程序映像在当前用户固定版本目录经哈希核对后复用；固定0.4.2的底层解包目录经SDK创建的受控NTFS目录联接指向同一引擎文件，Windows过滤平台应用身份不随时间戳/PID改变，退出只删除临时联接而非共享文件，不写防火墙策略；仅控制已核实 PID、启动时间、二进制与本地监听端口的组件。发现的目标端口还须属于所选 Minecraft，并通过组件私有协议回读一致后才能显示邀请。多候选不猜测选中；组件缺失/映射失败保留手动 LAN 路径。仅停止联机保留游戏；显式 ExitGame 与尚未完成的本次新启动取消按上述正常退出流程处理。

`world.publish` 使用原生 IntegratedServer.publishServer，保留世界模式和作弊设置。带 MultiplayerScope 的客户端使用双参数 LAN 接口，另行保留访客命令权限；旧接口继续沿用原世界设置。先解析固定绑定再进入效果阶段，已知接口缺失应拒绝而非报告未知效果。首个本地 world epoch 与游戏 incarnation 冻结，换世界会关闭房间，不自动开放下一个世界；未知效果不重放。加入端由房间持有固定回环转发入口，组件恢复后交换目标、关闭旧流，游戏自身需要重新连接。备用地址由调用者明确提供，不表示已配置中继。

`GamePort` 的进程及游戏协议核验与 `ComponentState` 的原生阶段分别保留。房主已找到监听、组件仍为 `host-scanning` 时报告 `discovering`，等待超时报 `MULTIPLAYER_LAN_DISCOVERY_TIMEOUT`；收到正确原生就绪及端口回读后才显示邀请。已有监听后，自动开放的未知／失败回执只更新世界观察，不覆盖网络状态或重放原操作。状态导出包含房间、组件、世界阶段以及 `gamePortObserved`，不导出监听端口数值、地址、邀请或玩家资料。

备用入口区分 TCP 可达与游戏状态可读；状态未返回时仍可尝试连接，`ErrorCode=MULTIPLAYER_FALLBACK_STATUS_UNKNOWN` 不代表认证或入服失败。后台探测失联不撤销已有流；被动解析更新只改变新 TCP／UDP 会话的目标，已建立会话保留旧目标直到结束、UDP 空闲过期或显式房间撤销。自动扫描本机房主仍要求真实 Minecraft 状态，不能把普通开放端口当作世界。网络恢复也不清空独立的游戏启动错误。

`Installation.GetVersionsAsync(offline, cancellationToken, refresh)` 共享按缓存目录/来源区分的六小时内存/磁盘目录，有进程内并发合并、跨进程资源锁及失败保留。`Packs.StageManualFileAsync` 只复制明确缺失条目到私有暂存区，现有 complete 才验证/发布。`Packs.ExportAsync(..., forMultiplayer: true)` 使用共享配置与可分发引用的导出配置，不携带玩家世界、个人设置或无来源本地模组，缺件由结果明确报告。

内置模组由`LauncherClient.GetBundledModsAsync`读取随包版本、实际加载器与实例状态，`ReviewBundledModsAsync`冻结所选ID、依赖及内容基线；提交复用`ApplyContentReviewAsync`和`ApplyContentReviewTask`。Core从`Assets/BuiltInMods/<Minecraft>/<loader>`及`catalog.json`读取已登记的包，全程不访问社区。Rodel UI 已撤出内置目录，其它随包模组不得声明对它的依赖。既有玩家安装不自动卸载。唯一登记源是`mods/bundled-targets.props`；按真实继承元数据选取精确组合，缺少组合时返回可用`RuntimeTargets`，不安装猜测的默认包。Fabric、Quilt、Forge、NeoForge范围见[兼容矩阵](../mods/COMPATIBILITY.md)。原生ID、依赖及哈希来自同一内容扫描器，改名/禁用旧包替换与恢复复用内容事务；提交复核运行环境，加载器切换会使原审核失效。普通在线更新跳过`platform=builtin`，保留Fabric API须同时满足版本及Minecraft声明。

只读目录使用按游戏目标共享的进程内随包元数据缓存，调用方取消等待不会取消其他读取者；实例安装/启用状态每次实际读取。App在内容页准备目录，展开选择器不再调用Core或读盘。审核及提交继续重新读取实际随包文件、计算指纹，不将浏览缓存作为写入许可或来源完整性证据。

故障修复入口：`LauncherClient.Repairs.CreateAsync(AiRepairCreate)`固定实例与故障运行/任务，`InspectAsync/PlanAsync/ApplyAsync/VerifyAsync/RestoreAsync`读取、冻结及执行同一修复记录。`LauncherClient.Assistant.StartRepairAsync`创建或复用修复会话并幂等提交首回合；`AiChatCreate.RepairId`绑定专用工具范围。App只展示Core记录及已有ask_user输入，不实现修复Agent循环。`startup_checked`仅覆盖窗口、声音/纹理初始化及存活观察，不能推定世界内故障已解决。

这里描述已实现的C#调用入口，不是App全部界面的验收表。普通业务调用端只需引用`Rodel.MinecraftLauncher.Core.csproj`，不需要CLI可执行程序、命令参数或MCP会话；AI主动消费启动器工具时，正常安装的CLI是工具通道依赖，不是Core的项目引用或独立AI宿主。具体界面及验证边界见[对齐清单](ALIGNMENT.md)和[App说明](../src/Rodel.MinecraftLauncher.App/README.md)。typed任务与共享工具是两种不同目录，数量以实际声明为准，不表示玩家流程已全部完成。

默认内存由`MemorySelection.Configure`保留Core语义：`memoryReserveMb=0`真实生效，负值兼容回退1024；自动模式使用Windows可用内存与自动上下限，查询失败才使用`maxMemoryMb`。`minMemoryMb`两模式共用，超过本次最大值时降低；用户自定义JVM `-Xmx`优先，显式启动内存参数可覆盖。UI不能把备用最大值当自动估算上限，也不能因未改动的手工旧配置增加保存门禁。

## 原版种子探索器

`LauncherClient.Seeds`统一提供指定版本的只读生成能力。CLI和Sprout只引用Core；独立[`Rodel.Minecraft.Seeds`](../src/Rodel.Minecraft.Seeds/README.md)拥有typed契约、JSON源生成、校验、地图栅格/PNG和自有Java适配器，不引用Core。首个实际验证版本为26.3 / Java25，不近似映射其他版本，不使用快速生成库。

| Core入口 | 职责 |
| --- | --- |
| `Seeds.ListVersionsAsync` / `PrepareSeedRuntimeAsync` | 合并缓存版本目录和已登记实例，只启用客户端已在磁盘的实例原版版本；显式准备复用安装/Java任务，不创建玩家实例。列表可用性不等于跨版本生成已完成。 |
| `WorldMaps.ListAsync` / `DescribeAsync` / `OpenAsync` | 只读发现实例中的目录及早期有限存档，取得种子、维度、保存位置与源修订；保留读取会话供交互窗口使用，不升级存档。 |
| `WorldMapSession.MapAsync` / `WorldMaps.ReadMapAsync` | 读取有界的实际保存方块、群系及结构记录；纯读取的未知像素透明。ReadMapAsync可通过FillUnexplored显式请求原版补图，CLI read/export共用--fill-unexplored；源变化后须重新打开。 |
| `WorldMapSession.MapWithPreviewAsync` / `FilterPreviewStructuresAsync` | 保存像素优先，只对确认缺失或原版地表阶段前的区块使用独立原版预览；保存归属mask、预览runtime指纹及预测结构分别保留，生成失败不阻断可读实况，不写真实存档。 |
| `WorldMapSession.HasChangedAsync` | 比较元数据／区块文件时间、长度及新增文件，不解码NBT；变化后调用方重开会话。App保留视野自动刷新，在拖动、选区、详情或导出期间延后提交。 |
| `WorldMapSession.RegionAsync` / `WorldMaps.ReadRegionAsync` | 冻结实际保存的XYZ方块状态、材料和容器Items／components；未知位置单独计数，LootTable不求值，不要求可读种子。 |
| `WorldMapSession.FindStructuresAsync` / `WorldMaps.FindStructuresAsync` | 按半径、记录ID和数量限制读取存档结构起点及合并边界；不预测未保存地形。 |
| `WorldMapSession.BuildSceneAsync` / `ReadItemImagesAsync` / `WorldMaps.InspectSceneAsync` | 复用已安装原版模型、纹理和物品图像，现代紧凑状态的省略属性取自原版默认注册表，不重生成世界；scene提供CLI网格摘要。 |
| `WorldMaps.PredictContainerAsync` | 显式复核当前保存容器的坐标、表、seed和源修订，再在独立原版世界中创建脱离保存区块的容器并求值；结果保留原版运行时身份、确定性与警告，不覆盖已保存Items。CLI使用`world map predict-loot --request-file`。 |
| `WorldMaps.ObservePlayerAsync` | 区分保存／实时／失联等来源，实时结果要求精确目录、实例和游戏／世界代际匹配；不把无位置变成原点，不附加未启用桥的进程。 |
| `Seeds.OpenAsync` → `SeedExplorerSession` | 解析原版基础版本与完整继承链、准备依赖指纹并持有主查询JVM；每个未缓存的原版出生点计算使用可单独终止的临时子进程，完成即退出，调用方必须异步释放整个会话。 |
| `Seeds.GetCatalogAsync` | 直接读取精确原版指纹的群系/结构/维度目录缓存；缺失时才临时启动原版，标签来自当前语言资源，不创建地图或搜索。 |
| `session.GetCatalogAsync` / `NormalizeSeedAsync` / `MapAsync` / `SearchAsync` | 读取原版注册表、无损解析seed、计算地图瓦片及正向条件；地图/搜索不会读写玩家存档。 |
| `session.FindStructuresAsync` / `Seeds.FindStructuresAsync` | 按当前seed、结构ID、圆形半径查原版完整拼装结果，按水平距离排序并返回真实坐标/范围；结果数量上限和实际发现数分开。 |
| `session.SpawnAsync` / `Seeds.SpawnAsync` | 通过原版新世界初始化和真实地表检查返回主世界共享出生点；按运行时指纹/seed/预设原子缓存，区别于坐标原点和玩家随机落脚点。取消/筛选期限可停止所属出生点子进程，主会话保持可用。 |
| `session.RegionAsync` / `Seeds.RegionAsync` | 在新建隔离原版世界按固定阶段/顺序生成有界区域，返回实际容器、原版战利品和方块计数；可选完整状态palette及ushortLE体积。支持阶段进度、取消、验证后原子缓存。 |
| `session.BuildSceneAsync` / `ReadItemImagesAsync` | 核对运行时指纹后读取原客户端JAR模型和纹理；独立Seeds库构建分层网格/共享相机投影，Core不持有Sprout或GPU。普通物品图片使用原版PNG，缺图不伪造。 |
| `Seeds.Searches.CreateAsync` / `RunAsync` / `Read` / `List` | 保存条件快照、运行时指纹、已提交游标和见证；取消后可以跨进程继续。`CreateAsync`仅持久化，不启动Java。 |
| `Seeds.Bookmarks.List` / `SaveAsync` / `RemoveAsync` | 同一数据根下的种子/地点收藏；包含世界配置、地点与传送Y，UI不另存业务副本。 |
| `SeedExplorerService.ExportAsync` / `ExportViewAsync` | 原子导出完整瓦片或冻结的矩形视野；可见视野优先使用细瓦片，拒绝未知像素和混合上下文，失败保留旧文件。 |

`SearchSeedsAsync`复用统一任务进度；`MapSeedTask`、`SearchSeedsTask`、`PrepareSeedRuntimeTask`进入既有typed任务执行器，不另造种子任务引擎。输入/地图/搜索由`SeedJsonContext`序列化，持久搜索与收藏由`SeedStoreJsonContext`序列化。seed是字符串，不经浮点数传递。

瓦片缓存按精确运行时/生成配置键写入`cache/seed-explorer/tiles`；固定范围区域缓存位于`cache/seed-explorer/regions`；收藏和搜索位于数据根`seed-explorer/bookmarks.json`、`seed-explorer/searches`。会话在缓存读取前核对完整版本继承链及依赖，文件变化不能复用旧结果。损坏瓦片可重算，损坏持久业务文件明确报错而不静默覆盖。工作进程串行执行有界请求、支持协作取消并观察宿主退出；超时/协议损坏结束自身进程，不管理玩家Java。

`session.IsAvailable`检查所属工作进程是否仍可复用，释放、协议损坏或退出时为false；它不保证进程不会在下一次请求前退出。App在再次生成时重开不可用的同配置会话，取得新瓦片前保留上次完整地图；不反复复用已退出的Java进程。

地图输出是原版基础地形列、流体与地表/明确Y群系，不是完整装饰后的存档。结构见证来自原版完整选择和拼装，坐标为拼装范围中心；粗网格未命中不能证明群系不存在。三维度、67群系与52结构为26.3注册表范围。性能及版本限制见[种子探索器](SEED-EXPLORER.md)。结构/区域API已接线，不由API可用推定原生验收通过。X-Ray保留隔离评估，不提供产品界面。

## 从哪里开始

```csharp
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;

var paths = new LauncherPaths(dataDirectory);
var progress = new Progress<LauncherProgress>(value =>
{
    // 在 UI 线程创建 Progress<T>，这里就能更新 ViewModel。
});
var launcher = new LauncherClient(paths, progress: progress);

var instance = await launcher.Instances.CreateAsync("我的游戏", "1.21.1", cancellationToken);
await launcher.InstallInstanceAsync(instance.Id, cancellationToken: cancellationToken);
var plan = await launcher.LaunchAsync(instance.Id, new LaunchOptions
{
    PlanOnly = true,
    MaxMemoryMb = 4096,
    GameArguments = ["直接传入的参数值"],
}, cancellationToken);
```

`LauncherClient` 是一个可复用的应用门面。`LaunchOptions` 等对象表达用户选择，省略的值沿用实例/全局设置。字符串值直接进入对应的业务参数，不会因为以 `--` 开头被当作启动器选项。

异步API的`CancellationToken`位于参数末位；涉及可选参数时优先使用具名参数，不能沿用旧位置猜测绑定。调用方取消不等于已经提交的状态会回滚：活动日志、任务终态及已启动游戏的监控继续保留各自的提交/善后边界。`ResourceLease.AcquireAsync(resource, cancellationToken)`按规范化资源路径跨数据根协调，不再接收未使用的`LauncherPaths`。`GameSettingsService.DiscardPendingAsync(gameDirectory, cancellationToken)`为静态入口，只保留待应用设置到恢复文件，不修改`options.txt`。

## 业务实体与JSON边界

固定结构的读取摘要使用所属模块的实体，调用方直接访问属性，不再按字符串取顶层业务字段。例如`SkinService.Describe`返回`SkinSummary`，`Settings.GetEffective`返回`EffectiveSettings`，`Settings.Get`返回`SettingValue`，`GameSettings.GetAsync`返回`GameSettingsSnapshot`；版本、加载器和Java目录分别返回`VersionCatalog`、`LoaderCatalog`和`JavaRuntimeCatalog`，托管Java登记列表返回`JavaRuntimeSummary`集合。

内容清单/依赖分析、整合包检查、世界/NBT摘要、备份清单、服务器解析/状态、实例/导入预览、身份、任务/会话、缓存/回收、运行环境/操作记录、日志/诊断与内存建议沿用同一结果边界。任务的参数、进度和业务结果可以异构，状态实体不因此把它们猜成一套固定字段。损坏或部分可读记录仍逐项报告，不因实体解析丢掉其它可用项。

AI固定读取同样返回实体：技能使用`AiSkillCatalog`/`AiSkillReport`，会话与回合使用`AiChatRecord`及分页结果，事件使用`AiEventPage`，外部配置使用`AiExtensionReport`，本地状态与目录使用`AiLocalInferenceStatus`/`AiLocalModelCatalog`，MCP检查使用`AiMcpCatalog`。客户端集成计划/状态、删除审核、效果观察、确认与归档页也保持typed结果；AI宿主RPC只在传输边界序列化，进程内调用不绕回JSON。provider原始模型列表仍开放，不能把未知供应商响应伪装成固定业务摘要；App控制回调的开放结果也不由Core猜造一套界面状态模型。

强类型不等于收窄未知数据：有效设置的`Values`与`Sources`、游戏原始设置、Mojang清单、加载器provider条目、第三方响应及异构warnings仍保留开放JSON。目录结果的固定来源/离线标记等字段由实体描述，provider扩展字段按原有JSON层级透传；未出现的可选字段与显式null不混为一谈。`GetOverrides`仍返回玩家的原始可扩展配置，不把它当作固定摘要或删除未知键。

CLI/MCP只在输出边界使用显式源生成元数据序列化，App直接消费实体或其开放载荷，不先把实体序列化再按JSON键读取。JsonContext按业务模块集中维护；CLI自有协议元数据仍归CLI。固定写操作的回执、审核、计划、任务请求外壳及进度也使用具名实体，不以“操作会变化”为由保留整块业务JSON。任务执行器自己的序列化参数/结果、实际持久文档和第三方原文仍保留真实边界，不改变持久化边界。

### 固定载体的统一归属

服务直接构造所属领域的结果；只有读取既存文档时才从JSON投影。多个已知结果使用闭合接口/派生记录，不增加协议discriminator，也不靠`object`、`JsonNode`或字符串索引器绕过类型契约。

| 范围 | 进程内传递的具名实体 | 仍保留JSON的具体边界 |
| --- | --- | --- |
| 安装、加载器、Java | `InstallationResult`、`LoaderInstallationResult`、`JavaRegistrationResult`及安装/导入分支、`JavaRemovalResult` | Mojang/加载器目录、下载描述及供应商原始版本值；登记文件的合并/路径改写 |
| 启动与会话 | `ILaunchResult`（`LaunchPlan`/`SessionStatus`）、`IInstallationResult`、`SessionStopResult`、`LaunchResolution`、`LoggingCompatibilityPlan` | 原始版本继承文档、规则读取器、环境键值、会话文档及第三方日志解析器游标 |
| 实例与迁入 | `IInstanceImportResult`（预览/实例摘要）、`InstanceSummary`、`InstanceRemovalResult`、内部`InstanceCopySource`及共享依赖引用 | 玩家设置、来源启动器元数据/字段出处、实例原始登记的可恢复读写 |
| 运行环境 | `RuntimeTarget`、`RuntimeChangeResult`、`RuntimePreparationResult`、`RuntimeRollbackResult` | 原子切换journal、前后配置快照、发布者未识别依赖及既存目标原文 |
| 游戏设置 | `GameSettingsChangeResult`、`ResourcePackMapping`、`ResourcePackSelectionResult` | 开放的游戏设置键和值、待应用文档；不把MC/mod设置猜成封闭表单 |
| 内容 | `ContentResult`闭合结果、`ContentInstallPlan`、`ContentPlanNode/Edge`、`ContentReview`、`DependencyRepairReview`及具名finding | 第三方包/平台元数据、tracking/journal原文、开放版本范围和异构警告 |
| 整合包 | `PackState`、`PackReview`、`PackOperationList`、`PackExportResult`、`OnlinePackSelection`、`PackFileDeclaration/Record` | 原始manifest/来源字段、平台扩展；HMCL已知依赖与规范文件声明由格式读取器提取，不把整份JSON当文件实体 |
| 备份与世界 | `BackupManifest`、`BackupRestoreResult`、`BackupRestoreState`、`WorldOperationResult`及导入/导出/恢复分支 | ZIP清单、原实例快照、世界保留/恢复journal和NBT开放内容 |
| 身份与认证 | 微软/外置登录完成与刷新返回`IdentityRecord`；启动只传内部`GameLaunchCredentials` | 原始认证响应、受保护的MSAL/Yggdrasil缓存及未完成授权文档；凭据不进入公开回执 |
| 诊断与导出 | `ILaunchDiagnosis`、`LaunchDiagnosisProblem`、`ServerConnectionReport`、`LogExportResult`、`DiagnosticBundleManifest/Entry` | 第三方日志值、原始失败证据、异构warnings和诊断包写入边界 |
| AI | 会话操作/生成/模型目录/工具审核/效果观察/文件网络回执等所属AI实体 | provider/MCP原文、事件与授权/效果记录、受保护凭据；Host帧只在真实RPC边界序列化 |
| 任务与进度 | `JobStatus`、`JobRequest`、`LauncherProgress`；整合包进度携带独立`PackPreparation`快照 | `JobRequest.Parameters`由当前executor反序列化；任务结果/错误细节异构；`.progress`是持久边界，不是进程内消息格式 |
| App控制与基础存储 | App请求/响应帧固定外壳具名化 | App自有回调结果、文件/DPAPI/管道读写本身的JSON协议载荷，Core不反向定义App页面状态 |

`JobRequest`只固定`operation/parameters`外壳，不按缺失字段猜测可执行请求。`JobStatus`及其请求/进度读取保留未知字段、类型不符的已知值和显式null；损坏参数可以被检查，但不能因此降级为空参数执行。进度只携带对应阶段已有的字段：未知总量的null与该阶段没有总量字段不同。任务输出使用`ApplicationJsonContext.Job/Jobs`，进度使用`ApplicationJsonContext.Progress`；这些元数据同时处理嵌套进度的字段存在性，不能换回未经修饰的默认元数据。

内容的`ContentJsonContext.Result/Review/DependencyReview/InstallReceipt/InstallPlan/PlanNode`同样保留字段存在性。带`JsonExtensionData`的源生成反序列化模型使用可写扩展字典，避免把扩展数据误绑定为构造参数。实体化不能移除冻结审核、恢复/回滚、原始文件校验或非标准手工配置的既有路径。

Java选择说明已使用实体：`JavaSelection.Describe(...)`返回`JavaSelectionReport`，包含`JavaSelectionCandidate[]`与`JavaSelectionSource`枚举；`LaunchPlan.JavaSelection`也为该实体，而非JSON对象。内存建议、编码配置和自动准备后的来源更新直接访问类型成员；启动计划输出、安装结果和会话持久化才使用Java模块统一的`JavaJsonContext`序列化。原有字段名、显式null、候选摘要字段及五个source标识保持不变，不输出原始探测日志。

`JavaSelection.Choose`只排除探测不可用的候选，不把未知版本或兼容建议升级为硬拒绝。只有存在推荐主版本时才优先匹配该值；没有推荐时不会因`null == null`给未知主版本额外加权。其余继续按兼容建议、64位、完整版本和稳定路径排序；有建议但没有其他可用候选时仍可返回该Java，显式选择由真实计划保留。历史Minecraft/Forge提示范围的测试保护本项目既有规则，不等同于这些组合的游戏实测兼容保证。

原始Minecraft元数据仍为开放JSON。`RuntimeComponent`为可空字符串：声明的合法组件名优先，`Component(major)`只是缺省时的Mojang组件映射，不是支持版本白名单；未知主版本可返回null，不拒绝已有或手工指定的Java。异常的非字符串组件声明保留在原始元数据中，报告具名警告并尝试已知主版本映射，不把未知JSON塞入固定字符串字段或新增启动门槛。

## 全部已有功能如何对应

AI与其它业务一样由 `LauncherClient.Assistant` 提供typed Core API，默认在调用者当前进程执行，不是对独立助手服务的转发壳。Core在 `Application/AI` 持有配置、会话、状态、技能与编排，向内部Assistant子模块传递只含协议/模型/本轮凭据的请求上下文；后者不引用Core、CLI或Sprout，且不暴露独立产品入口。CLI与App均只引用Core。CLI为跨命令host_owned显式选择同一rodel-mc.exe的内部后台模式；App直接Core调用不需要这个模式。AI工具的MCP → CLI → Core是请求运行通道，不是项目依赖方向。完整范围与未完成项见[AI规格](AI-SPEC.md)，不以门面存在标所有AI能力完成。

长对话由Core `AiContextCompactor`自动归档/压缩，不放在CLI或界面。`ReadContextArchiveAsync(new AiContextRead { ConversationId = ..., ArchiveId = ..., MessageIndex = ..., Offset = ..., Characters = 12000 })`分页读取原始可见消息；省略MessageIndex读取整份portable消息数组文本，按`nextOffset/hasMore`续读。字符页256～16000、offset使用UTF-16边界，跨会话引用/损坏哈希明确失败，不返回opaque续传状态。当前模型的`conversation_history_read`映射同一Core读取逻辑，仅固定当前会话；不计入共享MCP目录。

`context_compaction_started/progress/finished`及`context_compacted`是实际压缩生命周期，选择调用的`model_started/model_finished`带`purpose=context_compaction`与parentStepId，usage另行记录，不进入answer增量。v2中模型只返回当前包整数ID的keep/omit，Core验证完整分类并组装带来源原文；contextCheckpoint给出archiveId、估算方法、前后字符数及source/retained/omitted单位，summarySource为host_validated_extractive，不能当生成的工具事实。

公开`GetTurn`不返回私有excerpts，提供excerptCount和最多8000 UTF-16单位的summary预览，summaryCharacters/summaryTruncated明确标识完整长度和是否截断；完整来源仍按归档分页，不能将预览导出误当全部保留文本。私有调用与存储仍保留完整原文，UI不解析或修改opaque状态。选择失败/取消/超预算不替换原窗口；策略、中文优先验收与未测边界见[AI规格第6节](AI-SPEC.md#6-上下文与预算)。

服务设置可使用`AssistantService.DiscoverModelsAsync(AiServiceUpdate, secret)`临时发现未保存连接的模型，不写配置或凭据；本地GGUF直接登记所选文件，不调用目录发现；`ListLocalModelsAsync`只保留推理库目录的CLI只读入口。模型重新勾选由`Assistant.Services.SelectModelsAsync`按服务修订原子提交，保留仍选中模型的身份、参数和未知字段，不逐项保存造成半份列表。

AI非UI工具/安装的typed入口如下，界面只消费Core状态，不实现第二套Agent循环或命令解析：

| 能力 | Core入口 |
| --- | --- |
| 会话/发送/观察/停止/历史 | `Assistant.CreateConversationAsync`、`SendAsync(AiChatSend)`的宿主内持久队列、`RunQueuedTurnAsync`优先发送指定排队消息并停止同会话当前响应、`GetTurnAsync/GetEventsAsync`、`CancelTurnAsync(AiChatCancellation)`的指定回合/会话队列取消，及已有修订/导出/删除API |
| Embedded本地推理 | `Assistant.GetLocalInferenceStatusAsync`、`ListLocalModelsAsync`、`UnloadLocalInferenceAsync`；生成/流式仍用统一请求与会话API，Core缓存借用引擎，不依赖独立daemon |
| 单次有界生成 | `Assistant.GenerateAsync(AiTextRequest)`；`Temperature/MaximumOutputTokens`只覆盖当前捕获请求，null沿用模型默认，返回effectiveParameters，不写回服务配置 |
| App宿主控制 | 保留`launcher.AppControl.List()`、`InvokeAsync(appId, AppControlRequest)`及`AppControlHost.StartAsync`协议；当前Sprout端接线已移除，不注册宿主。Core侧有当前用户/进程/OS包身份校验，不猜写包偏好文件；受控回调不等于真实App接入 |
| 精确产品确认 | `ConfirmToolAsync(AiToolConfirmation)`、`ReadToolConfirmationAsync(AiToolConfirmationRead)` |
| 效果核查/用户接受未知 | `CheckTurnEffectsAsync`、`AcknowledgeUnknownEffectAsync(AiEffectAcknowledgement)`；确认未知只允许另一个新目标，不伪造结果或允许原目标重放 |
| MCP、文件根与用户技能目录 | `Assistant.Extensions.GetAsync/SaveMcpAsync/SaveRootAsync/SaveSkillDirectoryAsync/SetEnabledAsync/RemoveAsync` |
| 模型无关的客户端安装维护 | `Assistant.Integrations.PlanAsync/StatusAsync/ApplyAsync(AiClientIntegrationRequest)` |
| 冻结内容审核观察 | `LauncherClient.GetContentReviewAsync`；应用仍复用原typed任务与陈旧检测 |

权限叠加、同目标结果复用、真实job admission关联、私有文件快照/可恢复提交、固定公开API检索、第三方进程与稳定客户端入口见[托管工具说明](AI-MANAGED-TOOLS.md)。MCP `_meta`携带的是客户端请求身份，不把它混入实际业务参数schema；背景作业独立于AI回合，停止回答不默认取消已接纳任务或游戏。

下面列的是当前命令所代表的内核能力。左列方便核对覆盖，右列才是 Sprout 应调用的 API。单功能服务都位于 Core，门面上的服务属性与跨功能方法共用同一套实现。

| 能力 / 原命令 | 直接调用 |
| --- | --- |
| `version` | `LauncherClient.GetVersion()` |
| `self check-update` | `launcher.Maintenance.CheckUpdateAsync()` |
| 实例 list / show / create | `launcher.ListInstances()`、`launcher.Instances.Get()` / `CreateAsync()` |
| 实例 inspect-import / import | `launcher.Instances.PreviewImport()`、`launcher.ImportInstanceAsync(..., InstanceImportOptions)` |
| 实例 rename / relink / copy / move / remove | `launcher.Instances` 的对应方法 |
| 实例 prepare | `launcher.Runtimes.PrepareMigrationAsync()` |
| 配置读取 / 编辑 / 恢复继承 | `launcher.Settings.GetEffective()` / `Get()` / `GetOverrides()`、`SetAsync()` / `UpdateAsync()` / `UpdateSectionAsync()` / `RemoveAsync()`；实例参数是 `InstanceRecord` |
| 离线身份 list / add / remove | `launcher.Identities.List()` / `AddOfflineAsync()` / `RemoveAsync()` |
| 可用游戏版本 | `launcher.Installation.GetVersionsAsync()`，可选离线读取 |
| install / repair / plan | `launcher.InstallInstanceAsync(..., InstanceInstallationOptions)`；计划与执行沿用同一工作流 |
| Java list / available / managed | `launcher.Java.DiscoverAsync()` / `ProbeAsync()`、`launcher.JavaRuntimes.GetAvailableAsync()` / `ListAsync()` |
| Java install / import / remove / select | `launcher.InstallJavaAsync()`转发实际文件下载进度；`launcher.JavaRuntimes.ImportAsync()` / `RemoveAsync()`；选择用`Settings.UpdateAsync()`设置`javaPath`或`javaRuntimeId`，恢复自动则移除覆盖 |
| loader list / install | `launcher.Loaders.ListAsync()`、`launcher.InstallLoaderAsync(..., LoaderInstallationOptions)`；后者包含实例选择提交和活动游戏检查 |
| runtime show / operations / operation | `launcher.Runtimes.ShowAsync()` / `GetOperations()` / `GetOperation()` |
| runtime change / remove-loader / rollback | `launcher.Runtimes.ChangeAsync(..., RuntimeChangeOptions)` / `RollbackAsync()`；移除加载器用 `RemoveLoader = true` |
| 内容 search / project / versions | `launcher.ContentPlatform.SearchAsync()` / `GetProjectAsync()` / `GetVersionsAsync()` |
| 统一内容查找与最终确认 | 搜索传`projectType: "all"`，MOD/资源包/光影在同一个OR组分页，不按管理标签或实例兼容条件隐藏项目。`ReviewContentInstallAsync()`冻结版本和依赖计划；`ReviewContentFilesAsync()`逐项识别混合来源，可传明确类型选择。最终确认后用`ApplyContentReviewTask`或`ApplyContentReviewAsync()`执行；清单过期、来源变化和未解决项明确拒绝，批量部分成功保留逐项结果 |
| 内容 list / analyze / recover | `launcher.ListContentAsync()` / `AnalyzeContentAsync()` / `RecoverContentAsync()` |
| 内容 add / enable / disable / remove，含批量 | `launcher.AddContentAsync()` / `SetContentEnabledAsync()` / `RemoveContentAsync()`；接收文件/来源列表，保留逐项结果与部分失败语义 |
| 内容 pin / unpin / policy / detach | `launcher.SetContentPolicyAsync()` / `DetachContentAsync()` |
| 内容 identify / install / update / plan | `launcher.IdentifyContentAsync()` / `InstallContentAsync()` / `UpdateContentAsync()`；使用 `ContentWorkflowOptions`，运行兼容条件由 Core 推断。Identify可用`ExpectedProjectId`及`VersionId`在写来源前核对期望项目/版本，不一致报`CONTENT_IDENTITY_CHANGED`，保留原文件和跟踪信息 |
| 已安装内容冻结更新审核 | `launcher.ReviewContentUpdatesAsync(instanceId, fileNames, options)`逐项保留无来源/锁定/失败结果并合并根更新与依赖；最终通过`ApplyContentReviewTask`或`ApplyContentReviewAsync()`执行精确计划，保留禁用状态。兼容筛选与实际运行上下文快照分离；`ExpectedLockFingerprint`可在选根后重新审核前拒绝策略漂移 |
| 缺失依赖整组修复 | `launcher.PlanDependencyRepairAsync(instanceId, sessionId)`自动生成完整review；用户最终确认后执行`RepairDependenciesTask(instanceId, reviewId)`或`ApplyDependencyRepairAsync()`，不逐项选项目或版本 |
| 资源包 list / select | `launcher.GetResourcePacksAsync()` / `SelectResourcePacksAsync(..., ResourcePackSelectionOptions)` |
| App 材质使用 / 优先级 | `SetResourcePackUsageAsync()` / `MoveResourcePackAsync()`；读取实际选择，保留内置与未托管条目，实例运行中拒绝修改 |
| 光影 list / select | `GetShaderPacksAsync()` / `SelectShaderPackAsync(..., ShaderPackSelectionOptions)`；发现实际配置归属、单选或停用、只读计划、指纹与旧文件备份；不保证游戏已渲染 |
| 世界管理 | `launcher.Worlds.ListAsync(instanceId)`读取真实NBT/图标路径；`ListBackups()`、`ListRecovery()`和`GetDirectory()`提供只读入口。写入/归档使用`ManageWorldAsync(instanceId, WorldAction, world, source, destination)`或typed `ManageWorldTask`，共用实例活动检查、游戏目录租约及Java session.lock保护。Remove自动保留已校验ZIP；Compress通过source传入恢复ID转换旧目录，Recover兼容两种存储格式并支持world指定新文件夹名 |
| 实例截图 | `launcher.Screenshots.ListAsync()`返回`ScreenshotCatalog`，`GetFile()`支持按文件名读取或重验已显示的`ScreenshotFile`；`GetDirectory()`只解析路径，`EnsureDirectory()`仅供明确打开目录。`RenameAsync()`保留扩展名且不覆盖，`ExportAsync()`原子发布副本且拒绝同名目标，`RecycleAsync()`以Windows Shell真实回调拒绝永久删除；`DeleteAsync()`仅供调用方明确确认的直接删除，成功项无destination，不能作为回收失败的降级。修改按截图目录租约协调、核对选择路径/长度/修改时间，不套用世界锁或游戏活动禁用。批量`ScreenshotBatchResult`保留逐项结果和取消，已生效的删除不回滚；`ScreenshotJsonContext`提供源生成元数据。App负责默认查看器、剪贴板和缩略图，业务与CLI/MCP execute共用Core；没有世界分组或游戏侧采集 |
| 游戏设置 get / set / apply / discard-pending | `launcher.GameSettings` 对应方法，传入实例 `GameDirectory` |
| 本地整合包 inspect / import | `launcher.Packs.Inspect()`、`launcher.ImportPackAsync(..., PackWorkflowOptions)` |
| 整合包状态 / prepare / complete / export | `launcher.Packs.IsManaged()`只检查登记，`GetState()`读取已保存声明/缺件；`launcher.InspectPackPreparationAsync()`只读检查当前运行环境选择、必需文件及Java；`launcher.Runtimes.PreparePackAsync()`、`launcher.Packs.CompleteAsync()` / `ExportAsync()` |
| 整合包 review / apply / update / rollback | `launcher.Packs.ReviewAsync()`、`launcher.ApplyPackUpdateAsync()` / `UpdatePackAsync()`、`launcher.Packs.RollbackAsync()`；默认应用和更新包含运行环境/Java准备与失败/取消补偿，`consistency`区分文件可用和发布一致 |
| HMCL分发检查 / 历史 / 解绑 | `launcher.Packs.ReviewDistributionAsync()`暂存精确review，`GetOperations()`读取当前事务及读取警告，`DetachSourceAsync()`保留文件与来源证据、停止隐式来源更新；显式归档review/apply可重新接回 |
| 在线整合包 install / review-online / update-online | `launcher.InstallOnlinePackAsync()` / `ReviewOnlinePackAsync()` / `UpdateOnlinePackAsync()`；首次安装复用自动准备，更新与本地包使用同一失败/取消回滚策略 |
| 在线包候选与通道 | `launcher.OnlinePacks.GetVersionsAsync(project, channel, ct)`返回真实发布，`SelectAsync`冻结具体归档元数据；`PackWorkflowOptions.Channel`与内容通道共用约束，明确版本可附通道提示但不被猜测拒绝 |
| backup list / inspect / create / restore / rollback / delete | `launcher.Backups` 对应方法 |
| server list / show / add / remove / status / resolve | `launcher.Servers` 的 `ListAsync` / `GetAsync` / `AddAsync` / `RemoveAsync` / `StatusAsync` / `ResolveAsync` |
| server diagnose | `launcher.ServerConnections.ReadAsync(sessionId)`；有界客户端日志证据和实际游戏活动分开，未知保留 `joined=null`，不是入服检测或通用游戏控制桥 |
| server bind / unbind / set-instances | `launcher.Servers.AssociateAsync` / `DisassociateAsync` / `SetInstancesAsync`；只改收藏关联，最后一项原子提交完整集合及首选 |
| 服务器专用客户端归属 / 释放 | `launcher.Servers.SetClientOwnerAsync(instanceId, serverId?, preferred, ct)`串行登记实例归属和收藏选择，发布失败补偿且不移动文件；`InstanceRecord.ServerId`非空表示专用客户端，null为普通本地。移除收藏会释放客户端归属而不删除数据 |
| server connect / launch / launch plan | `launcher.CreateServerLaunchAsync(addressOrFavoriteId, instanceId, options)` 返回既有 `LaunchTask`；交给 `Tasks.StartAsync` 或直接 `LaunchAsync`。`LaunchOptions.ServerAddress` 是已确定的原始地址，不再解析成收藏 ID；`Server` 保留旧地址/收藏兼容入口 |
| 默认启动目标冻结 | `launcher.CreateLaunchAsync(instanceId, options)`解析显式或所属服务器，`Tasks.QueueAsync`在持久化前复用；专用客户端默认连接所属服务器，普通本地不从关联反推。`InheritServer=false`禁用默认继承但不取消显式地址；显式World同样不继承所属服务器。后续修改收藏、归属和身份不改已排队请求 |
| process list / show / stop | `launcher.Sessions.List()` / `Get()` / `Stop()` |
| logs / logs events | `launcher.Logs.ReadAsync()`、`launcher.LogEvents.ReadAsync()` |
| logs export / bundle、实例与会话诊断 | `launcher.Maintenance` 的 `ExportLogsAsync()` / `ExportBundleAsync()` / `DiagnoseAsync()` / `DiagnoseSessionAsync()` |
| logs launcher export | `launcher.Maintenance.ExportLauncherLogsAsync()`，无需游戏会话的启动器活动日志ZIP |
| 原始错误与辅助诊断 | `launcher.Maintenance.DiagnoseLaunchAsync(instanceId, sessionId?, jobId?, ct)`；核对目标归属并复用原始日志，`LaunchProblem.Describe`分开提供原始错误与可选分类 |
| 外部崩溃报告诊断 | `CrashReportService.AnalyzeAsync()` |
| cache status / prune | `launcher.Cache.StatusAsync()` / `PruneAsync()`；真实回收仍检查数据所有权与活动任务 |
| job list / show / cancel | `launcher.Jobs.List()` / `Get()` / `CancelAsync()` |
| 新任务开始 / 执行 / 恢复 | `launcher.Tasks.StartAsync()` / `QueueAsync()` / `RunAsync()` / `ResumeAsync()`；使用 C# 任务对象 |

内容列表默认保留完整身份读取。App展示可用`ListContentAsync(..., new ContentWorkflowOptions { IncludeHashes = false })`只读包元数据，未计算的SHA1保持null，不能把历史记录当成当前文件身份。`GetBundledModsAsync(instanceId, inventory, ct)`可复用同次展示结果，仅对匹配的内置模组补读实际身份；审核及应用独立重读和校验，不信任展示快照。原生列表内滚动并虚拟化，图标只为实际创建的行读取，不在render期间读盘。

缓存清理的`PruneAsync(olderThanDays, plan)`按相同年龄规则在执行时重新检查引用、文件变化与活动操作，并非冻结文件清单。实际结果可能与预览不同；`removed/reclaimedBytes`是执行事实，`retained/warnings`须保留供解释。warnings在实际IOException时提供`nativeError`供调用者按真实系统错误分类，不应解析英文异常猜原因。非负int范围内的极大年龄以DateTime最早值作截止时间（通常无候选），不因日期运算溢出人为缩窄输入范围。

冻结候选版本时设置`ContentInstallOptions.PreserveRetainedConsumers=true`：精确版本选择仍保留未更新消费者的依赖约束；普通显式版本覆盖保留既有行为，冲突/警告由审核给出。

命令版 `install` 与 `repair` 原本共用安装/校验实现，直接 API 也如此，不制造两个看似不同的安装器。更细的游戏目录、下载请求、日志解析等 API 仍是公开的 Core 类型；门面不是限制调用者能力的白名单。

服务器收藏的 `instanceIds` 与 `preferredInstanceId` 是最小关联，不复制另一份实例或内容来源。旧记录与未知字段保留；读取结果为独立 JSON 快照，不能让父节点所有权妨碍 CLI/MCP 结果封装。未指定实例时只选首选或唯一关联，不猜兼容版本；显式实例始终可用，不需要收藏、分发源或 Rodel UI。连接不以 SLP 成功为前置条件；确认的地址随 typed 请求保存，后续收藏同名/地址修改不重新解释旧请求。启动计划重建后仍保留同一目标，会话记录 `server` / `serverConnectionMode`，不推定真正入服。

`Servers.StatusAsync` 的原始 `status.favicon` 保留实际SLP返回值，CLI/MCP契约不变。原生首页/管理通过共享监视器消费同一API，按地址缓存状态并将可选PNG解码为图像；坏图标不把在线改成失败，查询不可用不阻断启动。SLP不证明正版会话、模组兼容或真正进入世界，也不用于猜测服务器认证方式。

`ServerConnections.ReadAsync`只归属最近可见且匹配地址的受支持具名客户端事件；没有匹配、日志截断或未知格式不能给出确定连接结论。`gameActive`仍以真实游戏目录活动为依据，断线不等于进程已退出；`Maintenance.DiagnoseSessionAsync`在服务器会话中附带同一证据。实时捕获只保留未完成凭据前缀，不以固定令牌长度滞留完整事件；该链路已用26.3公网真实微软断开复测，旧版本/模组并非自动取得相同覆盖。首页/连接Dialog现已接共享诊断与恢复路由，但完整失败及重试矩阵未全部验收。

会话诊断的`reportedErrors`按日志结构读取任意ERROR/FATAL的原始Message/Throwable，不依赖错误原因目录；非零退出还保留原始stderr。读取预算有界，范围和解析告警在`errorLogRead/errorLogWarnings`，完整保留日志仍可导出。当前文本事件即使还在写入也提供快照，连接分析和错误采集共用同一每流顺序。`currentReportedErrors`排除旧尝试/缺失连接边界的未归属错误，stderr及启动前错误明确属于游戏进程，不冒充服务器结论；`problem.reportedError/sessionReportedError`分别提供当前原文与可查看的会话原文。

`code/phase/source/evidence/action`、Java要求及注册表ID仍是可选辅助诊断，规则不可能穷尽，不作为原文显示资格或启动准入条件。原生界面只显示原始错误，不渲染匹配生成的原因、恢复建议或按分类导航修复；没有原文时如实说明。候选崩溃报告仍明确标为候选，错误日志也不等于游戏已经不可运行。

明确地址路径传 `LaunchOptions.ServerAddress` 与实例 ID，`CreateServerLaunchAsync` 也不会先读取收藏文件。该工厂将有效身份解析为具体账户 ID 或手工玩家名，并设置 `InheritIdentity=false`；所有带服务器目标的 `LaunchTask` 在 `QueueAsync` 序列化前也经同一工厂，覆盖首页、管理、CLI 与 MCP 后台入口。防止排队后默认账户变化或被删除时静默换人；显式冻结的身份不重新选择，删除已确认账户会明确失败。普通本地启动同样在创建任务时冻结有效身份，任务不保存凭据。

`Instances.PreviewImport()`只读识别来源，返回实际游戏目录、运行文件目录、来源配置、警告及`localVersions`（有标准本地JSON元数据的版本ID列表）。单一可识别版本可沿用；多profile需明确选择，多个本地版本不自动猜选。原生App导入使用`ImportInstanceTask`配合`InstanceImportOptions { Migrate = true, Copy = true }`；保留可读来源设置与未知字段，复制到本应用拥有的实例目录。Core仍保留显式`Copy=false`的关联能力，但它不是当前App导入默认行为。提交后须读取Jobs终态及实际登记；取消或失败不授权删除来源，也不自动重放非幂等导入。

HMCL/PCL关联按所选版本重新预览，不只修改版本字符串。`PreviewImport(directory, profileId, minecraftDirectory, launcherConfig, versionId, gameDirectory)`支持显式版本与游戏目录；typed `InstanceImportOptions.GameDirectory`与预览使用同一路径，确认时Core再次识别。优先读取原来源版本目录的配置，外部共享资源目录不会替换配置来源。

旧HMCL的`hmclversion.cfg`、PCL的`PCL/Setup.ini`及可选全局配置继续由Core适配；现代HMCL的`.hmcl/config/instance-game-settings.json`读取明确列在`overrideProperties`中的运行目录、自定义Java、手动内存及窗口尺寸。未解析的继承/自动策略不伪装成固定配置，可继续手工配置。发现隔离数据却缺少可读策略时返回`needsGameDirectory`及`gameDirectoryCandidates`，要求明确目录选择；配置损坏保留可读结果及警告。目录/版本编辑后UI须重新预览再确认。

复制迁入由`InstanceStore.ImportCopy.cs`实现：保留游戏目录内容与空目录，包含mods/saves/resourcepacks/shaderpacks/config/options；复制assets/libraries及选中版本的继承链文件，不把其他版本的世界混作当前实例；可用自定义Java的完整运行时一并物化到本应用的`dependencies/java`，实例以`javaRuntimeId`引用，重写已知设置路径。资源/库集合与客户端JAR由`SharedRuntimeLayout`共享，同内容复用，不同内容隔离；版本JSON保留在实例自己的`runtime/versions`。来源未知字段留作出处，不用于覆盖新的运行路径。进度通过Core现有Report/Jobs副数据管道提供扫描、复制字节及登记阶段，进度观察失败不回滚业务。

复制全部完成后才写实例登记；取消/失败只清理本次生成的实例目标目录，绝不清理来源；完整发布但尚未登记引用的共享依赖可留作缓存，不伪装为导入成功。目录链接会物化并检测循环，目标不能嵌套在来源中；复制时检查源文件变化。源缺失的元数据仍按可恢复边界处理，不填造依赖或声称可启动。

`LauncherPaths`默认根为`Environment.SpecialFolder.UserProfile/.rmcl`。App可在自身偏好保存覆盖目录（MSIX使用包LocalSettings，无包EXE使用HKCU），启动时优先使用该设置，未设置则使用默认根，不提供数据目录启动参数；更改需重启，已有数据不自动搬移。CLI的`--data-root`参数保持不变。

`Packs.ExportAsync()`按实例当前有效的运行配置解析Minecraft基础版本和已识别的加载器版本，不将安装配置ID当成游戏版本，也不沿用已切换的旧加载器声明。Forge的fmlloader坐标、NeoForge 1.20.1的独立FML版本与现代NeoForge参数使用实际发布元数据区分；未知依赖字段保留，尚无可读运行元数据的内容实例仍可沿用原包声明导出。归档排除存档、日志、Java及共享依赖；本地模组等包文件仅在调用方明确允许时打入overrides。

## 后台任务不需要自写命令分发

`Content.ListAsync(game, kind)`原始目录清单用`isContent`统一分类，mods同时保留`isMod`别名；其他项原样保留但`enabled=false`，不把配套TXT/备份/普通目录当包。App列表、计数、实例摘要、资源包选择及安装复用读取同一分类。未知元数据JAR和既有不完整归档仍可管理并警告；ZIP/旧litemod及合法包目录继续支持。目录按根`pack.mcmeta`或`shaders`结构识别，具体加载兼容性仍由实际游戏判断。

`ContentService.InspectSourceAsync(kind, source)`只读检查显式选择的包；`AddContentAsync`在复制前调用同一检查，分类不符、损坏归档或辅助文件返回`CONTENT_IMPORT_UNRECOGNIZED`，启禁用辅助项返回`CONTENT_NOT_CONTENT`。相同文件重导复用，不覆盖不同内容；发布前核对源/副本摘要，失败和准备态中断只清理通过路径/GUID/重定向检查的自有stage，发布后的异常保留可恢复日记。完整实例迁移不使用包发现清单来裁剪文件，配套TXT、配置、私人包及存档照常完整复制。

`ReviewContentUpdatesAsync`对无在线来源的本地模组复用已有扫描SHA1查询Modrinth，不逐文件重新扫描整个目录。未精确命中时按实际元数据ID／名称搜索兼容候选，以`ContentReviewItem.SourceCandidates`返回；`ContentInstallOptions.SourceSelections`及具名工具`ContentReviewInput.SourceSelections`按本地文件名映射用户选定的Modrinth项目ID。候选不是自动授权的身份，Core下载选定根包核对其声明覆盖原有全部模组ID，再冻结`LocalReplacement`和目标文件。执行在内容事务内重验原文件哈希、实际归档ID和冲突，保留启禁用／策略／备份并在成功提交时登记来源。检查／取消不写来源，哈希已匹配的文件沿用精确平台身份；已有CurseForge等来源和固定项保持既有处理规则。

依赖修复当前支持已知游戏版本的Fabric实例：结合真实加载器诊断与当前启用模组声明，自动检索Modrinth，下载候选到缓存并核对SHA1/SHA512及归档内模组ID/简单版本范围，递归补齐并按项目去重。归档内嵌依赖沿用Content分析的有界索引；不上传本地/开发包归档。歧义、不同固定版本、需替换已有版本或无法解释的版本谓词列入unresolved，整组不安装，且不影响正常手工管理和游戏启动。此能力不是通用的完整Fabric版本求解器，也不自动处理所有加载器。

规划可传`versionSelections`（Modrinth项目ID到真实版本ID），用于用户调整已自动选择的版本；每次变更重新生成并核对完整review，不能只改显示文本或沿用旧的确认资格。review节点包含平台名称、图标与依赖方，App用真实候选版本列表呈现，初始不要求用户逐项选择。

review保存在启动器数据根的`dependency-reviews`，规划不改实例内容；一次最终确认后复用现有整组下载/校验/提交与恢复机制。执行前核对实例归属、当前内容清单、运行上下文及内容锁基线，变化则要求重新规划；不会在确认后重新选择其他版本，不自动启动游戏。CLI/MCP通用execute通过`content repair`调用相同Core能力。

```csharp
var queued = await launcher.Tasks.StartAsync(new InstallInstanceTask(
    instanceId,
    new InstanceInstallationOptions { VersionId = "1.21.1" }), cancellationToken);

var jobId = queued["id"]!.ToString();
var observed = launcher.Jobs.Get(jobId);
await launcher.Jobs.CancelAsync(jobId, cancellationToken);

// 等原尝试进入 canceled/failed/interrupted 后，确认可恢复，再创建下一次尝试。
var resumed = await launcher.Tasks.ResumeAsync(jobId, cancellationToken);
```

Core的typed任务请求以[LauncherTaskRequest](../src/Rodel.MinecraftLauncher.Core/Application/LauncherTaskRequest.cs)为准：覆盖游戏启动、游戏/加载器/Java安装、Java导入/移除、运行组合、包/迁移准备、内容恢复/安装/更新/审核应用/依赖修复、包导入/补件/更新/应用、在线包安装/更新、实例导入/复制/导出/移除、备份/恢复及世界管理。它用source-generated JSON保存请求并重新调用同一套直接API；没有保存一段要求UI自己解析的命令。

设置`GetOverrides()`只读原始覆盖，不解析托管Java或合并默认值；损坏Java引用仍可修复。`UpdateAsync()`在单次租约中重读最新文件、只修改指定字段并原子替换；`UpdateSectionAsync()`对指定对象分区做相同处理，保留未知子字段。`RemoveAsync()`恢复继承，写null仍保留既有`SetAsync()`语义，不等同移除。托管Java与路径二选一，批量请求包含两者时拒绝且不改文件。

App的全局编辑由`CorePreferencesController`合并500ms内的有效输入并串行调用上述API；只清理实际提交且未被新输入替换的字段。无效输入保留在编辑器，不阻断无关有效字段；分类/搜索切换不丢输入。关闭窗口前Flush处理有效变更，实际写入失败保留窗口和错误；恢复默认自动移除覆盖。首页与实例页的运行配置共用窗口级Presenter及`InstanceManagementController`自动保存队列，轻触外部或关闭主窗前等待包括新编辑在内的保存与首页刷新；旧快照只推进对应Baseline，不覆盖新输入或转写当前首页选择。未结束的玩家名和内存输入不落中间值，独立有效项仍可保存；读取失败且未建立草稿时可直接关闭。此自动保存是App编辑协议，CLI的`config set`仍逐次提交；复制、恢复、删除等实例任务保留实际确认。

Java程序/目录导入仅登记外部引用，归档导入才解压为托管运行库；不是实例迁入的完整复制语义。托管下载和归档导入均持久化相对`runtime`目录的登记，随整个数据根移动后重新解析；返回结果仍为当前绝对路径。`ImportJavaTask`、`RemoveJavaTask`执行原服务，失败/取消由Jobs记录；非幂等导入不自动恢复重放。

运行库列表只逐项Probe，不重新全局发现；同ID当前布局优先于旧布局，与路径解析一致。登记ID必须匹配实际目录，坏登记返回该目录的真实ID与警告，不探测另一记录的程序，也不阻断其余列表。

移除拒绝仍被实例、全局默认或活动游戏引用的运行库。托管`runtime`、未完成的`runtime.partial`与自有安装元数据永久删除，不生成`.removed`或返回`recoveryDirectory`；外部Java只移除登记，原文件不变。登记最后删除，文件占用导致删除失败时仍保留登记供重试；登记目录本身被链接重定向时拒绝遍历删除目标，避免把外部目录当作自有文件。未知或关联文件不递归清除，剩余目录或目录清理失败通过结果`warnings`明确报告。不自动清扫实际磁盘上既有的旧`.removed`目录。App的确认文案、CLI帮助和typed任务均采用同一语义，没有Java恢复入口。

`JavaRuntimes.GetAvailableAsync()`增加`source=network|cache-fallback|cache`及必要的`warning`。网络失败可使用缓存，但明确说明来源与原因；新目录已获取而缓存写入失败时，仍返回新目录及警告，不回退成旧版本候选。显式离线不请求网络，无缓存时返回空候选与说明。App下载Dialog显示该警告，不将缓存目录当作实时网络结果。

`InstallJavaTask.ExpectedVersion`及`InstallJavaAsync(..., expectedVersion: ...)`可携带目录审核时的实际版本名称；执行只接受同名目录项，目录已漂移则在创建/下载运行库前拒绝并要求刷新。省略仍保持原先按component取最新版本的CLI/手工调用语义；此参数不把显示名称当作兼容推荐。
服务器连接复用 `LaunchTask`，不另造业务执行器。

`StartAsync` 返回的是已提交的任务记录，不是完成凭证。结果、取消和错误保存在 `Jobs` 中；进度带 `jobId`，Core 自己保存 `.progress`，写进度失败不会中断业务。只有可安全重放的安装/准备/恢复类型默认支持恢复；其他操作应检查已发生的结果后显式重做。

统一任务能力不规定独立下载页。App当前在各功能任务Dialog和首页呈现相应进度；未来支持关闭Dialog后继续执行时，可先增加轻量全局入口。任务归属、取消、恢复及终态仍由同一Jobs/Tasks契约决定。

`QueueAsync` 只保存，`RunAsync` 执行，适合宿主自带的独立 worker。`GetRequest` 直接读回 typed 请求。宿主的自定义任务也可使用 `JobService` 的 `JobDefinition` 与执行委托，属于扩展途径，不是使用内置启动器能力的前置工作。

Sprout任务使用Core typed请求。CLI独立解析命令参数；具名MCP与内置AI共用Core/Tools中的强类型工具与任务提交；通用CLI后台操作仅保存当前标准命令请求，独立工作进程执行。废弃的工具任务、顶层参数记录和旧token顺序不提供兼容读取或迁移。

## 游戏监控由谁承接

`new LauncherClient(paths)` 默认使用 `InProcessSessionHost`：Core 自己启动和监控 Java、捕获日志、保存退出结果。只引用 Core 的 NativeAOT 消费工程已实际跑通 Java 8/21 子程序。

进程内监控需要应用进程保持存活。Sprout 若关闭窗口后仍驻留，可以继续监控；若整个应用退出后还要持续收集游戏日志，可实现 `ISessionHost`，使用 **Sprout 自己的 worker**。这个接口接收 `LauncherPaths` 和 `sessionId`，不要求 CLI 参数或 CLI 可执行程序。CLI 的独立监控实现仅属于它自己的宿主选择。

`LaunchAsync` 的等待取消不等于停止已经运行的游戏。要停止游戏，调用 `Sessions.Stop()`；避免因为关闭一个设置页面而杀掉玩家的会话。

`Sessions.Get/List`在Windows上实时核对游戏PID/启动时间及确证子进程的可见主窗口，`SessionStatus.Readiness`区分`process_started`与`window_visible`，观察错误通过`WindowObservationWarning`明确返回。可见窗口不是主菜单、世界或服务器就绪证据；不因创建Java进程就自动隐藏App。

`LaunchOptions.GameLanguage`是可选Minecraft地区语言代码，`LaunchPlan.GameLanguage`报告解析后的值，CLI/MCP省略时保持玩家语言。App提供当前应用语言对应的`zh_cn`/`en_us`/`ar_sa`。计划不写游戏选项；实际会话持有游戏目录准备租约，在Java启动前调用现有GameSettings文档编辑/原子替换，只改`lang`，保留编码、换行与未知字段。旧资源索引的语言文件名决定`zh_CN`等实际大小写，不靠UI重写整份游戏设置。游戏选项只读、占用或编码不能安全往返时保留原文件、继续启动，通过`GameLanguageWarning`与会话warnings明确报告，App在启动状态中展示，不把语言同步变成运行门禁。

首页接入后，会话额外保存`instanceId`和`jobId`，宿主可将任务与会话精确关联，不用以当前选中实例猜测归属。默认`InProcessSessionHost`在Java创建前的租约、资源准备和进程启动边界检查取消，取消保存为终态`canceled`；Java已启动后继续监控，不因启动等待被取消而终止游戏。窗口隐藏到托盘时仍须保留同一Core客户端与宿主。

`SessionService.WaitForStartAsync`保留单次30秒观察上限；`LaunchAsync`遇到仍为`queued`/`preparing`的返回值会继续等待，不提前完成typed任务。长时间准备期间`Jobs.CancelAsync`仍可用；宿主不要把一次观察超时当作启动完成。

同一轮多实例状态观察可将刚读取的`Sessions.List()`传给`SessionService.HasActiveGame(game, observedSessions)`，避免逐实例重读全部会话；跨宿主活动登记仍逐次检查。该快照不能跨观察轮次或修改操作缓存。修改前的普通`Sessions.HasActiveGame(game)`仍重新读取当前会话，不复用界面快照。

## 共享皮肤编辑内核

皮肤编辑本身由独立工程[`Rodel.Minecraft.Skins`](../src/Rodel.Minecraft.Skins/README.md)提供，是遵循Minecraft规则的图像编辑库，不依赖Core、CLI、MCP、Sprout或AI。独立客户端可只引用该库；AI意图理解和工具编排属于上层。

`Core.Skins.SkinService`是启动器存储适配：管理数据根下的文档、跨进程租约和文件发布，不重写图像编辑逻辑。AI由皮肤专用MCP映射相同CLI入口后调用该适配，短小绘图事务无需新增后台任务体系。

```csharp
using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;

var skins = new SkinService(paths);
var id = await skins.CreateAsync(SkinModel.Slim, cancellationToken: cancellationToken);
var document = await skins.EditAsync(id, expectedRevision: 0, edits:
[
    new SkinEdit
    {
        Tool = SkinTool.Pencil,
        Surface = new SkinSurface
        {
            Part = SkinPart.Head,
            Layer = SkinLayer.Base,
            Face = SkinFace.Front
        },
        X = 2,
        Y = 3,
        Color = "#FFFFFF"
    }
], cancellationToken: cancellationToken);
await skins.ExportAsync(id, outputPath, cancellationToken: cancellationToken);
```

适配层的`Read`返回独立库的typed `SkinDocument`，`Describe`返回不含历史像素的`SkinSummary`及UV矩形；`EditAsync`/`RestoreAsync`持有跨进程文档租约并调用库检查修订号。独立库客户端可直接调用`SkinEditor.Apply`/`Restore`/`Sample`；启动器Sprout继续通过Core功能服务承接持久化与候选业务。批次先在副本完成，出错不改调用方像素。UI线程、输入采样和工具选择归客户端，业务代码不依赖控件。

文档结构、历史、图层ID与背景alpha约束归`SkinDocument.Validate()`，不是UV布局的职责。`Apply(..., cancellationToken)`在动作及栅格循环中观察取消，Core适配会传入调用方token；`Restore`也接受末位取消参数。CLI参数JSON解析留在命令组，Core不保留请求字符串解析入口。破坏性Cut/Move/翻转不接受半透明或透明锁设置，像素转移规划先完成，错误及取消不发布部分工程。

独立库的`SkinLayout.Face/Faces`按文档尺寸定义Classic/Slim的64/128各面坐标；`SkinGeometry.Quads`、`Pick`及模型边传输共享相同几何，拾取返回真实表面与连续笔划坐标。`SkinPreview.Render(document, SkinViewOptions, cancellationToken)`生成有深度和透明合成的PNG，内外层和身体部位可过滤；`SkinService.PreviewAsync(id, outputPath, options, overwrite, cancellationToken)`仅发布PNG，不包含HTML或浏览器客户端。`SkinPng.Decode/Encode`负责标准PNG和旧格式转换，导出自动合成可见绘图层。

`SkinDocument.Layers`是独立绘图层，不等于MC内外层。`SkinEdit.Tool=Layer`的`SkinLayerChange`与像素动作共用`Apply`事务和完整撤销快照；`SkinLayers.Describe`返回设置，`Pixels`返回可见合成或指定层副本。背景Base保持不透明，附加层可透明。`SkinSelections.Create`提供几何/颜色mask，`SkinEditor.Capture`返回带mask的`SkinPixelBlock`供跨面/跨文档粘贴；UI自己负责系统剪贴板和指针射线转换。

`SkinEditor.CopyPartToOpposite`复用同源身体镜像映射及Copy/SampleMerged/Replace，将单个胳膊或腿的六面可见合成RGBA写入对侧指定绘图层；底层始终包含，`includeOuter`决定是否同时复制外层。图层结构、其他层、来源及透明蒙版不变；透明像素也替换目标，背景Base的不透明保护仍有效。Core的`SkinService.CopyPartToOppositeAsync`持有同一文档租约、修订检查与原子发布，`SkinEditingSession.CopyPartToOppositeAsync`冻结当前选中层并沿用草稿/保存/可绘制守卫，整次操作一个撤销步骤。CLI/MCP既有`skin edit`批次可表达相同Copy动作，不新增同义命令。

原生镜像通过`SkinEdit.MirrorParts`传入当前可见部位。Body模式逐个表面判断同类对侧是否在集合内：在则双肢体对应镜像，不在则沿当前面法线反射到该肢体的相对面（前/后、内/外、顶/底），而非同面左右列取反。直立面翻转X、顶底面翻转Y以保持真实空间对应；同源`SkinGeometry.MirrorPoint`同时返回目标面与像素。头、躯干只在自身左右对称，不使用可见集合包围盒。空缺此字段保持原全身配对，Part模式保持原显式左右对称；源目标和选区规则不变。预览、画笔、橡皮、填充、像素转移和同一笔内跨部位采样共用该解析，草稿与延后笔画冻结集合，最终提交返回独立数组；CLI/MCP沿用同源JSON/schema承接。显式整肢复制仍使用身体左右映射，不受单肢体相对面模式影响。

`SkinEdit.PixelMask`是可选的Size×Size贴图行优先bool数组，null表示不额外限制，全false表示没有可写像素；它与旧面内Selection相交，并严格裁切镜像的最终写入，不扩展填充原有面/连通范围。旧Selection仍保留反射到镜像侧的契约。Copy/Move/Cut/Paste/Flip不接受PixelMask，Layer动作忽略该绘画参数。SkinStrokePreview及Core编辑会话冻结其数组，ToEdit返回独立副本；选区本身由UI持有，不是持久内容或撤销步骤。

`SkinEdit.AtlasTransfer`为独立的绝对贴图Move/Cut/Paste契约，不与旧面内Selection/Source/PixelMask/Mirror混用。CaptureAtlas捕获活动实际绘图层的最小包围矩形，SkinAtlasBlock/SkinPixelBlock保留精确RGBA及mask（已选透明点不同于未选空洞）。规划先冻结来源、验证全体清源/落点，再修改副本；重叠以原始来源覆盖清源。Move不允许任何源点落出合法区域；Paste按原像素1:1裁到目标真实Base/Outer及可选DestinationMask，不隐式缩放或重映射。背景Base清源要求显式不透明ClearColor，其他表面清透明；背景Base粘贴非不透明像素整笔拒绝，不改alpha或偷换合成模式。全裁剪/零偏移内容未变时无历史。普通CLI skin edit的源生成JSON直接承接此typed字段。

`SkinJsonContext`提供NativeAOT兼容的格式3完整内容文档、绘图/图层动作和观察请求元数据。库返回快照、PNG字节、几何或剪贴像素；Core适配层才将文档与输出用临时文件发布，已有导出路径需要显式覆盖。手动编辑UI由Sprout客户端负责，不是库内嵌网页。旧开发文档格式不隐式迁移，既有PNG仍可导入；具体边界见[库说明](../src/Rodel.Minecraft.Skins/README.md)。

独立库源码按Models/Editing/Geometry/Imaging/Serialization组织，公共类型仍在`Rodel.Minecraft.Skins`命名空间，调用方无需因文件重组更改引用。文档拥有背景层标识及初始像素/图层寻址规则，图层合成由只读图像能力复用；`SkinEditor`仍为事务入口，不改变修订、撤销、取消或序列化契约。

Sprout已接原生3D/平面/UV编辑、完整图层与统一AI辅助，不包含正版认证/账号上传、模组自动安装或运行中皮肤同步。正式回归见[测试说明](../test/README.md)，操作格式见[CLI](CLI.md#皮肤绘图与3d预览)；回归通过不代替原生或模型验收。

## 可编辑皮肤工程

工程文件统一使用`.rmcs`（Rodel Minecraft Skin），库内路径为`skins/<id>.rmcs`。新保存采用`rodel-skin-project/1`压缩容器：源生成的`project.json`保留格式3文档及完整历史元数据，像素按SHA-256引用共享的压缩小端uint32纹理；读取校验摘要并返回独立数组，不让可编辑内容与历史相互污染。已有当前格式3纯JSON可读、成功保存后写为容器，不减少历史或恢复格式1/2；公开JSON及独立库序列化契约不变。

`LauncherClient.Skins`提供同一数据根的`SkinService`：Create/Read/Describe/List、Rename/Clone、Edit/PreviewEdits/BrushFootprint、Import、History/Restore/RestoreTo及PNG输出。`SaveProjectAsAsync(id, expectedRevision, outputPath, overwrite, ct)`另存完整可编辑工程及现有撤销/重做，来源不变，不携带执行回执或独立AI会话；同源目标、修订漂移、未确认覆盖及取消明确拒绝。编辑会话的`SaveProjectAsAsync`使用已观察修订并拒绝活动草稿/保存，与原生选择器及CLI共用服务。预演不保存，事务使用expectedRevision；Describe中的revision/modifiedAt可供宿主有界刷新，跨进程自动订阅尚未接入，原生交互由编辑会话发布有界渲染快照。工程名称不进入绘图撤销，背景层名称及模型/尺寸/像素/图层进入完整内容历史。

原生编辑使用`OpenEditor`返回的具体`SkinEditingSession`：内存快照Render、Begin/Append/Cancel、CommitAsync、ApplyAsync、RestoreAsync、History及RestoreToAsync，不在pointer move重读工程或验证历史。Render返回独立合成像素与同源Quads/Pick/PickAtlas；BrushFootprint复用同一栅格规则生成含镜像的轮廓足迹，不改像素、不重复验证历史。FillFootprint在会话锁内按真实目标层像素执行同一填充算法的只读分支，不用合成图推测连通范围，不增加revision或撤销记录。SelectLayer只选择随后绘制的目标，不增加revision或历史；隐藏、锁定或零不透明度图层仍保持选中，CanPaint为false，绘制请求被拒绝而不会改写其他层。图层添加、删除、改名、显隐和排序通过ApplyAsync的Layer事务进入同一历史；新增选中新层，删除后选择相邻有效层，撤销保留仍存在的选择。LayerPixels复制实际层RGBA供缩略图使用。预演保持原revision，一笔增量处理coverage后仅在提交时按原revision调用正常EditAsync。保存冲突保留草稿，不能自动重基或覆盖；调用方可放弃并重新打开最新工程。会话同步短期内容/显示缓存发布，磁盘与完整事务工作应从UI线程移出。Templates提供稳定内置模板ID，宿主负责本地化显示。

History在会话锁内原子返回revision及时间线元数据，不重读磁盘；RestoreToAsync必须携带显示条目时的revision，拒绝本地或外部过期版本，成功才Publish完整像素/模型/尺寸/图层，保留未来历史且当前项为no-op。

PNG交互先用SkinService.PrepareImportAsync冻结有界原始字节，SkinPngImport.Decode按所选模型创建独立预览，不在确认时重读来源。已有会话ImportAsync一次替换完整内容并进入同一撤销历史；无工程时CreateImportedAsync原子创建同尺寸/模型的空白基线及一条导入历史。失败保持当前快照，取消不写工程。SkinEditingSession.ExportAsync导出当前已提交会话快照，原子发布合成PNG；指定`size: 64`可将128工程以像素中心最近邻采样导出64副本，默认保留原尺寸，两者均不改变工程或历史；不重新读取可能被外部修改的同ID工程，不改revision或历史。

SkinPngImport.DecodeLayer读取方形PNG原始RGBA，不执行整张皮肤的Base补实或旧格式转换。SkinService.ImportLayerAsync与SkinEditingSession.ImportLayerAsync接受同尺寸64/128素材，新增一个顶层并原子提交一条import-layer历史，保留原有模型/尺寸/层；会话成功后选中新层，失败保留原状态。SkinService.ExportLayerAsync及会话同名方法输出指定层原始RGBA，不合成、不乘层透明度、不受隐藏/锁定影响；Core统一负责覆盖确认参数和原子发布。

`SkinEditingSession.HasDraft`涵盖笔画和转移；HasStroke保留笔画专属语义。BeginTransfer/UpdateTransfer在冻结基线上维护SkinTransferPreview，Render/LayerPixels向各视图发布同一预览，TransferMask复制实际目标足迹。CommitAsync经既有持久化租约提交一次，失败保留可重试草稿；Cancel恢复已保存内容。CaptureAtlas只读复制当前实际层，不增加revision/历史。活动transfer期间不允许第二个编辑、导入、图层变更、导出或历史跳转；系统剪贴板与选区观察状态仍归App。

`Skins.Proposals`提供CreateScope/ReadScope/RevokeScope、Propose/Describe/ReadContent、Accept/Reject、Receipt及受管Preview。默认宿主冻结可信文档/图层/表面/mask授权，再将scope绑定到AI；模型传来的Selection只是绘画参数。原生极简编辑器显式使用`SkinScopeRequest.DirectEditing=true`授权当前整份文档，不与局部/只读限制混用；此模式Propose立即调用同一原子接受逻辑，无前台审批，返回accepted状态及真实回执。CreateScope和ReadScope返回与实际模式一致的有效图层权限：直接编辑包含新增和复制图层，`AllowLayerChanges`为true；已有会话读取同样遵循此语义，无需重建工程或授权。内置AI在safe回合直接使用该宿主授权执行绑定的皮肤工具，不再要求通用操作审批，不修改会话模式、全局默认或普通聊天许可；只读回合、撤销授权、几何变化与跨工程请求仍拒绝。默认受限候选契约不变。Core仍在写租约内重查修订、几何与授权；失败保留旧工程，重复接受查询原回执，不重放像素。

`AiChatCreate.SkinScopeId`将统一AI会话绑定到一个宿主授权，和InstanceId互斥。内置AI直接调用Core受控工具，外部MCP由独立CLI包装同一套契约；授权撤销、intent、未知效果及同clientKey恢复沿用统一AI，不新建模型SDK或Agent循环。系统上下文传递真实授权和直接/候选提交模式。Sprout复用共享AiChatController发起、恢复、停止及观察；直接提交成功后有界重读工程，同步3D/平面/UV与撤销历史。操作区提供模型、参考图、输入、新会话和发送/停止；聊天区保留全部消息、思考与流式内容，输出之间的连续工具合为一个原生Expander，展开后按真实参数展示读画位置、图层、形状、颜色、可见性变更及实际结果；绘图没有额外审批。每个工程只持有当前会话，新会话须先确认停止活动回合并删除旧对话，保留皮肤、图层及绘图历史；UI在删除前结束旧观察器，删除失败恢复原会话观察，替换后重新读取已提交作品。没有通用launcher、任意文件路径或跨工程工具授权；真实模型与原生验收分别判断。

`AssistantService.Images`提供`PrepareFileAsync`/`PrepareAsync`冻结用户显式选择的图片、`Describe`/`ReadBytes`读取原快照，PNG/JPEG/WebP按原字节保存而不缩图。CLI的`PrepareSubmissionAsync`按会话/clientKey/槽位冻结，受同源租约保护；同键重发复用相同附件，内容变化显式冲突。`AiChatSend.ImageIds`每轮最多3个，Core在提交时冻结元数据，重试必须保持原图；`AiChatRecord.Images`只返回typed元数据，完整MEAI图像内容留在持久历史。跨provider采用标准`DataContent`，不把base64文本当图像token，不因未知视觉能力设置准入门槛。

工具预览图不计入玩家每轮最多3张的显式参考图限制。Core保留实际MCP图像字节并构造标准工具图像消息；公开`AiChatRecord.Intents`及效果检查只呈现图片类型/长度等元数据，原始intent的私有读取用于恢复，不能把公开精简记录写回私有状态。当前工具文本压缩保留图像，`conversation_history_read`精确读取某messageIndex的首页会将该条原图附回模型输入，不要求模型解析base64或取得文件权限。

`AiChatSend.MaximumOutputTokens`为可选覆盖，不再默认为4096，也不设置65536的人为上限。未提供时沿用保存值或统一能力解析所得provider/models.dev输出容量，未知则省略；计量和压缩仍为建议，不把估计量当准入门槛。`AiChatRecord.FinishReason`保留实际结束原因，输出截断归`incomplete`及`AI_OUTPUT_TOKEN_LIMIT`，不是网络失败；不完整工具不执行，已有操作回执保持权威。

## CLI、MCP 和 AI 的位置

起调用链统一为`AI → MCP → CLI → Core`，Sprout保持`App → Core`。原`Rodel.MinecraftLauncher.Adapters`项目已移除；命令解析、工具schema、分页和结果封装归回CLI，未发布的旧任务兼容已删除。具名MCP工具映射成CLI参数token，和通用`execute`一起调用`LauncherApplication.ExecuteAsync`，不再直接调用Core服务；不会拼shell命令或逐次启动CLI进程。

CLI通过System.CommandLine命令树统一声明typed参数、路由和帮助，每个具体命令直接绑定处理函数及后台typed工厂，不按命令名二次路由。MCP和内部调用复用真实Command/Option符号生成token，终端输入和当前后台命令请求共用同一入口；工具分页/摘要也在注册处绑定。必填、类型、范围和组合错误在业务与排队前拒绝，重复注册/别名冲突在初始化时拒绝。JobService要求存在当前request，CLI不再执行旧工具任务或规范化旧token顺序。此解析依赖仅在CLI工程，Core直接API和Sprout不需要构造命令或引用该库。

具体命令注册和符号归CLI的`Commands/Groups`功能类，根类只组合与解析；MCP引用如`LauncherCommands.Instances.List`的真实符号，不增加另一份API路由。CLI自有帮助与命令层错误通过`Strings/CliStrings.resx`及MSBuild生成的强类型资源访问，当前仅英文中性资源。资源生成和命令分组不改变Core公开API、业务JSON字段或当前任务格式。

CLI/MCP的固定外壳、分页/续查和工具输入由强类型模型与System.Text.Json源生成元数据维护，输入/输出schema从同一`JsonTypeInfo`导出。具名工具先反序列化为typed输入，再映射CLI token；不会绕过CLI直接调用Core。开放`data`、第三方数据和异构warnings不机械DTO化。Core新增`LauncherOperationException.CreateResult(Exception)`返回`LauncherOperationResult`与`LauncherOperationError`，保留原错误分类、网络信息及失败/取消的恢复data；原`ToResult(Exception)`签名与JSON字段兼容，App无需改调用方式。

Sprout的游戏管理与AI调用均只引用Core；Core可依赖内部AI实现模块，不反向引用CLI或参数解析。模型SDK/通用传输留在该子模块，CLI发布将它们与Core一起AOT编入主入口rodel-mc.exe。Assets/AiMcp中的固定原生组件是伴随工具，不是新的助手产品；不恢复旧单次ai run/任务格式，AI使用工具的通道仍消费当前CLI MCP。旧独立宿主及此前不含模型SDK的发布记录属于历史形态。

账户管理复用Core认证服务。CurseForge默认在线能力关闭，离线归档及provider扩展接口保留；游戏人物托管不属于当前契约。

[当前架构](ARCHITECTURE.md) · [正式测试](../test/README.md)

## 新建原版与安装准备

`Instances.CreateAsync(name, version)`只登记实例，不表示游戏已安装。新建实例使用schemaVersion 2的相对game/runtime路径，资源、库及客户端归档通过`runtime/dependencies.json`引用数据根dependencies；旧实例布局继续可读。`InstallationService`安装开始前分离共享集合，成功后按内容发布，原子切换引用后才回收本次私有工作集；中断保留当前引用和已完成文件。

`InstanceInstallationOptions.PrepareJava=true`使同一个安装任务在游戏文件准备后，继续发现、复用或按版本元数据准备Java，不启动游戏。显式Java优先，自动准备失败时已有可用Java保留降级信息；没有可用Java则任务失败，游戏文件仍保留。结果包含`java`选择依据及可选`javaWarning`。自动安装的Java用`javaRuntimeId`登记到实例，新装官方运行时的registration保存相对路径。`PlanOnly`仍只读且不会准备Java。

宿主应观察Jobs终态，取消/失败后对同一实例重试安装，不重复登记，也不清除共享依赖。实际可启动性继续由Launch工作流检查。App新建切片只接原版，微软登录与AI均非前提。

### 会话日志与诊断导出

`Logs.ReadAsync`读取每流1MiB的内存控制台窗口或已保存的崩溃快照；`LogEvents.ReadAsync`复用相同字节来源。Core监控宿主持有缓冲与只读当前用户管道，跨进程CLI/MCP不依赖App专属控制端点。生产者只复制内存，不等待文件或管道读者；原始令牌在进入窗口前脱敏。`firstOffset`报告被淘汰的前缀，续读仍使用绝对`nextOffset`，不把有限窗口当成完整历史。`source=unavailable`及`warning`明确表示宿主结束、读取失败或保存文件已不可用。

非主动停止的非零游戏退出将最多两路各1MiB快照原子写入数据根`logs/<session>.stdout.log`与`.stderr.log`；正常退出、主动停止不写游戏日志。落盘失败保留原退出码、原文件和独立流警告，不阻止另一条流保存。会话JSON仍记录生命周期和计数，不存正常控制台正文。受管捕获保留七天，在监控开始/运行期间每日以及读取/导出的按日检查清理；活跃或活动不明会话、链接、未知文件、游戏原始日志及用户指定的导出文件不纳入清理。

`Maintenance.ExportLogsAsync(sessionId, destination, ct)`显式导出可用窗口的原始字节，并在`session.json`的`logWindows`记录来源、范围及警告；省略启动参数与环境覆盖。无可用窗口时仍可导出元数据，但明确返回警告。`ExportBundleAsync(..., redact=true)`组合窗口、会话、诊断、活动事件和可读崩溃报告，清单标记截断/不可用，默认尽力处理常见凭据，不能保证任意秘密或本地路径均被隐藏。二者不要求游戏先退出，也不会恢复窗口已淘汰的内容。

`Maintenance.ExportLauncherLogsAsync(destination, ct)`独立导出最近7个UTC日的受控启动器活动文件及`manifest.json`，不读取游戏会话、游戏输出或配置。包含正常及最多3个轮换分段，单段快照最多16MiB，读取开始后追加的内容不扩大本次快照；过大、占用或不可读的单个来源记录警告，其余继续。仅有空清单也可导出，并报告`LAUNCHER_LOGS_EMPTY`。该入口固定尽力过滤常见凭据，不能保证任意秘密均被识别。

Core导出目标不静默覆盖，临时归档完成后发布。App在其外使用自有暂存ZIP，手工已有目标具名提示，只有保存选择器明确确认替换才覆盖目标；取消或失败不发布未完成的文件，原目标保持。设置提供独立的“启动器日志”与“游戏日志与诊断”对话框；游戏先按实例ID分组，再选带秒的启动时间及状态，同名不同实例附短ID。运行记录不是Minecraft存档列表，不增添没有执行策略的日志等级或保留天数。

### 安装恢复与活动日志

`LauncherClient.InstallationRecovery.ReadPending()`返回按实例的最新失败/取消/中断安装，含实例ID、原始名称/版本、jobId及status。宿主自己本地化显示，选中只读取状态；显式重试使用`Tasks.ResumeAsync(jobId)`，新attempt保留`resumedFrom`。此接口不枚举孤立登记或自动重放。

`LauncherClient.Activity.WriteAsync(...)`写固定字段结构化事件并返回日志是否成功；`ReadAsync(instanceId?, jobId?, limit=200)`读取最近事件（limit 1～1000）。数据根`logs/launcher`保留UTC七天，每日4×8MiB有界轮换；只写安全ID/阶段/错误码/数值，不写自由文本。日志是副数据，不改变Jobs业务终态。App详情与Maintenance诊断包复用此API。

安装结果增加`completedFiles`、`sharedReusedFiles`、`cacheReadFailures`；进度的游戏文件计数与当前文件bytes/totalBytes分开，Java阶段沿用已完成游戏计数。共享候选只有预期SHA1存在且匹配才复用，不可读/损坏回退下载；有效版本缓存与共享元数据可支持相同版本完全离线安装。Java文件/目录/相对路径通过同一ResolveExecutable处理；结果java含selectedMajor，javaWarning及版本不符需提示而非硬性阻断。

显式一次性Java路径相对于调用目录；保存到设置的相对Java路径相对于实例game目录。安装和启动均先去除外层引号、转绝对路径并将Java home解析到bin/java.exe，之后再探测。

### Java发现与变化感知

`Java.DiscoverAsync`每次重新收集显式路径、环境变量、当前/旧托管登记、Windows注册表、常见厂商Java home、用户`.jdks`和官方runtime布局。不再任意递归Program Files厂商目录；非标准位置可显式选择或登记，登记中的自定义可执行路径保持有效。候选规范化去重后最多四个并发探测，输出按路径稳定排序；只受调用方取消控制，不设置整次发现的时间预算。

`Java.ProbeAsync`优先使用一次`-XshowSettings:properties -version`读取版本、架构、位数和编码。不支持完整属性时回退`-version`并返回`JavaInstallation.Warnings`；每个子进程仍有五秒超时，取消会终止本次探测进程树并收尾输出读取，不终止游戏。

完整成功探测保存到`LauncherPaths.CacheDirectory/java-probes`，在同一缓存位置内跨Core实例和CLI进程复用。文件名只取决于规范化可执行路径，同路径更新覆盖原记录，不按版本、指纹或查询次数新增历史文件；不同路径即使版本相同也各有记录，读取时同时核对指纹与记录中的实际路径。每次使用前重新解析链接，并校验可执行文件与`release`文件的SHA256、JVM/核心库的路径/大小/修改时间以及相关环境与文化信息。更新、删除、目标重定向或环境变化使旧观察失效；探测期间发生变化时重试一次，持续变化返回不可用及重试提示，不发布不稳定缓存。包装程序报告的Java home不对应自身位置时不缓存，自动发现继续探测其实际目标。

这是按需重新校验，不是后台文件监视器，也不是每次对整个JDK做完整性审计；任意库被同长度、同时间戳替换不保证检出。未缓存失败或不完整探测。缓存读写/校验失败退回真实探测并保留具名警告，CLI、启动/安装选择报告和App Java列表保留这些证据。失效记录不要求立即删除；既有缓存维护入口识别顶层`64位十六进制.json`并按最后写入时间清理（默认30天），保留未知文件和Java本体。不增加配额、自动巡检、访问时间跟踪或全量Java文件台账。

`LauncherPaths(root, cacheDirectory: ...)`允许宿主只指定可丢弃缓存的位置，不改变实例、Java、配置、存档和备份目录。不传缓存位置时仍为`root/cache`。App没有显式数据目录时，MSIX使用包级LocalCache，EXE使用`%LOCALAPPDATA%/Richasy/RMCL/LocalCache`，均在其下的`Launcher`保存Core缓存；通过设置指定目录时，保持原来的`<数据目录>/cache/java-probes`相对布局。CLI仍沿用自身数据根的`cache`目录。不同缓存位置不强求共享观察；不自动迁移/删除已有目录，不因卸载策略自动删除玩家数据。

### 原版安装进度

`InstallationProgress.TotalFiles`在版本元数据、资源索引及适用运行库解析后提供实际文件计划总数；计划未完整前为null。`CompletedFiles`计已验证或下载完成的计划文件，`SharedReusedFiles`计其中共享复用项。适用库/客户端/日志与资源对象使用同一计划，下载字节仍表示当前文件，不是整个安装的字节总数。`LauncherClient`和typed Jobs保持这些字段，整理共享依赖后Java准备阶段也保留文件计数。取消/重试沿用已校验文件重放，每次尝试重新计算计划；不能把登记、文件100%或任务提交当作整个任务成功。

已解析的客户端、library/native及日志配置使用`network.concurrency`有界并发，后续assets复用同一设置（默认8，上限64）。元数据/索引先完成以形成计划；同批次失败或外部取消向实际下载/共享复用/资源映射传递取消，不等待其他卡住的文件超时后才失败。单文件校验、共享目标租约、恢复文件和完成计数保持原有语义。

### 实例管理任务

新增typed `CopyInstanceTask`、`CreateBackupTask`、`RestoreBackupTask`、`ExportInstanceTask`及`RemoveInstanceTask`，仍通过Tasks提交/Jobs终态读取；会检查同一实例的活动会话及其他已登记任务。复制复用完整迁入复制及共享路由，不修改来源引用，所选版本按有效设置解析。备份是全部游戏目录，恢复沿原覆盖/安全备份/回退语义；ExportInstanceTask当前明确导出Modrinth包（含本地模组与配置），不包含存档、Java与共享游戏依赖。移除实例时永久删除登记及归档清单中归属于该实例的备份；`DeleteFiles`仅控制是否另删实例拥有的game及私有runtime版本元数据，共享依赖、外部runtime不清理，其他登记仍引用私有文件时拒绝删除。备份清理成功后才移除登记，文件占用时保留登记供重试；创建／恢复备份与删除共用实例登记租约并在获锁后重读实例，避免排队任务使用已删除实例。无法读取归属的损坏归档及单文件链接不猜测删除，恢复流程的独立回退目录和记录不在ZIP备份清理范围内。删除前原子保存removal.json范围记录，文件或登记被占用后保留依据，可在重启后重试；登记删除后附带记录清理失败仅记日志，不把已经移除的实例误报为可重试失败。

`Instances.SetIconAsync(id, imageFile?)`将不超过4MiB的PNG/JPEG编码字节写入实例元数据，null恢复默认；源图片保留，复制继承图标。图像显示与失败回退由宿主处理。App已接实例管理并有真实文件/重启/游戏证据，不将API存在描述为全部验收通过。

### 本地整合包导入与保留实例

安装进度的`InstallationProgress.DisplayName`及任务JSON的`displayName`用于资源索引中的逻辑名称，`Path/path`仍为真实落盘位置；界面不应以SHA1缓存名代替资源名称，详情仍可核对原路径。此字段不改变下载摘要、共享复用或原生缓存布局。

包review的`changeSummary`只补充呈现信息：`mods`按真实且唯一的JAR声明关联update/add/delete，保留前后版本/路径及原始`files`动作；未关联条目在`files`，读取警告在`warnings`。多ID、重复身份或未知元数据不猜配，不改变`actions`的事务/摘要/应用校验。原生展示完整滚动列表与自然高度tag，不添加搜索或筛选。

`launcher.AnalyzeContentAsync(instanceId)`及CLI `content analyze`是本地只读声明分析，不需要游戏会话；原生内容页直接复用。提供方的声明别名只在已启用时计入依赖ID。结果`advisoryOnly=true`，版本范围/嵌套局限保留，不等同加载器实际兼容校验，也不触发下载、自动修复或启动准入。

本地导入审核还读取实际原生模组 ID，包括 Forge／NeoForge 同一 JAR 的全部`[[mods]]`声明，不将`provides`别名或内嵌依赖当作独立安装包。`ContentReviewItem.Replacements`冻结将替换的已安装文件及哈希，`Action=replace`需经既有`ApplyContentReviewAsync`确认执行；同批 ID 冲突或会丢弃多模组 JAR 中其他声明的部分替换不可应用。确认复用内容组事务和恢复日志，保留启用状态、玩家策略及未知跟踪字段，并清除不再对应新字节的旧平台来源。直接`AddAsync`和启用写入在内容租约内独立检查 ID，不能靠绕过审核或改名产生第二份启用包；无法识别 ID 的手工 JAR 仍可使用。

本地包检查使用`Packs.Inspect`，首次确认提交`ImportPackTask`；默认由Core连续处理包文件、游戏/加载器及Java，不启动游戏。已保留实例使用`PreparePackWorkflowTask(InstanceId, Options, Archive)`或`launcher.PreparePackAsync`，CLI的`pack prepare ID [ARCHIVE]`复用同一入口。`ImportPackAsync`与继续准备共享同一管线；Java准备与普通实例安装共用同一实现，保留显式选择、发现、下载及可用Java降级语义。旧的仅运行环境typed包准备请求已移除，不维护废弃任务格式的读取或迁移。

重新打开准备界面通过`InspectPackPreparationAsync`读取当前就绪状态，不复用旧任务终态。整合包声明了已识别加载器但实例尚无已提交或显式运行环境选择时，普通启动先继续准备，失败或取消不创建游戏进程；离线或仅计划请求返回`PACK_PREPARATION_REQUIRED`及当前准备状态，不冒充原版启动计划。显式版本选择保留，已提交的手工运行环境仍走正常文件/Java检查。`LauncherProgress.OverallPercent`表示整批条目或实际已完成阶段的汇总，不是当前文件字节百分比；下载字节及文件名仍作为独立详情。加载器阶段比例表示已完成工作阶段，不是耗时或下载字节估计。

`preparation`及进度中的`packPreparation`包含有序的`files/runtime/java`步骤、当前步骤、真实结果和状态。完成消息随步骤保留，不靠前台将一次任务拆成三次点击。完整准备遇到缺件返回`PACK_FILES_MISSING`，后续步骤不执行；运行或Java失败保留已登记实例及先前完成步骤。取消仍为`canceled/CANCELED`且携带恢复数据，不把取消请求当作已停止。已完成的已知准备若含未识别声明或兼容提示，使用`completed-with-notices`而非无条件完成，不据未知元数据全局禁用正常启动。普通内容更新、包发布一致性与游戏实际兼容性仍是独立结论。

显式offline/content-only不隐式联网准备运行环境，对应步骤标记为skipped；缺件与处理范围仍明确保留。独立的`CompletePackTask`及`Packs.CompleteAsync`继续只补件，不准备运行环境，供明确选择该范围的调用端使用。补件由Core复核原manifest，只处理缺件，保留可选选择、覆盖文件和玩家修改；MRPACK客户端unsupported及未选optional也约束同路径overrides。CF保留manualFileName/project:file映射，离线identityVerified/classificationVerified为false。`Packs.GetState`只读取持久声明/缺件，不冒充重新核验全部文件或运行就绪。

正式`PackCompletionTests`覆盖文件补齐保护、CF离线映射/错误发布版本重试、自动推进至受控运行失败、完成文件后的真实取消边界，以及未知声明不作无条件就绪或Java前置门禁。取消检查直接调用托管Core，其余流程包含真实CLI进程；不使用在线provider或游戏进程，不代替正常App、NativeAOT及真实游戏验收，也不把准备结束当成Minecraft入服。

### 网络配置与请求生命周期

`network.source`为`official`（缺省）或`bmclapi`。App统一选择器用`Settings.UpdateSectionAsync("network", changes)`只更新source并持久化，CLI复用既有全局配置命令；Core按新服务调用读取当前选择，已有传输保持其来源。预置规则仅匹配已知HTTP(S)来源及目录边界，保留原查询串，用户相同前缀规则优先。默认不启用BMCLAPI，未知source保留存储并按官方源执行。

| 下载范围 | BMCLAPI接入方式 |
| --- | --- |
| 游戏版本清单、版本JSON、客户端/服务端、资源索引及日志配置 | Mojang的launchermeta/launcher/piston-meta/piston-data保持原路径 |
| 游戏资源对象、依赖库及native | resources → `/assets/`；libraries → `/maven/` |
| Fabric/Quilt目录与profile | `/fabric-meta/`、`/quilt-meta/`；Quilt接口缺失时回退官方 |
| Forge/NeoForge目录、安装器、校验文件、依赖及Fabric/Quilt Maven依赖 | `/maven/`；NeoForge目录使用两源均支持的Maven XML，保留按Minecraft版本过滤和1.20.1旧artifact规则 |
| Mojang Java目录、运行库元数据与文件 | Mojang原路径，同一来源设置 |
| authlib-injector公开元数据及JAR | `/mirrors/authlib-injector/`；保留SHA256及Java agent检查。镜像元数据返回的镜像JAR地址还原为规范原源后走共享下载，以支持校验失败回退 |
| Modrinth内容/整合包、第三方自有下载、微软/外置账号认证、软件更新、AI模型服务 | 没有对应BMCLAPI预置规则，继续使用原来源；认证传输仍显式禁用镜像 |

来源与条款以[BMCLAPI官方文档](https://bmclapidoc.bangbang93.com/)为准。设置中可见BMCLAPI名称及bangbang93来源说明；Core直接使用其既有HTTP接口，不新增镜像转发服务。内置第一方模组随包提供，不因来源选择变成运行时下载。未实现的OptiFine/LiteLoader等不会仅因BMCLAPI提供接口而新增产品能力。

`NetworkSettings`按数据根和配置内容复用HTTP客户端。`LauncherClient.ContentPlatform`及App默认内容搜索在新请求时读取当前配置，显式注入的平台服务保持调用方选择；保存镜像后下一次搜索无需重启，已开始的请求继续使用其客户端。

共享请求处理镜像请求头/正文期限、下载读空闲超时后的有界原源回退，以及基于实际响应地址的相对重定向；用户取消不触发回退。加载器元数据沿用共享请求及保存的元数据期限。17项真实loopback HTTP/代理/文件NativeAOT夹具与正常App新镜像请求已有证据；实际SYN连接超时、安装峰值并发和完整远端安装不能从这些检查推定通过。

## 配置读取与执行限制

`Settings.GetEffective` 对失效的托管Java引用保留原ID并返回结构化 `JAVA_RUNTIME_UNAVAILABLE` 警告，不阻断读取和配置修复。显式 `LaunchOptions.JavaPath` 或安装准备的单次Java覆盖优先，且不改持久选择；无覆盖时执行给出该选择的具体错误，诊断不把自动发现的另一Java冒充已选择的Java。其它前置条件与恢复边界见[运行限制审查](RESTRICTIONS.md)。

## 账户与可选微软认证

`launcher.Identities` 提供 `List`、`GetSelection`、`AddOfflineAsync`、`SelectAsync`（null 清除当前选择）、`RemoveAsync`、`LoginMicrosoftAsync`、`RetryMicrosoftLoginAsync` 和 `RefreshMicrosoftAsync`。UI/CLI 调用同一服务；登录不进入持久化后台任务，使用 CancellationToken 取消。删除账户清理本地凭据及对应选择引用，不删除游戏内容。

`RefreshSkinAsync`只查询并提交当前角色的皮肤地址，不续期令牌、不改登录状态或选择。正版复用可用Minecraft凭据查询profile，无可用凭据或返回401／403时改查公开UUID资料；第三方使用已登记来源的sessionserver角色接口，查询不带游戏令牌。网络与格式错误保留旧资料，明确无皮肤才清除旧地址；提交时重读登记，删除、后续登录或更新优先于晚到结果。网络请求不占用账户登记锁，查询有10秒期限。App负责窗口触发、请求合并、冷却／退避及图像加载成功后的显示替换；CLI通过`account refresh ID --skin-only`复用同一Core操作。

`MicrosoftLoginOptions` 指定 Method（System/Browser/DeviceCode）、可选旧 AccountId、系统模式的真实 ParentWindow 和前台 Prompt。Client ID依次取选项、进程 `RODEL_MICROSOFT_CLIENT_ID`、Windows当前用户的同名环境变量、Core内嵌的`Rodel.MicrosoftClientId`资源；全部未配置或选中的覆盖无效时返回 `AUTH_NOT_CONFIGURED`，不静默跳过无效覆盖。正式构建通过环境／MSBuild属性注入公开OAuth ID，不将实际值写入源码，也不携带Client Secret；开发构建允许无默认ID，当前用户变量仍支持正常MSIX激活。微软账户不影响离线身份能力。System 通过 MSAL WAM，Browser 通过系统浏览器，CLI 可用 device-code；设备码只送前台回调。

凭据和 MSAL 缓存以当前 Windows 用户 DPAPI 保护。授权完成但 Minecraft 资料尚不可用时保留加密待完成缓存，Retry 不重复要求授权；`AUTH_PROFILE_REQUIRED` 表示返回权益但未创建 Java 玩家资料，不能显示为未购买。公开结果仅含玩家资料和状态，不包含令牌。启动计划保存占位符，启动前在内存替换，避免将实际令牌写入计划、参数文件或启动器游戏日志。微软账户状态为ready、缓存玩家UUID匹配且Minecraft令牌剩余有效期超过5分钟时，启动直接复用保护的会话，不请求Xbox/XSTS/Minecraft认证；临近到期、期限缺失/无效或凭据不完整时走原有静默续期。检查与必要续期共用账户租约，并发启动可复用已提交的新会话。`RefreshMicrosoftAsync`与`account refresh`仍明确执行刷新，已标为login-required的账户不会因旧令牌尚未到期而自动恢复ready。

Core在第一个账户实际登记完成后设置全局`accountId`，覆盖离线及共用凭据提交的微软/外置账户；只有待选角色或未完成授权时不设置默认。后续注册、刷新及显式清空默认后新增均不自动切换。默认设置失败仍保留已完成登记及其加密凭据以便恢复。App在真正启动任务前要求身份：无账户直接输入离线玩家名，已有账户但未选中则选择；完成后重新通过Core冻结本次账户，等待Popup关闭才继续启动，取消不落任务。Core/CLI显式手工playerName的既有能力保留。

真实 CLI 已验证 Ultimate 账户的缺少 Java 资料 → 用户创建资料 → Retry 成功 → Refresh 成功。WAM 自有应用配置、完整在线游戏/服务器链及全部原生可访问性仍需独立验证，不由上述结果推定。

原生App的首次登录、重新登录及缺少pending缓存的重试均显式选择System并传入当前窗口HWND。`RetryMicrosoftLoginAsync`可额外提供expectedAccountId，防止未完成授权恢复到错误账户；存在pending缓存时继续其原认证方式。静默续期沿用保存的System/Browser方式，MSAL账号枚举与令牌获取失败均经Core错误转换。`AUTH_BROKER_UNAVAILABLE`仅表示已知WAM运行组件初始化失败，前台才显示显式浏览器替代；其他认证失败不误报组件缺失。`AUTH_TIMEOUT`表示认证服务或登录期限超时，不能当作用户取消。原生恢复错误会重新加载账户状态并区分凭据失效、网络失败与账户不匹配。

`SkinService.OpenProjectFileAsync(path)`打开当前格式工程文件：按规范完整路径与实际DocumentPath匹配时沿用库内身份；其余合法文件（包括同GUID文件名和备份扩展名）原子复制到新ID并直接返回会话/名称。完整Document及Undo/Redo保留，旧Receipts清空，ModifiedAt为本次入库时间；来源不改写，读取/校验/发布失败不接管UI会话。工程格式仍沿用当前源生成元数据校验，不承诺未知格式兼容。
AI会话上下文：`UpdateConversationAsync(AiConversationUpdate)`支持`InstanceId`或`ClearInstance`及`ModelId`，按expected revision保存未来回合绑定与持久切换标记，不修改已有turn。`CreateConversationAsync(AiChatCreate)`的`GenerateTitle`由消费者选择是否进行有界模型标题概括；App启用，显式标题/后续手动重命名始终优先。标题失败不失败对话，且不超过回合模型调用预算。

`AssistantService.GetModelIdentityAsync(baseUrl, model)`只访问匿名公开models.dev元数据/图标缓存，返回区分ProviderId/LabId的AiModelIdentity。服务商host匹配与模型canonical归属分离，LogoSvg可空，不使用provider凭据。数据根下`ai/model-catalog`保存最后有效目录与按需logo，失效后台刷新不阻断聊天。

`AssistantService.RespondToUserPromptAsync(AiUserResponse)`回答或跳过绑定到conversation/turn/prompt的当前问题，默认进程内执行，也支持已有host通道。`AiChatRecord.PendingUserPrompt`包含冻结问题及选项，只有`awaiting_user`且原拥有者仍存活时才能首次提交；相同答案重试幂等，不同答案冲突。该API不产生操作权限或业务效果授权。内置模型工具`ask_user`由Core直接承接，MEAI函数驱动保持不变；CLI规范入口为`ai chat respond --file`。

ask_user仅提供给普通AI聊天；皮肤AI聚焦当前皮肤编辑，不提供该工具或问答卡。开启工具的普通聊天在system prompt中直接注入交互规则：需要用户信息或偏好时主动调用ask_user并等待，不能以正文问题清单替代；已授权工具能查询的事实先自行查明，操作授权仍交由既有宿主审批。无需玩家提醒，也不依赖额外skill_read才获得这条规则。

## 外置认证账户

`Identities.DiscoverExternalAsync`通过ALI读取Yggdrasil来源；`LoginExternalAsync(ExternalLoginOptions)`认证并返回`ExternalLoginResult`，多角色经`GetExternalLoginAsync`读取和`SelectExternalProfileAsync`提交，`CancelExternalLoginAsync`删除受保护的待完成凭据。公开模型只暴露玩家资料、来源及用户授权码；密码、设备私码、OAuth令牌和Minecraft令牌不进入公开JSON。独立身份由来源、服务用户及UUID共同确定，重新登录只允许原身份；添加不修改默认账户。

`BeginLittleSkinAsync`、`PollLittleSkinAsync`提供设备码流程，使用Rodel客户端1645或显式/环境覆盖；轮询遵守interval、pending和slow_down，授权后读取服务用户与角色，再转换为所选角色的Minecraft会话。当前真实设备申请被白名单拒绝，协议回归通过不等同真实授权成功。`RefreshAsync`按类型派发，`RefreshExternalAsync`只在明确凭据失效或角色移除时更新对应状态，网络失败不使账户过期。

`PrepareExternalLoginComponentAsync(localFile?)`核对官方SHA256及agent结构并发布自有组件，`GetExternalLoginComponent`检测已有文件篡改，`CanUseCachedExternalSession`只报告完整缓存的可用性。`LaunchOptions.UseCachedExternalSession`是显式选择；正常启动先刷新身份，然后在main class前注入agent和预取元数据，保留原UUID，实际令牌不持久化到会话/日志/参数文件。

`GetExternalLaunchRecovery(instanceId, sessionId?, jobId?)`读取原失败任务/会话，返回冻结的`LaunchTask`、原账户、错误和当前缓存资格。App据此呈现重试、重登、组件导入或缓存尝试；不得按当前默认账户重新创建恢复目标。会话保留结构化`ErrorCode`、清理后的`ErrorDetails`及`UseCachedExternalSession`，监视器边界不吞掉认证分类。

AI普通对话的初始模式归 `AiServiceConfiguration.DefaultOperationMode`（缺省 `allow_operations`）；`AiServiceStore.SetAccessDefaultsAsync`按配置revision原子更新模式与文件/网络默认，不改已有会话的权限或授权修订。CLI规范入口为`ai access show/set`，App只在创建新的普通对话草稿时读取默认，特殊修复/皮肤会话继续使用Core原有限定授权。

## 游戏数据桥

`LauncherClient.GameBridge`为可选实时游戏能力，复用已有`Sessions`，不增加产品服务。`GetStatusAsync`返回连接/能力；`SnapshotAsync`和`ReadRegionAsync`读取客户端同步数据，`QueryAsync`读取原生语义/NBT并标识客户端同步或本地服务器权威来源；`WatchAsync`以有界频率拉取最新样本；`EditAsync/GetOperationAsync/CancelOperationAsync`提供精确请求和收据。模型及JSON源生成上下文位于Core/GameBridge。

`RequestAiAsync(sessionId, GameAiRequest)`与Java反向`launcher.ai`共用`GameAiSession`。Core负责当前世界复核、控制台/侧栏共享的实例与存档单槽历史、可恢复清理、默认模型，以及既有`AssistantService`的发送、事件、停止、问答和范围确认；返回`GameAiState`投影，不另造模型消息/调用/用量类型。游戏入口发送时使用既有`AiChatSend.AllowOperations`提供本回合授权，不改全局或普通会话模式；`GameAiState.Mode`报告该入口的有效模式。`quick/QuickId`只区分控制台提交及精确观察，不另建会话或槽。`AiChatSend.GameContext`冻结来源session与世界，工具策略拒绝转向其他游戏；原生作弊/世界权限、文件范围及未知效果检查保留。游戏端仅转接及呈现。普通启动器回合没有该来源，不受游戏内纯文本提示影响。

`LaunchOptions.GameBridge`覆盖实例`gameBridgeEnabled`（默认开启，明确保存的全局／实例false仍优先），仅影响下次启动；Java17+ agent由Core嵌入资源提取并校验，无玩家JDK依赖。离线仅使用已校验映射缓存，缺失时禁用桥但保留普通游戏启动。运行监控拥有连接，CLI和App均可访问同一个session，不依据窗口/缓存猜实时状态。

只允许持有内置服务器的本地世界执行时间/生命、原生输入、容器点击/按钮、复活和受原权限约束的命令，Core和Java临执行前共同检查；服务器、localhost独立服务端、OP或AI允许操作均不能绕过。每次编辑绑定incarnation/epoch/dimension及operationId；queued/unknown不能当作完成或自动重发。完整限额、协议及证据见[游戏桥](JAVA-GAME-BRIDGE.md)。

`AiServiceStore.InspectLocalModelAsync(path)`只读取GGUF文件头，返回typed `AiLocalModelInfo`的标注长度和默认预算，不启动推理引擎。默认64K，已知较小标注则取该值；`AiModelUpdate.ContextLength`独立覆盖当前模型，`ClearContextLength`只恢复上下文默认并保留温度/输出设置。App滑块、CLI `ai local inspect`和`ai model save`共用此契约；元数据不可用不阻断原有配置。

SkinService 的 Cape 文档使用64×32/128×64真实矩形像素；`Describe.Height` 是实际高度。`SetCompanionAsync(id, expectedRevision, companionId)` 原子保存或清除另一类作品的只读预览关联，`ReadCompanion(id)` 返回冻结参考；不修改两件作品的像素、修订或历史。`PreviewAsync(..., solo: true)` 不加载关联。SkinSceneSnapshot只为渲染拼接两张原尺寸纹理；SaveProjectAs/Export/ExportLayer分别作用于当前工程/纹理/图层，关联作品不被写入导出PNG。准备披风导入须显式选择 `PrepareImportAsync(..., model: SkinModel.Cape)`。
