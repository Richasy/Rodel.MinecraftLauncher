# 现在的内核怎么组织

本文描述当前代码结构与调用边界。功能范围见[能力清单](ALIGNMENT.md)，正式测试入口见[test/README](../test/README.md)。

**调用边界：[能力映射](CORE_API.md)。** 产品仅有CLI与Sprout两个入口，二者只引用Core。项目依赖为CLI / App → Core → 内部Assistant子模块，Core统一拥有AI与普通业务；子模块不反向引用Core，不独立发布。内置AI经Core受控AIFunction直接执行业务；对外MCP由独立CLI包装同一组Core工具。Core不包含命令解析或MCP服务器，不引用CLI；App不依赖或打包CLI。

## 原生App的职责与工程归属

控件、状态及页面协调遵循[原生App设计原则](APP-REFACTOR-CHARTER.md)。

全App源码按[组件组织](../src/Rodel.MinecraftLauncher.App/README.md#全-app-组件组织)整理：独立行、卡片和表单按组件成文件，小型专属渲染/投影与其宿主保持内聚；大目录按真实功能流程分组，小目录不为形式统一增层。此整理不新建Store、通用Owner或转发接口，不改变Core所有权；页面仍组合具名控件，原生资源/焦点和业务协调的边界不因文件移动而迁移。

AI入口的源码导航按[App代码组织](../src/Rodel.MinecraftLauncher.App/README.md#ai目录的阅读路径)：根目录只保留页面与共享契约，Chat、Transcript、Configuration、Interaction四个粗粒度目录分别承接会话、呈现、配置与用户交互。内部命名空间不为目录分组新增转发层；独立历史行控件单独放在AiHistoryRow，面板及其列表组合留AiHistoryPanel，会话状态保持内聚，不按每个类型机械拆文件。会话操作弹窗与配置登记弹窗分别依赖自己的状态所有者，删除跨领域的AiEntryDialog分支表单。

App的`Program`只配置Sprout宿主，不提供自定义业务启动参数；独立的`LauncherApplication`通过Sprout原生应用生命周期处理启动、再次激活和窗口关闭，不在入口保留长事件闭包。数据根只在用户设置的路径与Core现有默认目录（用户目录下的`.rmcl`）之间选择，不接受App命令行覆盖；设置变更重启生效，不自动迁移数据，CLI的目录参数不受影响。共享依赖使用Sprout自带DI：`AppSettings`与`LauncherClient`由工厂注册为惰性单例，`AppImageCache`登记现有共享实例。容器在应用退出时释放其创建的设置对象，图片加载器仍由Sprout图片管线释放；窗口级图标监听由具名关闭处理器释放。皮肤编辑器管理对象只在首次点击入口时创建，后续复用，原生窗口和未提交草稿保护仍由原有编辑器流程负责。

内容页的 `Downloads/ContentDownloads` 持有在线选择与既有 `FeatureTaskController`，下载 Dialog 只订阅任务，最小化或切换页面不释放任务。右下角原生 Flyout 复用同一条目；速度由真实字节进度采样，成功/取消移除，失败保留至用户关闭。Shell 读取进行中状态并呈现退出确认，明确退出绕过托盘驻留。Core 的在线审核保存库存及来源快照，下载并行、提交串行；提交锁内允许不相干的新项目与相同精确依赖，拒绝原文件/策略变化、原生 mod ID 重叠及依赖版本冲突。其它内容变更仍沿用原有冻结与忙碌守卫，不增加 App 业务执行器。

当前包含 `Features/Servers`，由实例页“本地实例 / 服务器”共用 Segmented 切换；没有服务器独立主导航。`ServersPanel` 协调收藏、共享状态订阅及任务 Dialog，`ServerDialog` 只收集/冻结实例、地址和身份并观察既有 `HomeController` 的持久任务/会话。Core 的 `ServerService` 负责原子收藏关联，`CreateServerLaunchAsync` 返回已有 `LaunchTask`；CLI/MCP 转同一能力，Sprout 不解析命令。既有本地实例列表保留，不因分段切换重建；服务器任务纳入原来的窗口驻留与游戏开始行为。

游戏首页同样是服务器启动入口。`HomeServerController` 维护收藏快捷选择及只读客户端/身份快照，独立于 `HomeController.SelectedId` 的本地选择；首页模式和服务器 ID 是包偏好，不复制 Core 实例。`HomeServerList` 与既有本地列表各自保留滚动位置，`HomeHeroView` 共用主操作/状态呈现。明确客户端时直接提交同一 typed 任务；缺关联时与管理页共用 `ServerConnectionPresenter` / `ServerDialog`，不把页面导航当启动前置。真实会话观察、任务归属、取消及托盘行为仍由既有 `HomeController` / Shell 生命周期负责；纯 View 明确承接语言、材质和变化状态。

`ServerConnectionPresenter` 持有一份 `ServerStatusMonitor`；首页/管理只登记活动地址并订阅不可变快照。监视器复用Core SLP，按地址去重、4并发、30秒保鲜/刷新，取消旧批次并丢弃过期结果，关闭时等待所有批次后释放并发资源。可选favicon仅接收有界64×64 PNG data URI，后台用窗口图片服务解码为像素源；错误仅作为图标警告，不改变SLP在线事实。解码后UI快照移除重复base64，Core返回契约不变；同次运行短暂失败可保留最后有效图标，不持久化到游戏目录。Shell只接入图片服务和释放生命周期。

收藏关联不复制实例或包锁，不从服务器状态猜完整模组和认证要求。内容来源仍由已有实例包元数据负责；包导入/补件/更新界面及 HMCL 分发消费复用Core内容与整合包服务。

App运行资源位于自身`Assets`，背景为`Assets/Backgrounds`，不链接设计目录。`LauncherShell`仅组合窗口级背景/导航/页面/页脚，保存应用偏好并协调窗口生命周期；首页业务状态不放在Shell或多个巨大partial中。

`HomePage`组合首页控件并订阅`HomeController`。`HomeController`以不可变`HomePageState`发布Core读取和typed任务状态，负责取消/归属/异步版本保护；`HomeLaunchSettingsService`直接复用Core配置和身份API，`LaunchErrorPresenter`只负责错误Popup。UI通过`HomeHeroView`、`HomeInstancePanel`、完整行输入`HomeInstanceRow`及既有共用控件呈现；共用几何在`Layout/LauncherLayout.cs`，没有新建通用工具箱或工作流引擎。

已有实例导入由`Features/Import`承担：`ImportController`协调Core只读预览、typed `ImportInstanceTask`、取消与终态登记复查；`ImportState`保存原始预览/任务并生成显示投影。`ImportPresenter`/`ImportPopup`/`ImportFields`呈现目录、profile/版本及完整复制迁入，不实现目录识别；提交明确使用`Migrate=true, Copy=true`。完成后通知首页刷新并选择实际登记，窗口生命周期同时考虑导入任务。CLI保留默认关联及显式复制参数，不能混用两个入口的默认行为。

实例管理由`Features/Instances/InstancesPage`组合真实虚拟列表与操作入口，`InstanceManagementController`协调typed Core任务及持久结果，`InstanceManagementState`保留原始数据并本地化投影；操作Popup/字段/列表单元格分开呈现，不将文件业务放回Shell。`PopupFocusRecovery`共用布局后的焦点恢复。界面文案直接读取源生成`Resources`，不维护或透传`UiTextSnapshot`；前后语言求值、委托投影、虚拟化/回收及无障碍元数据的本地化依赖由Sprout统一承接。业务变化仍作为View的明确输入，不借此放宽对可变Owner对象的隐式读取。

`Features/Runtime`拥有实例运行环境页及实际组合审核/切换/回退；`Features/Content`拥有内容/世界列表、发现安装和已安装内容冻结更新。更新使用`ReviewContentUpdatesAsync`冻结所选根与依赖，再执行`ApplyContentReviewTask`，不在确认后重新查latest。`Features/Packs`独立负责本地包检查、可选文件和typed导入，保留实例后继续准备，不重复导入；包维护/在线界面尚未接入，后续已暂缓。

世界列表的实例绑定读取与反馈由Sprout响应式`WorldListStore`协调，`WorldListReader`读取Core结果与有界可选图标；`WorldsPanel`保留菜单、原生操作、目标可写状态及展示。同实例刷新保留前值，切实例隔离内容，条目以实例ID与目录共同标识；Store由面板独占，释放只停止UI读取，不取消Core世界任务。

发现安装的候选、目标、版本及冻结审核由Dialog独占的`ContentInstallStore`协调，`ContentInstallReader`调用实际Core查询和审核；目标/版本变化立即使旧审核和确认资格失效。候选与审核各有固定请求的LatestOperation，迟到结果不能回到当前选择。确认一次性取得包含实例、审核和版本的binding，Dialog仍通过现有ContentController提交Core typed任务；Store不执行安装事务，释放只取消本Dialog读取，不把Core任务当作UI生命周期资源。

AI历史使用`AiHistoryItem`单向只读投影传入面板、列表与行；显示名称和badge仍直接读取Resources，不向行传整个Page或可变会话。会话的`UiKey`在首次持久化时保持，Core ID继续用于持久业务及既有AutomationId；选择与菜单通过稳定UI键回到页面查找实际会话，打开菜单后绑定该对象。页面仍是旧Changed与历史投影之间的具名边界，Transcript等未迁移部分仍保留原通知链，不将此片写成全部AI Store迁移。

AI配置使用`AiConfigurationStore`，默认作用域是窗口、数据根和UI线程，不是全进程singleton。主窗口的聊天与设置共享配置和既有短请求互斥；一个皮肤编辑器窗口的各工程借用窗口配置，但分别持有聊天、scope和项目生命周期。配置拥有services/models/defaults/registrations/skills、读取及写入错误、可选品牌图读取；Core仍持有配置与修订，Store不镜像Resources或主题。默认值通过等值Computed阻断无关传播，由聊天端只播种适用的新草稿，皮肤Files/Network边界不被覆盖。可选图片使用随配置切换的LatestOperation作用域，错误不冒充聊天错误或发送门槛。设置不再订阅聊天总Changed；旧聊天通知的具名桥通过Sprout `Untracked.Run`隔离命令式sink读取，不以刷新token绕过框架依赖跟踪。

`Features/Settings/SettingsPage`拥有设置分类和搜索定位，`CorePreferencesStore`负责有效输入合并、串行自动写入与关闭Flush；保留500ms延迟、Editing分类暂缓、写前快照、未知覆盖、无效草稿和恢复继承时移除覆盖。Form/Field直接读取同一响应式State，删除逐层Changed和State副本；基线/草稿/保存快照仍按合并原子性整体发布，不宣称每字段已有独立Signal。正常关闭先显式await Flush，失败保留窗口；同步Dispose取消UI等待，DisposeAsync可额外等待保存资源释放，不隐式代替Flush。Java管理为设置子页，缓存和会话导出由各自控制器与Dialog调用Core。`AppSettings`只保存包级偏好，Core设置仍在数据根`settings.json`；Shell组合页面及关闭协调，不承接表单业务。`Features/Tasks/FeatureTaskController`复用typed任务观察，不是第二个任务引擎。

安装/操作/错误共用`Controls/Reports/DetailReportPopup`。内部二级页保持来源实际宽高，原文JSON使用独立LTR作用域，易读正文沿当前语言；复制/导出在文本背景内。焦点恢复在真正SetFocus前检查`CanTakeFocus`，避免异步关闭后聚焦已折叠祖先下的旧控件。页面行为与几何统一遵循[设计规范](../design/DESIGN.md)。

主题资源按职责放在App的Theming目录：`LauncherTheme`提供当前scope的语义颜色入口及系统调色板覆盖；`LauncherMaterials`保存缓存画刷与表面材质；`LauncherStyles`提供文字、输入及选择控件样式；`LauncherButtons`提供按钮几何与角色样式，交互状态配色由`LauncherButtonPalette`统一生成；`LauncherMenus`提供菜单、下拉与弹层外观。各自的纯值工厂基于`LauncherAppearance`求值，快照仅保存不可变`ThemeColors`与`ThemeMode`，颜色和角色逻辑不复制。资源定义可组合共享颜色和材质，但不负责注册其他资源目录；应用默认样式由Program的`ConfigureStyles`内联块明确组合，只注册主题无关的几何／字体及绘制时token，继续使用Sprout同一套默认样式注册与优先级。

Sprout的纯值`[View]`会在比较时重算旧、新两份Body，不能在内部读取live Theme。响应式`Control.Body`或框架实际追踪的条目工厂捕获外观，再显式传给View及其子View；旧描述保留旧快照，比较器据此更新同一个控件。静态主题入口仍供响应式owner及合法绘制路径使用，不在全局缓存当前外观。长期保留的Flyout chrome按实际owner Theme解析；手写保留控件须在所属生命周期更新外观，不能用重建页面代替依赖追踪。Sprout的`SPROUT074`沿同编译源码检查生成View的间接主题读取；App的`RMLUI001`只补充手写只读`IView`计算属性，不重复报告生成View。两者保留void回调、方法组、`nameof`与`Markup.Defer`的时机边界；`RMLUI002`另检查AppStyleRegistry.Add／UseDefaultStyle的实时取色，阻止在启动时冻结当前主题。这些检查不推断外部程序集的任意委托执行时机，也不代替真实窗口热切验收。

`BevelColors`仅保存固定装饰边色，绘制器在HC路径提前返回。主题偏好直接使用Sprout的`ThemeRequest`，设置生成器以枚举名称字符串持久化，不另建主题映射类。MSIX主窗口与皮肤编辑器通过Sprout.WindowsAppSDK的`UsePackageIcon`使用平台选择的包logo，由框架管理异步加载、HC刷新和原生图标生命周期；无包EXE沿用框架读取的内嵌应用图标，App不按亮度选择ICO。`Features/Wallpapers/CaptionBackgroundSampler`仅提供叠加实际壁纸/遮罩后的`ContrastBackground`提示，caption对比度、交互态与系统HC策略属于Sprout；显式颜色通道仍可覆盖默认值。不接管caption按钮、非客户区或窗口行为。背景从App的Assets声明复制，数量随图库更新，不依赖design目录。

固定颜色优先用`Color.FromHex`（alpha在前：`#AARRGGBB`）及Sprout的WinUI同名`Colors`；动态采样仍使用通道构造。`Colors.Transparent`与WinUI一致为透明白`#00FFFFFF`；`Color`不再提供预设色。渐变明确需要透明黑时使用`Colors.Black.MultiplyAlpha(0f)`，不能因入口统一而改变原有插值。主题调色板属性名为`LauncherTheme.Palette`，避免遮蔽`Colors`类型。

正式回归测试及小型合成夹具位于`test`并随Git维护。`Rodel.MinecraftLauncher.Core.Tests`直接引用Core，当前先覆盖JavaDiscovery及其探测缓存，不依赖CLI；`Rodel.MinecraftLauncher.Cli.Tests`负责CLI/MCP与跨宿主契约。辅助类归各自测试工程的`Infrastructure`，不在项目外链接松散源码；两工程各自显式引用独立的Java报告可执行项目并声明部署target，不复制同一组Core行为用例。合成报告不是实际JVM兼容性证据。产品编译和打包不依赖测试或临时验证目录。一次性脚本、报告、探针和机器数据在任务结束后清理，不维护独立会话日志。

启动器最重要的边界是：入口理解用户想做什么，功能服务完成操作，存储与进程代码负责把操作落实。游戏版本、模组来源和手工安装有很多例外，所以业务层保留未知信息，兼容建议不成为启动门槛。

## .NET工程、两个产品入口与独立游戏模组

```text
CLI ──┐
      ├── Core LauncherClient / 功能服务 ── 文件 / HTTP / Java
App ──┘      └── 内部 Assistant ── 模型协议 / SDK / 通用传输
内置AI工具 ── Core/Tools ── Core业务
外部AI ── 独立CLI/MCP ── 同一Core/Tools
SettingsGenerator ── 编译期生成 App 偏好属性、检查纯 View 主题依赖
Core SkinService ──→ Rodel.Minecraft.Skins（独立皮肤编辑库）
Core SeedExplorerService ──→ Rodel.Minecraft.Seeds ── 自有适配器 ── 精确原版Java
未来手动编辑器 ────→ Rodel.Minecraft.Skins
```

- **Core** 统一提供启动、安装、内容、实例、下载、备份、运行管理和AI API。`Application/AI`拥有配置、凭据租约、会话/回合、技能、历史、状态与执行协调。默认AI在调用进程执行，不要求CLI或助手EXE来做普通模型对话；Core经内部AI库使用在线provider或Rodel.Inference Embedded及库拥有的原生worker，不自研推理服务。
- **CLI** 直接引用Core，负责参数、命令执行、输出、后台宿主与MCP协议。具名工具与通用execute均进入`LauncherApplication.ExecuteAsync`；工具层只做schema校验、token映射、分页和输出适配，不再另写Core业务调用分支。调用为进程内方法调用，不为每次工具请求启动CLI子进程，也不经过shell。
- **Assistant** 是Core的内部辅助类库，不是公共服务。仅含提供商请求上下文、协议/SSE/SDK适配与通用MCP传输；没有Core项目引用，内部实现只向Core开放。业务路径、凭据存储和Core配置不灌入底层协议类型。CLI通过Core而不是直接引用该库。
- **同EXE后台模式** 只服务CLI的跨命令host_owned场景：Core内部管道客户端按需启动`rodel-mc.exe ai serve`，实际仍运行同一Core内核。没有Assistant.Host项目或rodel-assistant发布物。跨进程执行租约、PID/启动时间、取消信号和会话操作租约避免App与CLI同时生成、误恢复活回合或覆盖修订。MCP工具与终端AI操作复用CLI实际命令符号、结果和退出码；不从MCP读取交互凭据stdin。
- **App** 是packaged Sprout原生宿主，业务只引用Core，功能目录负责首页、实例、新建、迁入、本地包、运行环境、内容/世界及设置；资源、窗口偏好和生命周期归App管理。AI对话界面尚未接入。
- **SettingsGenerator** 在编译期为App的`SettingProperty`生成偏好属性，并以`RMLUI001`保护手写只读IView的主题依赖、以`RMLUI002`保护应用默认样式；生成View交由Sprout的`SPROUT074`。App由`AppStorage`按真实包身份选择包LocalSettings或无包EXE的当前用户注册表偏好，文件与缓存分别归各自LocalState/LocalCache；它不承接Core业务。独立EXE由`scripts/package-exe.ps1`发布自包含NativeAOT及Inno Setup当前用户安装包，无包EXE是默认构建与GitHub Releases分发路径，MSIX仅保留显式验证。
- **Rodel.Minecraft.Skins** 是遵循Minecraft贴图规则的独立图像编辑类库，负责文档、绘图、UV、PNG及3D预览，不引用启动器、CLI、MCP、Sprout或AI。Core引用它进行启动器存储适配，不能反向依赖Core。
- **Rodel.Minecraft.Seeds** 是独立种子契约/地图输出类库，与Skins同级，不引用Core。正常构建嵌入自有Java适配器，实际生成只调用精确版本原版代码；Core的`Worlds/Seeds`持有依赖准备、JVM生命周期、完整文件指纹、缓存、可恢复搜索和收藏/导出事务。Seeds另承接冻结区域/容器契约、原版模型与纹理解析、可切层几何和共享投影相机；Core承接原版FULL区域生成、校验与原子缓存及原资源读取。App的`Features/SeedExplorer`持有独立窗口、视野、渐进瓦片调度、图例/图钉/Popup、D3D预览工作器及Core调用，Shell只组合入口。GPU工作器按需绘制、复用输入，相机帧与图钉使用同一投影，关闭时等待原生资源释放；不将游戏生成或战利品逻辑搬入UI。详细边界见[Core API](CORE_API.md#原版种子探索器)。

CLI与packaged App以NativeAOT发布，Core启用AOT兼容检查；AI实现与边界见[AI说明](AI.md)。`mods/rodel-ui`是独立Gradle/Fabric工程，不加入.NET解决方案、不引用Sprout；它的菜单适配及外观同步不提供AI游戏观察/控制能力。不引入无实际需求的ORM或反射插件框架。

App复用Jobs/typed Tasks观察真实终态，各功能任务Dialog负责当前操作的呈现和退出约束。任务中心是共享业务能力，不规定独立下载页；未来允许任务脱离Dialog继续运行时，先按需要接轻量全局入口，不为页面形态新建任务引擎。

### Core工具所有权与渐进发现

`Core/Tools/ToolCatalog`同时拥有输入契约、源生成schema与具体Core调用；`LauncherToolService`复用普通业务服务和typed任务，不解析CLI token。CLI/MCP仅包装目录、传输与CLI专属帮助/execute。`AiCoreTools`按绑定资源和当前能力冻结授权目录，`AiToolExecutor`保留许可、审核、intent/job及未知效果检查。后台提交将调用标识传入`LauncherTaskService`，重复提交观察原job，不创建第二个副本或工作流。

`AiToolDiscovery`初始仅暴露发现与技能读取，按领域/关键词/精确名称加载一页定义；外部/托管MCP的连接也按领域延迟。`AiTurnDriver.SetTools`同步官方MEAI invoker列表，模型请求、压缩与预算只使用`Visible`，不是原来的固定全目录。每回合重置，任务内可显式reset。`AiTurnContext`保留原作和完整授权数据，显式读取才序列化入模；纯聊天关闭这些操作上下文。查看定义不授予任何权限。

App正常发布不产生CLI构建依赖或CLI payload。独立CLI仍提供命令、对外MCP和CLI显式选择的后台Core宿主。外部客户端安装只管理产品技能与MCP条目，引用选定独立CLI的绝对路径与hash；更换程序使旧计划失效，移除接入不删除该独立安装。

### Assistant模块职责

[Assistant](../src/Rodel.MinecraftLauncher.Assistant)按职责组织，根目录只保留工程文件；保留`Rodel.MinecraftLauncher.Assistant`命名空间，以`Microsoft.Extensions.AI 10.8.3`及其Abstractions作为统一基础。provider保持自研，不引入供应商SDK；不维护平行的消息、调用、工具、回复或用量模型。

| 目录 | 职责与入口 |
| --- | --- |
| [Chat](../src/Rodel.MinecraftLauncher.Assistant/Chat) | `AiChatClients`创建自研HTTP/本地IChatClient及统一MEAI观测管线；`AiModelStream`向Core转发真实流/推理观察并用官方聚合、typed parser；其余方法仅处理完整调用、schema与续接绑定 |
| [Providers](../src/Rodel.MinecraftLauncher.Assistant/Providers) | `AiHttpChatClient`自研四种协议的完整SSE/update映射，声明/消息/回复使用MEAI；模型目录与`AiTokenCounter`承接公开元数据、可选计数和本地估算；独立文本探针保留有界请求协调，`Requests`声明必要wire DTO，不依赖供应商SDK |
| [Inference](../src/Rodel.MinecraftLauncher.Assistant/Inference) | runtime负责Embedded缓存/租约/卸载，engine接口限定SDK替代边界；本地聊天通过统一管线直接调用SDK IChatClient。此处仅保留目录与独立文本报告，不再另写聚合循环 |
| [Mcp](../src/Rodel.MinecraftLauncher.Assistant/Mcp) | `AiMcpSession`使用SDK McpClientTool的WithName/WithDescription/AsDeclarationOnly复用元数据，仍负责目录限额/冻结、凭据与会话；Windows进程归属及有界JSON-RPC流独立封装 |
| [Transport](../src/Rodel.MinecraftLauncher.Assistant/Transport) | 有界HTTP/SSE、凭据同源约束、诊断脱敏和`AiProtocolException`，不拥有Core配置或业务权限 |
| [Models](../src/Rodel.MinecraftLauncher.Assistant/Models) | 仅保留启动器请求上下文、续接绑定/内容指纹、业务推理观察、公开文本/目录结果、MCP注册/结果/诊断及引擎观察；不重新定义MEAI已有的数据模型 |
| [Serialization](../src/Rodel.MinecraftLauncher.Assistant/Serialization) | 业务与wire DTO使用`AssistantJsonContext`；标准消息/contents/参数使用MEAI `AIJsonUtilities.DefaultOptions`自带源生成元数据。用量转换器仅保留CLI输出字段名，不参与完整消息存储 |
| [Properties](../src/Rodel.MinecraftLauncher.Assistant/Properties) | 程序集级友元声明，与业务模型分离 |

本地SDK不再经过自定义消息/调用的往返转换。Core自有工具用`AIFunctionFactory.CreateDeclaration`，MCP目录用SDK `AsDeclarationOnly`，向provider只提供声明。回合驱动另将这些声明绑定为真正调用Core守卫的`AIFunction`，由MEAI负责调用与继续推理；不把可直接执行的裸MCP工具交给模型管线，也不造假执行委托。Core仍拥有业务状态、授权与执行策略。旧Inference HTTP健康探测与daemon响应包装分支已删除，不新增产品入口、框架或对Core的反向依赖。

JSON只留在真正开放的边界：第三方原始HTTP/SSE响应、原生消息/签名块、工具schema及arguments/structuredContent、异构错误详情。请求固定外层、归一化结果及MCP会话接口均使用实体；原生消息内部保留开放JSON，避免实体投影删除provider扩展、加密reasoning或thought signature。HTTP结果子类型保留requestId/refusal等字段，不把它们强加给本地SDK结果。脱敏针对外部文本和开放payload，不能改写host-owned状态、身份或能力标志。

Core的[AiChatHistory](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiChatHistory.cs)在同一持久记录中保存权威的完整MEAI消息与供归档/效果判断使用的`role/text/calls`投影；标准消息用MEAI自带元数据存储，不只保存`response.Text`。Core自建system/user/checkpoint/tool记录在这个业务边界转为标准contents，不恢复废弃任务或CLI格式。压缩改写当前窗口时显式移除失效完整消息，原始归档与hash不被重序列化覆盖；本地预算计算实际标准输入，不重复计算存储投影。

原生payload与protocol/model-binding/service-revision及contents指纹一起放在MEAI `AdditionalProperties`的`JsonElement`中，完整消息原子保存。`TextReasoningContent.ProtectedData`由标准序列化保留；`RawRepresentation`不参与持久化。指纹仅规范MEAI对已处理调用设置的`InformationalOnly`标记，不忽略真实文本、参数或签名变化。只有绑定和内容指纹同时匹配才复用原生状态；换服务/模型/revision或编辑内容时按允许字段重建portable contents，清除reasoning、签名与原始元数据。完整消息/签名不进入公开投影、普通日志或跨服务导出。

基础类型、工具工厂、JSON元数据与流聚合已按[固定MEAI源码](https://github.com/dotnet/extensions/tree/ccb356f31db9d894807c4fd0c97c2f41553d1524/src/Libraries/Microsoft.Extensions.AI.Abstractions)核对：`Contents`、`Functions/AIFunctionFactory.cs`、`Utilities/AIJsonUtilities.Defaults.cs`、`ChatCompletion/ChatResponseExtensions.cs`、`UsageDetails.cs`。聚合不会自动拼装provider函数参数字符串碎片，HTTP wire层仍必须校验完整调用；provider累计usage也不能逐帧重复累加。

曾核对[官方OpenAI/Responses适配器](https://github.com/dotnet/extensions/blob/ccb356f31db9d894807c4fd0c97c2f41553d1524/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs)、[Anthropic](https://github.com/anthropics/anthropic-sdk-csharp/tree/027b1322d02b221cc282eb7aa0890c307cf46281/src/Anthropic)与[Google.GenAI](https://github.com/googleapis/dotnet-genai/tree/c1ac776a6d0454c7ef09024733dea81ec23065d9/Google.GenAI)的MEAI接入，现仅作为协议/互操作参考，不实施此前的供应商SDK替换建议。自研provider继续支持手工端点/模型、未知原生字段保真、凭据同源、禁重定向和流不重发，不增加厂商模型目录或排他性服务前置。

#### MEAI回合驱动与Core策略边界

[AiTurnDriver](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiTurnDriver.cs)继承官方`FunctionInvokingChatClient`，一次调用运行模型→工具→结果→继续推理；不复制官方循环或在外层另写工具迭代。[AiChatRunner](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiChatRunner.cs)保留回合生命周期、模型调用策略与Core工具守卫。固定版本的[官方循环](https://github.com/dotnet/extensions/blob/ccb356f31db9d894807c4fd0c97c2f41553d1524/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs)和[函数处理器](https://github.com/dotnet/extensions/blob/ccb356f31db9d894807c4fd0c97c2f41553d1524/src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationProcessor.cs)是实际运行依赖，不只是类型参考。

- 每次模型调用仍经Assistant真实流式`IChatClient`向Core持久事件发送正文、思考与用量，App/CLI观察同一事件流；外围使用`GetResponseAsync`不意味着把流式界面退回整段回答。
- 普通请求、上下文选择、拒绝后恢复与可选标题共用严格模型调用计数。MEAI的iteration limit可能追加工具关闭后的最终请求，因此不拿它替代Core总预算。token预估与目录上限仍是建议；可选计数失败不凭估计阻止本可运行的请求，真正的上下文拒绝仅在尚无部分输出时进行一次有界压缩恢复。
- Core的guard负责目录/重复ID检查、权限、精确确认、journal/job与未知效果；确认在函数内等待并可取消。串行调用且错误阈值为0：预期业务失败返回结构化结果，持久化I/O故障直接中止同批后续工具和下次模型。未提供的调用也进入明确拒绝/审计；incomplete/refused中的调用仅作观察，不执行。
- 官方批量结果规范为每条工具消息一个`FunctionResultContent`，保留协议名称/调用ID；压缩后的完整持久窗口写入MEAI实际可变工作历史。最终不将聚合回复重新追加回去，避免复活归档内容或重复已完成工具。
- 函数处理器不取得观测源，执行范围隔离ambient agent activity，避免工具描述/异常原文被默认工具遥测采集；模型层继续使用下述受保护观测。Core事件与效果记录始终保留，不以遥测代替执行证据。

#### 结构化输出、观测与评估

上下文选择使用`ChatResponseFormat.ForJsonSchema<AiContextSelection>`从同一源生成契约取得schema，放入`selectionSchema`数据包后才计算输入预算；MEAI `ChatResponse<T>`负责typed JSON解析。此处不调用会额外追加schema消息的通用辅助方法，避免绕开当前硬预算；也不强迫未知服务实现原生JSON-schema模式。围栏JSON只在克隆的解析视图去除围栏，原回复及非文本contents保留。ID完整分类、原文/角色/hash、选中文本成本和未知效果仍由Core校验，typed parsing不代表这些约束已通过。

聊天流、selector及本地文本探针使用官方`OpenTelemetryChatClient`，source/meter为`Rodel.MinecraftLauncher.AI`，显式`EnableSensitiveData=false`覆盖SDK环境默认值；不安装exporter、collector或正文日志采集器。保留provider、非敏感模型标识、耗时和真实usage元数据，不输出消息正文、工具参数/结果/描述、认证值或endpoint query。绝对路径/文件URI形式的模型标识在观测期间使用绑定别名，包含显式ChatOptions与回复元数据；观测结束后恢复原返回身份，不改实际请求或玩家模型引用。

MEAI即使关闭正文采集也会把异常Message写入span（见同版本[OpenTelemetryLog](https://github.com/dotnet/extensions/blob/ccb356f31db9d894807c4fd0c97c2f41553d1524/src/Libraries/Microsoft.Extensions.AI/Common/OpenTelemetryLog.cs)）。因此诊断保护层在观测前把异常Message变成稳定错误码，凭据清理后的上游原文保留在显式diagnostic details，而非span status。单次provider管线不附加缓存或自动重试；工具循环由上层MEAI驱动，不在provider中再装一份，也不替代Core事件/intent/job证据。独立远程非流式探针及模型目录仍使用原有有界transport。

`Microsoft.Extensions.AI.Evaluation.Reporting 10.8.0`仅引用于Assistant测试工程，不进入产品依赖图。现有试用为真实Core原文抽取/呈现→MEAI evaluator/scenario→官方磁盘结果存储/重读，比较合成中文证据与刻意破坏否定、编号、未知效果的候选。指标仅是独立预期短语的精确保留率，不是LLM语义评分、压缩无损保证或授权判据。评估缓存显式关闭；报告保留在忽略的TestResults并由MSTest附带，操作见[测试说明](../test/README.md#meai-pipeline-and-evaluation)。

## 各层负责什么

| 位置 | 职责 | 典型入口 |
| --- | --- | --- |
| `Application` | 直接应用 API、后台任务、进度、跨功能编排 | `LauncherClient`、`LauncherTaskService`、`JobService` |
| `Runtime` | 运行组合的查询、计划、准备、切换、历史、回退 | `RuntimeService`、`RuntimeChangeOptions` |
| `Launch` | 元数据解析、Java 参数、启动计划、会话和日志 | `VersionResolver`、`LaunchPlanner`、`SessionService` |
| `Installation` / `Loaders` / `Java` | 下载原版、安装加载器与 Java | `InstallationService`、`LoaderService` |
| `Instances` / `Configuration` | 实例定位、设置层叠、游戏设置及迁移 | `InstanceStore`、`SettingsService` |
| `Content` / `Packs` | 本地内容、平台更新、资源包、整合包事务 | `ContentService`、`PackService` |
| `Worlds` | Java存档NBT读取、完整目录/ZIP复制、单世界归档与可恢复替换 | `WorldService`、`WorldMetadata`；App世界管理与在线内容Kind分开 |
| `Core/Skins` | 将独立皮肤库适配到启动器数据根、文件租约和安全发布 | `SkinService`；像素业务不在Core中重写 |
| `Downloads` / `Storage` | 网络策略、文件校验、原子写入、资源租约 | `DownloadService`、`JsonFiles`、`ResourceLease` |

`InstanceStore.PreviewLinks/LinkExistingAsync`复用迁移适配器和原有非托管登记，将所选外部`.minecraft`版本/profile链接到RMCL元数据，不进行迁移复制或获取文件所有权。共享依赖根与真实游戏目录分开保留，官方同版本不同profile保留各自参数与实际游戏目录；单个默认profile不覆盖其它版本的名称和参数。链接前整批验证目录选择，重复项按物理游戏目录／依赖根、版本及profile标识复用，失败只回滚本次新登记。RMCL运行参数覆盖写入自身instance.json，来源HMCL/PCL/profile配置不回写；options.txt、模组配置和存档仍是游戏自身文件，正常启动和显式内容操作可修改它们。

`LauncherClient`是直接应用门面，提供单功能服务和跨功能工作流。启动、内容更新上下文/批量操作、资源包选择、包准备/回退、实例与加载器安装均在Core；`LauncherTaskService`保存并执行typed任务请求。CLI的功能命令组负责命令参数到这些公开方法的适配，Mcp目录将工具输入转换为同一命令入口的token，Core不接收CLI解析对象。

### CLI模块职责

CLI不是只服务AI模块的附加终端，而是整个App能力的可编程入口。业务按领域/资源/动作共用Core；App专属能力仍应由App拥有，不用Core配置假冒或为AI复制同义命令。`AppControlHost/Service`的Core通道与CLI契约保留，发现记录在活动数据根并核对OS包身份/进程/nonce，CLI不引用Sprout。Sprout端此前直接依赖页面控件的控制器和生命周期接线已移除，当前App不注册该端点；将来应由页面和端点共同调用App功能控制器。当前边界见[CLI能力设计](CLI-DESIGN.md)，不能由Core夹具推定真实App已接入。

| 模块 | 职责 | 不承担 |
| --- | --- | --- |
| `LauncherCommands` | 组合命令树、公共解析、帮助与标准token | 功能注册堆积或按命令名二次分发 |
| `Commands/Groups` | 所属功能的声明、校验、参数映射、具体处理与typed工厂 | 复制Core业务或管理后台宿主进程 |
| `LauncherApplication` | 统一执行、缓存租约、后台派发/恢复、进度与宿主生命周期 | 配置/Java/日志/内容等功能处理或静态参数映射 |
| `ContextCommandHandler` | 多个命令的摘要聚合、分段失败保留及活动范围投影 | 重写功能业务或建立另一条Core分发入口 |

LauncherApplication是单一非partial类。局部处理直接留在所属命令组；复杂处理可用具名方法，不为每条命令创建接口、Handler类或DI注册。前台处理和后台工厂共用同一份options映射，launch与server connect共用LaunchCommands的映射，不能各自维护一套。上下文处理器只依赖实际命令执行委托，便于独立替换边界和验证聚合契约，不依赖应用类私有状态。

CLI的`LauncherCommands`只组合System.CommandLine命令树并处理公共解析、token与帮助。具体注册拆入`Commands/Groups`的独立功能类，覆盖实例、配置、Java、启动、会话、日志、任务、备份、加载器、运行环境、游戏设置、资源包、内容、整合包、世界、维护、服务器、身份、皮肤、AI及宿主契约；不是同一大类的partial。各组持有可复用命令符号，例如`LauncherCommands.Content.Install`，不在根类保留几十个转发属性。`LauncherCommand`保存处理函数、typed工厂及校验/缓存元数据；`CommandFactory`只复用建树声明。`CliOptions`/`CliArguments`提供typed参数符号，launch与server connect显式共享同一组选项对象。

每个具体命令在注册处直接绑定对应操作与后台typed请求工厂，不再由共享handler读取`Command.Name`二次分发；只保留确有解析或编排职责的辅助方法。logs仍是可执行且带子命令的父命令。终端帮助与`LauncherCommandHelp`的JSON说明均由同一棵树生成；参数类型转换交给库，范围和组合校验在业务/后台排队前完成。

CLI自有命令/选项/位置参数帮助、帮助使用说明和命令层错误使用`Strings/CliStrings.resx`及中/阿卫星资源。SDK的MSBuild GenerateResource在obj生成强类型`CliStrings`，不提交手写访问类或生成文件。启动语言按参数、环境和系统UI文化选择，仅改变UI文化。动态错误使用完整格式资源，不翻译命令名、选项名、JSON字段或业务取值，也不伪称System.CommandLine、Core与第三方原始提示全部本地化。JSON源生成与resx资源生成是独立机制，均保持NativeAOT可用。

CLI专属execute/帮助与CLI内部调用通过`CliInvocation`引用真实命令及选项符号，按实际父链生成标准token，再进入同一解析与执行入口，不维护第二个业务解析器。选项泛型约束值类型，构造调用时校验符号确实属于当前命令或可继承的祖先；名称不再跨文件重复拼写。`CommandRegistration`拒绝同级命令/别名、有效作用域选项/别名冲突，MCP目录也拒绝工具重名；这属于初始化校验，不冒称编译器能检查字符串唯一性。Core共享工具目录绑定typed输入与业务委托；分页/摘要/结果保留归Core工具服务。解析库只属于CLI，不进入Core/App依赖。

项目未发布，不保留开发中废弃的任务格式。任务只使用`request.operation`和`request.parameters`：Core typed任务为`launcher-task`，CLI通用后台操作为`command`，后者用源生成的`CommandJobRequest`保存当前标准token及显式实例关联。顶层重复arguments、旧具名工具任务、缺失request的回退及旧MCP参数顺序解析均已移除；失败明确记录，不自动迁移或猜测执行。任务取消、已验证可重试操作的恢复仍走同一JobService；恢复只复制当前request。

CLI/MCP固定结果、错误、分页、续查及工具输入使用强类型模型，序列化和反序列化显式传入System.Text.Json源生成的`JsonTypeInfo`；工具输入与输出schema由.NET的`JsonSchemaExporter`从相同元数据导出。构造参数决定必填项、类型和默认值，范围/最少条目等补充约束共用声明；不再逐个手写输入字段的JSON类型、必填表或输出模板。工具目录绑定typed输入和Core服务委托，内置AI直接执行，对外CLI/MCP适配同一契约；只有CLI专属入口解析命令。Core的`LauncherOperationException.CreateResult`提供typed错误分类，既有`ToResult`继续输出相同JSON结构供原调用者使用。

`data`、第三方响应、日志解析器状态和异构`warnings`仍为开放JSON；不把未知内容裁剪成不完整DTO。配置`value`允许任意JSON且必填，缺失与显式null有别；游戏设置和环境字典使用标量转换器，保留数字精度、空字符串与删除语义，禁止对象/数组冒充标量。输出schema明确错误字段、状态和固定外壳；协议允许开放data，不是Core内部用JsonObject传递固定结果的理由。命令文本仅保留在公开语法声明、用户输入和当前命令请求边界；平台名、配置值等真实业务字符串不机械转换成另一套命令enum。

第三方元数据有稳定字段和开放扩展；未知字段本身不是拒绝强类型模型的理由，但不能用严格反序列化无意收紧现有兼容范围。需要保留动态JSON时，将已知property提取集中到领域专用的只读读取类型，以具名方法返回需要的值，不在判断逻辑中反复散写字符串路径。版本规则使用`MetadataRuleReader`包装原节点，不反序列化整套模型、不改写未知字段；`MetadataRules`只负责匹配与覆盖顺序，启动架构预检和安装候选同样复用读取器。保留缺失/非数组与显式空数组的既有区别，非对象规则项统一忽略，未知架构继续参与安装候选；未匹配规则的action不提前读取。封装不意味着吞掉损坏的已知字段，也不承诺任何无效JSON都可执行。

### 皮肤绘图与独立MCP目录

[`Rodel.Minecraft.Skins`](../src/Rodel.Minecraft.Skins/README.md)为独立类库工程，包含唯一的图像编辑实现：`SkinDocument`拥有文档/历史不变量和状态快照，`SkinEditor`协调原子事务/修订/取消，`SkinDrawing`负责笔划/形状/填充与混色，`SkinTransfers`先规划剪贴变换及来源清空，`SkinColor`负责标准RGBA/HSV，`SkinSelections`处理mask，`SkinLayers`处理堆叠与合成。UV模块不承担文档校验，绘画执行器不再内嵌像素转移规划。它不读取启动器目录，不进行模型请求或意图理解；JSON文档元数据、PNG和几何均归本库。StbImageSharp/StbImageWriteSharp的纯托管图像依赖也只在本库声明，兼容NativeAOT。

库内按`Models`（含`Enums`）、`Editing`、`Geometry`、`Imaging`、`Serialization`分区，根部`SkinEditor`保留公开入口。实体/枚举、内部面框架和转移计划不再混放在执行文件，`SkinJsonContext`按模块集中。背景标识、初始化和图层像素访问归文档；只读可见合成归内部`SkinCompositor`，拾取/PNG/预览不依赖编辑执行器，捕获规划不反调公开入口。保留唯一UV规则及单程序集内必要的基础能力协作，不声明为严格单向DDD分层，也不引入空接口或新项目。公共命名空间、API和JSON格式不变。

`Core/Skins/SkinService`只负责启动器数据根下的文档ID、文件租约、工程存储及输出文件安全发布。`SkinProjectFile`将完整文档/历史元数据和按SHA-256去重的压缩纹理存入`.rmcs`容器，复用原有持久flush、临时文件替换及共享删除读取；不再将每步全部像素重复展开为JSON整数数组。纹理使用明确的小端uint32编码并校验内容摘要，读入缓存只作解码复用，返回数组逐份复制以保持当前内容与历史独立。已有当前格式3纯JSON可读，成功保存才写新容器；独立库文档/CLI协议及历史语义不变。另存为生成独立快照，不修改库内来源，不携带执行回执或独立AI会话。独立手动客户端可直接引用皮肤库而不引入Core；AI意图理解、工具映射和会话归上层整合，不能进入图像编辑基础。

CLI请求字符串的typed JSON反序列化与命令资源错误留在`SkinCommands`，不放入Core存储服务。Core将取消参数传播到库内批次与栅格工作，取消不提交部分状态。Cut/Move/翻转会清空来源，必须完整不透明度、关闭透明锁；否则明确拒绝，不能先清空来源再因目标画笔规则跳过写入。半透明或透明锁合成使用Copy/Paste。

`SkinGeometry`的规范面框架供射线拾取、跨缝传输、CPU预览及Sprout客户端复用，UV只来自库内`SkinLayout`。皮肤库不嵌入HTML、不生成网页客户端；Core/CLI/MCP预览仅为PNG，手动编辑的相机交互、输入与呈现归Sprout App，不能以网页查看器替代。独立绘图测试只引用皮肤库，CLI/MCP接入测试继续留在CLI测试工程，不复制同一组绘图用例。

当前文档格式3将背景像素和完整绘图层一起保存到撤销快照，不把图层压平后冒充可编辑工程；容器去重只改变磁盘表示。MC Base/Outer与绘图图层是不同维度：背景维持Java基础表面不透明，附加绘图层以正常源覆盖合成。预览/PNG/取色/射线透明判定共用可见合成，指定绘图层的读取仍返回原始像素。选区仅作用于面内，镜像反射足迹；跨面笔划只在同一立方体的相邻面展开，不猜测跨身体部位连接。

常用工具行为参考固定版本Blockbench与NeedCoolerShoes，来源与许可边界集中在[库说明](../src/Rodel.Minecraft.Skins/README.md#references)。不复制上游GPL实现，不引入JS编辑器、插件或模型SDK，也不扩成建模/动画系统。

第二MCP是同一CLI的独立`mcp serve --tool-profile skins`进程，使用`SkinToolsCatalog`，不增加第二个可执行程序或Agent循环。专用目录不发布通用`execute`和启动器管理工具；默认`launcher`目录不变。两者复用传输以及Core/Tools中的typed schema、处理函数、错误外壳和结果保留；CLI专属execute才进入命令树。皮肤PNG预览通过标准MCP图像内容返回；外部MCP返回标准图体；内置AI直接读取Core预览字节，并与同一调用的标准MEAI工具结果一起持久化，实际模型输入包含图片而不只是路径。外部模型能否观察图像由provider决定。该目录不是权限沙箱，显式文件路径仍按宿主权限访问。

Java账号使用标准64×64、Classic/Slim，旧64×32按原版规则导入；不把128×128输出或任意自定义模型冒充标准Java账号兼容。游戏实际应用与编辑文档分开：导出不是账号上传或游戏加载；本地模组验证不证明联机其他玩家可见。详见[CLI](CLI.md#皮肤绘图与3d预览)及[Core API](CORE_API.md#共享皮肤编辑内核)。

## 运行管理

运行组合的计划、提交、恢复与回退由Core RuntimeService负责，可独立于CLI和界面调用：

```csharp
var runtimes = new RuntimeService(paths);
var plan = await runtimes.ChangeAsync(instanceId, new RuntimeChangeOptions
{
    MinecraftVersion = "1.21.1",
    RemoveLoader = true,
    PlanOnly = true,
}, cancellationToken);

// 确认需要执行时用相同 API 去掉 PlanOnly；已有本地版本可设 Offline。
var history = runtimes.GetOperations(instanceId);
await runtimes.RollbackAsync(instanceId, operationId, cancellationToken);
```

CLI 的 `runtime` 分支只转换参数。普通切换、包准备和迁移准备共用运行管理服务；其中依赖安装循环也合并了。`RuntimeSelectionStore` 是内部具体类型，负责快照、文件租约与回退；不是为了模式名称造一个通用仓储。

提交顺序仍是：

1. 保存原选择，准备可复用的游戏文件。
2. 安装完成后取得游戏目录和实例元数据租约，检查游戏是否在运行、原选择是否被改过。
3. 写 prepared 操作快照，再一次写入实例运行设置，最后更新快照状态。
4. 回退只还原 `versionId`、`loader`、`migrationRuntime`，保留用户后来改的昵称、内存等设置。重复回退仍可用。

这是一套可恢复的文件事务，不是假装磁盘上多个 JSON 能进行数据库式原子提交。快照最后一步写失败时会提示，已经成功的选择仍算成功；安装失败也不会删除原来的可用运行组合。

## 采用的设计方法

- **门面与适配器**：入口共用应用能力，协议格式不进入新的运行管理服务。
- **按玩家任务组织服务**：安装、内容、运行选择各有负责人；跨功能操作由应用层或明确的运行服务编排。
- **策略只用于真实变化点**：网络请求用 HTTP handler 组合镜像策略；外部平台保留可注入的 provider。普通文件操作无需一层空接口。
- **租约、快照和补偿**：内容、包、运行选择均根据自己的数据所有权恢复；不强迫它们继承同一个“万能事务基类”。
- **文件发布语义**：Loader与Java解包共用`Storage.AtomicFile`的流复制、刷盘、关闭临时输出及move提交/清理，源流仍由调用方持有，缓冲与文件选项由业务显式选择。租约、路径/哈希校验与备份/回滚留在各自负责人；`JsonFiles`保留自己的Replace及Windows重试，不把不同提交语义统一改写。

这不是完全的整洁架构或严格 DDD，也没有逐类实现全部设计模式。目标是能找到修改位置，并让一次修改影响尽量少的玩家流程。

## 游戏控制台

`SessionService`持有每流1MiB的`GameLogBuffer`；`GameLogHost`只通过当前用户命名管道提供有界只读快照，复用`FramedJsonPipe`和内部源生成JSON类型，不另起日志服务EXE。CLI的既有监控子进程与App进程内宿主使用同一实现。采集热路径只读游戏管道并复制环形缓冲，日志读取、解析、导出和每日清理不在生产者循环中。正常结束释放内存；非主动停止的异常退出才保存快照。绝对字节游标、淘汰范围和不可用原因始终随结果提供，持久捕获按七天策略管理，玩家文件及显式导出不属于自动清理范围。契约见[Core API](CORE_API.md#会话日志与诊断导出)。

## Win32 互操作规范

此规则适用于仓库自有C#产品代码及验证夹具，不修改第三方库、子模块或非.NET模组的内部实现。

1. **先复用现成能力。** .NET、Sprout、WinAppSDK及已采用的专用库能直接满足语义时，不另写Win32包装。确需Windows API时优先采用CsWin32；不在业务文件复制SDK已有的`DllImport`/`LibraryImport`签名、结构体、消息常量或枚举。
2. **声明归调用工程。** 使用`Microsoft.Windows.CsWin32`的`PrivateAssets=all`，符号列在各工程`NativeMethods.txt`，不提交生成源码或增加运行时生成/下载依赖。相关工程保持同一生成器版本；当前Core、Assistant、App及JavaProbe使用0.3.275。Assistant使用独立生成类名`AssistantPInvoke`，避免friend assembly中的同名绑定冲突；不把生成类型扩散到Core公共业务契约。
3. **匹配真实ABI。** 优先使用生成的类型、命名标志、Span和安全句柄重载；结构大小来自实际类型，不写死字节数。保留原API的Unicode/ANSI、错误码、调用约定和功能语义。生成元数据的别名仍需核对，例如`DnsQuery_W`返回的`DNS_RECORDA*`别名须按`DNS_RECORDW`读取UTF-16字符串，不能直接调用ANSI指针的文本转换。
4. **架构不能靠猜。** 架构相关的DNS记录布局需要具体CPU。Core的`IsRidAgnostic=false`使调用方RID能沿项目引用传递；显式RID使用SDK架构推导，无RID的本地构建默认当前SDK CPU，不硬编码x64或退回手写布局。启动器自身CPU与所选Java位数是两回事，不因此限制游戏版本或32位Java。
5. **资源所有权优先于重载便利。** 本函数拥有的句柄/分配使用匹配释放器的SafeHandle，或在`finally`释放；借用句柄不关闭，转交成功后不再释放。DPAPI返回分配由`LocalFreeSafeHandle`管理，解密输出复制后清零；DNS列表交给`DnsFree(..., DnsFreeRecordList)`。剪贴板读取借用`HGLOBAL`，写入成功后系统接管，必须使用生成的原始强类型句柄重载，不能套用会关闭返回句柄的便利重载。
6. **保留错误与降级契约。** 按API实际返回的BOOL、WIN32_ERROR等判断成功；需要last-error时，在清理或下一次系统调用之前读取。兼容提示仍为提示，凭据失败不退回明文，日志副数据失败不改变游戏退出码，不能因迁移绑定新增玩家准入门槛。
7. **例外必须有依据。** 仅对SDK元数据确实未覆盖或无法正确表达、且现有库也不能满足的接口保留最小手写绑定；说明原生原型来源、架构/编码/所有权和保留原因。非Win32的私有原生协议、着色器数据布局不机械套用CsWin32。不能仅为回避生成器诊断而保留重复ABI或关闭诊断。

验证围绕实际行为与释放边界，不为每个声明机械增加测试。修改后运行受影响回归并验证NativeAOT；编译、受控进程、真实系统调用及App交互分别记录。共享环境中不为测试清空用户剪贴板、修改玩家数据或结束无关进程。

当前自有`src`/`test`中的手写Win32导入已迁移，涵盖游戏启动、DPAPI、受限文件、DNS、App进程/管道身份、MCP Job、皮肤剪贴板和测试窗口；已有框架绑定继续复用。原生界面实施同时遵循[设计规范](../design/DESIGN.md#win32互操作实施约定)。

## 风格和 Azure SDK 指南如何落地

[根 `.editorconfig`](../.editorconfig) 以 `Rodel.ItHome/.editorconfig` 为基础：4 空格、CRLF、文件作用域命名空间、`var`、版权头、修饰符和命名偏好。Git 对 C# 也使用 CRLF；文档和脚本保留原来的 LF 规则。生产代码展开原先密集的多语句，新增 API 使用有含义的参数和 XML 说明。

参考 [Azure SDK .NET 设计指南](https://azure.github.io/azure-sdk/dotnet_introduction.html)，这里采用职责集中的服务、复杂操作的 options 对象、异步方法命名、明确的资源所有权和可取消操作。已有服务 API 中的 `ct` 参数暂保留，以免破坏命名参数调用；新运行 API 使用末尾可选 `CancellationToken cancellationToken`。

按 [实现指南](https://azure.github.io/azure-sdk/dotnet_implementation.html) 的异步实现原则，Core 的等待和异步释放使用 `ConfigureAwait(false)`，并启用 CA2007 检查。进度回调由操作线程调用；UI 应传入在 UI 线程创建的 `Progress<T>`。回调不要在阻塞等待中再同步调度 UI。

现有公共服务有少量无实例字段的方法，保留实例调用方式并在方法上写明 CA1822 豁免，避免为了“可改 static”破坏 API。没有全局关闭这项偏好。CLI 事件输出改用自己的 `Lock`，避免锁定外部也能访问的 Console 对象。

Azure SDK 面向云服务的 `Response<T>`、统一云异常、同步/异步成对方法等没有照搬。我们的磁盘操作、游戏进程和未知模组元数据有自己的语义。

## 怎么保持下去

```powershell
pwsh -NoProfile -File scripts/check-style.ps1       # 检查
pwsh -NoProfile -File scripts/check-style.ps1 -Fix  # 修正格式与约定
dotnet build Rodel.MinecraftLauncher.slnx -c Release
```

检查脚本遵循根EditorConfig。正式测试加入解决方案，使用各自的obj/bin；测试CLI部署从被引用工程的实际输出解析，避免混用其他构建的产物。提示级检查和运行方式见[测试入口](../test/README.md)。

## 还应留意什么

Core固定结构的业务摘要、操作回执、审核、计划与进度由所属模块实体表达，服务直接构造结果，CLI/MCP输出边界使用显式源生成元数据序列化，App直接读实体属性。多种已知结果用闭合接口/派生类型，不添加wire discriminator，不用`object`/`JsonNode`替代原来的`JsonObject`签名来冒充收口。JsonContext按模块集中，不为每个结果类型创建Context。第三方响应、未知元数据、动态设置值、异构warnings及实际持久文档继续保留JSON；模型化固定业务消息不等于改写玩家文件或重建存储格式。CLI/MCP协议字段、显式null、可选字段缺席和未知数据透传保持既有语义，不给实体增加模拟JSON对象的字符串索引器或隐式转换。各领域实体与保留理由见[Core API](CORE_API.md#固定载体的统一归属)。

任务状态使用`JobRequest`固定执行器请求外壳，`Parameters`只在实际任务序列化边界保持开放；执行前仍由所属typed任务/CLI执行器校验。`LauncherClient`、实例复制及世界操作通过`IProgress<LauncherProgress>`传递测量值，不先造JSON再读。任务节流、历史计数及进度写入失败不阻断业务的契约保留，整合包准备进度复制可变步骤，不能让后续完成/取消篡改已发事件。`ApplicationJsonContext.Job/Jobs/Progress`与内容模块的专用TypeInfo保留阶段字段存在性；不能用未经修饰的Default元数据丢失null/缺席差异。扩展字典保持可写以支持源生成反序列化，持久读取将未知/不规范值保留到扩展数据，不把它们转换成默认成功或空请求。

Java选择说明已收敛为`JavaSelectionReport`、`JavaSelectionCandidate`和`JavaSelectionSource`：策略、启动计划、内存/编码与自动准备流使用实体，JSON只在计划输出、安装结果与会话持久化边界生成。原始第三方元数据继续开放，不因实体化丢弃扩展字段。Java模块的源生成元数据集中于`Java/JavaJsonContext.cs`，同时覆盖探测缓存和选择报告，不为每个实体创建Context；字段声明保留缓存与报告原有的命名和null契约，不增加迁移或旧Context转发层。

`LoaderService` 的安装处理器代码仍较长，`ContentService` 的事务和分析也不小。下次增加安装器种类或改变事务机制时，应按实际变化点提取协作者；仅把文件拆成更多 partial 不会解决职责问题。

网络客户端按数据目录与配置内容复用。设置现可自动保存网络配置，默认内容服务逐次请求取新配置，旧请求继续使用原客户端；显式注入保持调用方契约。大量不同配置仍可能累积客户端缓存，当前没有覆盖其长期资源增长的证据；后续按实际负载评估退休/释放，不能在有请求时直接Dispose共享客户端。镜像正文超时、有界原源回退、相对重定向和加载器元数据共享路径见[Core API](CORE_API.md#网络配置与请求生命周期)；loopback检查不替代真实SYN超时或峰值并发测量。

HMCL/PCL选择版本后的来源目录、隔离策略及配置解析继续集中在Core `InstanceMigration`，现代HMCL明确覆盖与版本目录定位位于`InstanceMigration.Selection.cs`。预览与typed导入共用版本/游戏目录参数；UI编辑使预览失效，明确目录选择后才提交。

最新导入默认复制迁入，由Core `InstanceStore.ImportCopy.cs`规划和复制，完成后提交metadata；进度复用LauncherClient.Report/Jobs。App只选择参数并观察typed任务。默认数据根为用户`.rmcl`，覆盖目录由App Settings功能控件保存，Shell仅组合控件，重启解析客户端数据根。

可丢弃缓存与玩家数据分开：`LauncherPaths`允许宿主覆盖缓存根，其他路径仍相对于原数据根。App未设置数据目录时，把Core缓存放在包私有`LocalCachePath/Launcher`；通过设置指定目录时，保持原来的`cache`等相对路径不变。CLI默认及显式数据根也维持`<root>/cache`。不自动搬迁或删除旧目录，不把实例/存档放进新的卸载清理范围；图片缓存继续使用其既有包私有位置。

## 实例与共享依赖存储

参考 Prism Launcher 源码 `736213d92b554a26b5a1ac8d9b3046d8cd8b4d1c`：`launcher/Application.cpp` 的资源/库缓存根和 Java 目录，以及 `launcher/minecraft/MinecraftInstance.cpp` 的 `OverrideJavaLocation` 与 `librariesPath()`。沿用游戏内容独立、启动器级依赖共享和实例可覆盖 Java 的原则；以下 JSON 与依赖集合布局是本项目实现，不声称与 Prism 文件格式兼容。

```text
.rmcl/
  dependencies/
    java/<runtime-id>/runtime/     # 共享完整 Java；同级 registration.json
    assets/<set-id>/               # 资源集合
    libraries/<set-id>/            # 库集合
    clients/<client-id>.jar        # 客户端归档
  instances/<instance-id>/
    instance.json                 # schemaVersion 2；相对目录与 Java ID
    game/                         # mods、saves、资源包、光影、配置等可变内容
    runtime/
      dependencies.json           # schemaVersion 1；资源/库/客户端引用
      versions/<version-id>/      # 实例自己的版本元数据
```

导入先在本次生成的私有目标中复制并校验，再按完整目录内容 SHA256 发布/复用资源、库和 Java，客户端 JAR 单文件去重。相同 Java 内容跨实例只存一份，版本字符串相同但字节不同不会合并；实例版本 JSON 独立，允许同名自定义加载器版本并存。当前资源/库去重粒度是完整集合，不是不同集合之间的逐文件去重；重复导入仍需要临时复制空间，最终存储会复用。后续可以在版本化引用背后优化集合粒度，不改变游戏目录或 UI 业务契约。

`SharedRuntimeLayout`负责解析共享引用。安装与加载器服务在写入前分离当前实例的共享依赖，防止修复/更新污染其他实例；旧布局无引用文件时仍按原路径解析。`JavaRuntimeService`统一管理`dependencies/java`，兼容读取旧`runtimes`登记；实例用`javaRuntimeId`及相对`javaRuntimeExecutable`引用，同一Java可保留各实例选择的java.exe/javaw.exe，手工`javaPath`仍可覆盖，实例 Java 优先于全局配置。删除 Java 时检查实例/全局设置及活动会话的引用。

Java管理保持现有具体服务，不另建统一仓库框架或持久全局列表。登记列表与解析均优先当前布局的同ID条目，逐项保留坏登记并核对目录身份；托管下载与归档导入使用相对登记，外部关联保留其原路径。移除直接删除自有运行库和安装元数据、最后移除登记，外部文件及未知文件不扩大删除；不再移至Java恢复目录。官方目录区分网络与缓存回退，缓存写入失败保留已取得的新目录并报告警告。App只消费typed任务和结果，确认文案同步永久删除语义。

`JavaDiscovery`按已知布局/注册表/环境及登记重新收集候选，以四路有界并发执行单次属性优先探测，不设整体查询时间预算。`JavaProbeCache`单独负责可丢弃观察的原子持久化与跨进程协调；每次根据可执行文件和release摘要、关键运行库元数据、链接解析和相关环境校验。失败或不完整观察不缓存，包装程序不作为稳定目标缓存；可选缓存故障返回警告而不阻断真实探测。详细边界见[Core API](CORE_API.md#java发现与变化感知)。

探测记录按可执行路径固定命名，同路径新观察原子覆盖，不保存版本历史；既有缓存维护只按已知命名和写入年龄回收，不增加文件追踪器、容量策略或后台扫描。未知文件继续保留，记录失效不等于删除Java。

行为参考为[HMCL JavaManager.java](https://github.com/HMCL-dev/HMCL/blob/63207affc6a73127cea19e64272a0401fde4b7a4/HMCL/src/main/java/org/jackhuang/hmcl/java/JavaManager.java)的已知布局/注册表及变化键缓存，以及[Prism JavaInstallList.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/77f2bee593eb9e0cdeef530e0e8686524b68d9d9/launcher/java/JavaInstallList.cpp)的有界并发探测；只参考职责与行为，自行以.NET实现，不复制上游源码。

共享依赖发布在登记前完成。取消/失败清理本次实例目标，已完整发布的公共依赖可能留作缓存，绝不据此删除来源或其他实例依赖。当前没有自动清除未引用共享集合，避免把缓存清理扩张成有数据风险的管理功能。相对游戏/运行路径与 Java ID 可随整个数据目录移动；用户自定义 JVM 参数中的绝对文件路径仍需用户按实际用途调整。

新建原版切片位于App `Features/Create`：Controller管理清单、创建登记、typed安装任务、取消/重试及最近未完成任务恢复；Fields/Popup/Presenter只呈现选择、状态和共用详情。Home负责完成刷新与选中，Shell继续只组合入口及窗口生命周期。新建路径与导入采用同一版本化共享引用；安装完成发布按内容去重，引用切换前保留旧私有内容，取消或发布失败不清理其他实例依赖。Java准备是Core `InstallInstanceAsync`的可选步骤，不在UI重写下载或版本判断。

### 新建安装的共享命中、恢复与日志

InstallationService在网络请求前使用SharedInstallationCache按预期SHA1/大小查找本数据根共享集合及受管版本元数据。候选只读，命中复制到本次可写目标并复校验，最后原子替换；不可读候选退回下载，不修改其他实例依赖。不新增全局二进制CAS，集合发布/引用切换继续由SharedRuntimeLayout管理。离线能力取决于完整可验证文件和元数据，不把清单缓存视为已安装。

InstallationRecoveryService按实例归并Jobs最新attempt，再暴露可恢复终态；UI通过Tasks.ResumeAsync创建关联attempt，不在窗口打开时自动重放。登记没有对应job的孤立状态仍需明确检查，当前没有自动回滚/删除机制。

LauncherActivityLog是Core副数据服务，AsyncLocal只传递job/instance关联，固定字段排除路径/名称/URL/参数/原始异常。目录锁和200ms预算避免日志阻断业务；UTC七天、每日最多4段×8MiB轮换，保留范围只涉及本类前缀日志。Jobs仍是操作终态依据，活动日志不能替代状态数据库或作为安装完成证据。Details和Maintenance诊断包读取相同日志；现有游戏日志不受该日志保留策略影响。

原版安装先解析真实文件计划，再执行文件校验/共享复用/下载；InstallationProgress同时保留总文件计数和当前文件字节，Java准备由LauncherClient衔接并保留前者。App只投影阶段与进度，复用Jobs取消/恢复语义。安装可读报告/JSON切换/复制导出仍由共用DetailReportPopup承担；HomeInstanceList只持有列表与滚动状态，不承接Core任务或选择持久化。创建后启动由CreatePresenter转交HomeController现有启动工作流，Shell不增加业务。

### 实例任务表单与只读诊断

Create/Import 控制器继续拥有识别、安装和迁入任务，Dialog 只组合固定 header、滚动正文与底部操作。导入识别按修改立即失效、300ms 合并、revision 淘汰过期结果；实际迁入仍调用 Core typed 任务。LaunchSettingsDraft 保存原值及编辑期状态，统一字段策略/脏状态；普通保存不写版本，仅可显式恢复经 VersionResolver 验证的已登记版本。HomeInstanceReader 使用同一实际版本解析结果提供启动可用性，未知加载器信息本身不升级为门禁。

DetailReportPopup 对等值报告不重置操作状态，JSON 阅读使用进入时的快照，复制/导出使用显示内容。布局刷新保持滚动范围内的位置，显式编辑/光标移动/真实内容替换由 Sprout 的相应输入路径显现光标，不能每次 LayoutUpdated 都将滚动拉回旧光标。

## Java游戏数据桥

可选的同源Java17 agent嵌入Core，启动监控在玩家明确启用时追加独立`-javaagent`，保留authlib-injector。agent无第三方依赖，仅窄变换明确的原生读键getter，以明确官方映射/原名和Fabric公开MappingResolver绑定游戏API，不要求安装模组。游戏类初始化及加载器就绪后才接入；原版26.3时钟变化保留局部适配，不能以同一个JAR推定所有版本兼容。

Core/GameBridge持有强类型API、只读边界、操作intent/收据及有界IPC。监控→Java为loopback TCP，双方挑战HMAC与会话incarnation绑定；App/其它CLI→监控为当前用户专用管道，并核验PID/启动时间。端口和秘密不持久化，后台只用既有会话监控，不新增服务EXE。产品仍为CLI/Sprout → Core → 内部AI子模块。

`mods/rodel-ai`是游戏内的薄输入/渲染入口，不是第三个启动器宿主。它经agent已有认证连接反向调用`launcher.ai`；`GameAiSession`在会话监控进程内调用既有`AssistantService`，按实例＋存档身份保存唯一侧栏槽位，快速问答使用独立会话。对话、默认模型、队列、压缩、MEAI工具循环与精确许可仍由Core拥有。Java只转发typed动作与新鲜快照、接收状态和绘制；游戏来源的纯文本提示不进入普通启动器聊天。

快照及有界区域读取调度到客户端线程，服务端写入在当前自有内置服务器线程、原生输入/容器在客户端线程执行，并双重核验归属；服务器一律只读。Core与Java共同核验世界身份，AI回合冻结首次本地世界根身份，跨维度/复活须重新观察当前epoch并沿用现有未知效果守卫。业务收据不等于存档已落盘；断线不重放，停止观察不停止游戏。完整契约及实际版本矩阵见[Java游戏桥](JAVA-GAME-BRIDGE.md)。

披风复用独立皮肤库的Cape文档与真实矩形纹理，不新增平行编辑器服务。Core工程只保存可选CompanionId预览关联；原生在切工程/选择搭配时异步读取冻结引用，渲染与输入热路径不读盘。场景只拼接原RGBA纹理和独立几何，GPU分别取样、遮挡；拾取只返回活动作品。人物皮肤和披风按各自工程事务保存及分别导出，PNG不包含预览关联。只读参考不扩大皮肤AI的绑定文档权限。

对外MCP的discovery profile固定发布五个小入口，capabilities按需给单个schema，tool_call复用Core契约；独立CLI仍可显式发布launcher/skins完整具名目录。安装器只登记独立CLI路径与discovery参数，真实握手和只读Core调用通过后才登记。

对外MCP的typed后台任务使用独立CLI作业宿主；同EXE内部job-dispatch仅负责短生命周期派发并在回包前退出，防止MCP客户端清理服务端进程树时误终止已接受作业。它不承载业务或成为额外发布产品，App/Core进程内路径不使用它。
