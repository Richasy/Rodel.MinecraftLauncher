# Tests

`LinkedInstanceTests`使用真实小型文件布局验证HMCL/PCL的共享／版本隔离、继承元数据、官方同版本多profile，以及单个默认profile不会污染其它版本。链接、重开、修改RMCL设置、重复链接及解除登记后，用来源树内容摘要核对没有写入或复制原游戏；含目录歧义的整批确认必须在任何登记前拒绝。用Core入口加`--filter FullyQualifiedName~LinkedInstanceTests`；这些用例不代表真实HMCL存档或原生菜单／弹层E2E通过。

原生E2E入口`test/Fixtures/InstanceLinksUi`使用真实App Shell、导入/链接弹窗与Core，不替换扫描、登记或存档读取。默认隔离数据为VM的`C:\sprout\data`，授权本机读取时用进程环境`RODEL_LINK_FIXTURE_DATA`指向专用临时目录；它只替代App偏好和数据根，不连接玩家已有RMCL配置。可在该目录放`theme`（`light`或`dark`），或放`import-source`（实际来源路径）直接观察旧复制导入的只读预览。通过DevTools原生UIA菜单/Value/Toggle/Invoke验证链接，独立检查instance.json、来源摘要、取消和重复链接；实际游戏运行必须另用隔离游戏/存档副本，不在原玩家存档上做破坏性验证。夹具及临时数据不得进入产品安装包。

`UninstallProcessTests`使用实际发货的卸载脚本和就绪后阻塞等待的真实子进程，验证同一目录下的多个实例均被强制终止，而同名、路径前缀相似的另一目录进程保持运行；进程不通过关闭窗口或标准输入正常退出。用 Core 测试入口加 `--filter FullyQualifiedName~UninstallProcessTests`，只操作测试创建的进程，不读取或卸载本机已安装 App。真实 Inno 确认／取消、托盘卸载及玩家数据保留另在隔离 VM 验证。

`NativeKeyboardNavigationTests`使用真实App投影、回调、DirectWrite和Sprout输入路由，保护设置分类／皮肤页签的一次Tab出入、方向键只移动而不提前执行、末项滚动可见，以及图片裁切只处理一次方向键且不连带滚动父层。用App测试入口加`--filter FullyQualifiedName~NativeKeyboardNavigationTests`并在VM运行；不打开玩家工程，也不代替正常签名MSIX的键鼠、弹层和Narrator实测。交互契约见[键盘导航与焦点](../doc/KEYBOARD.md)。

`SkinLimbCopyTests`用非对称六面标记、半透明可见层、隐藏层和目标哨兵像素，保护Classic/Slim及64/128的对侧映射、仅当前层替换和隐藏外层保留。`SkinMirrorInputTests`沿用实际App输入及Core会话，检查单侧肢体的画笔/橡皮仅写自身对面，显示同类对侧后才互相镜像；六面字面坐标同时核对预览与落笔，避免正面或顶面被误做同面左右翻转。一笔跨头、躯干、单侧胳膊及双腿仍逐部位配对，不写隐藏肢体或把头顶映到躯干底部；只替代packaged窗口/Controller对等方。`SkinHoverRenderingTests`用真实D3D读回核对正面单画笔与镜像输出相同，因为另一画笔应位于被长方体遮住的背面；无轮廓基线须确实不同。`SkinBrushAndSurfaceTests`另核对可见部位内填充和预览一致；`SkinTransferSessionTests`以实际文件重开和单次撤销/重做核对底层与外层一次持久提交。分别用下方Skins、App、Rendering、CLI入口按类名筛选，不代替原生确认框、免提示偏好和真实填充手势验收。
`AiSettingsLifetimeTests`直接引用实际App程序集（使用别名，不替换既有状态测试），通过真实Sprout条目工厂实现／回收／重新实现AI服务分组，核对模型名称更新和编辑入口仍在。它保护模型控件不能跨已释放的虚拟分组复用，不使用网络、凭据或应用窗口；用App测试入口加`--filter FullyQualifiedName~AiSettingsLifetimeTests`。原生滚动、225%几何及准确MSIX另在VM验证，不由这个控件生命周期用例推定完成。

`NativeTextSizingTests`使用实际App行／导航／Checkbox投影与DirectWrite测量，核对100%／225%的AI历史和近期皮肤工程都容纳完整主次文字，长确认说明在有限宽度内完整换行，以及中文短词不拆行、英文／阿拉伯语导航最多两行和有界zoom。皮肤页签另在常规／紧凑窗口高度下核对完整命中区域、文字居中和底部指示线，防止仅给外框最小高度而留下空白。另保护带逐项可用性的选择器模板与皮肤事务历史的真实有限宽度省略；选择器只在测试侧通过反射取得固定Sprout的实际item source，不复制模板或改变产品API。Checkbox用例不打开或接受删除流程；皮肤项目由隔离Core存储创建，不读玩家目录。使用同一App入口加`--filter FullyQualifiedName~NativeTextSizingTests`并在VM运行；它不替代外层滚动视口、完整窗口、真实tooltip和caption的实际验证。

`SkinCameraPanTests`使用真实相机投影及皮肤射线拾取，保护放大／旋转后的屏幕平移方向，以及平移后同一已知面像素的拾取一致性；相机平面锚点按输入像素位移移动，不复制平移实现来构造期望。使用Rendering入口加`--filter FullyQualifiedName~SkinCameraPanTests`；不冒充原生Ctrl拖拽路由、指针捕获或DPI实机验收。

`SkinSelectionInputTests`直接编译原生App共用输入／选区／响应式状态，向真实输入入口发送Down／Move／Up，配合隔离的Core编辑会话核对RGBA、草稿、修订与历史。覆盖画笔／橡皮／填充在3D／贴图入口的选区外首次按下只取消、同次拖动不补画、下一次按下恢复正常，以及选区内起笔的边界保护、取色／Alt／右键不误取消。仅替代packaged窗口和Controller对等方；填充只核对是否派发，实际填充已有独立Core用例。使用App入口加`--filter FullyQualifiedName~SkinSelectionInputTests`，不声称覆盖真实拾取、指针捕获或悬停动画。

`ReleasePackageAuditTests`保留独立包审计的微型ZIP／PE回归：真实两架构组件定义、严格父作用域下的可选archiveFormat、ARM64配方篡改及错误实际DisplayName拒绝，不冒充可运行程序或有效媒体。`MicrosoftClientConfigurationTests`保护真实Core内嵌资源及显式／进程／当前用户配置覆盖，不修改机器环境或打开账户。使用Core测试入口加`--filter "FullyQualifiedName~ReleasePackageAuditTests|FullyQualifiedName~MicrosoftClientConfigurationTests"`；不代替实际原生运行、签名或真人登录。

`LaunchMemoryPreferenceTests`除内存继承外，使用真实Core文件与共享实例Controller验证自动保存：在已捕获快照后的saving通知中插入新编辑并改变首页选择，核对最终值／归属／未知字段；未提交及非法文本不落中间值、不阻断独立有效项；真实文件替换拒绝后保留草稿且可恢复重试；初次读取失败没有待写草稿时允许退出且不改坏文件。使用App入口加 `--filter FullyQualifiedName~LaunchMemoryPreferenceTests`。`HomeObservationTests`原窗口就绪用例另核对窗口前非100%、真实就绪状态后仍显示100%及后续错误保留。以上是状态与持久化证据，不代替轻触关闭手势、原生颜色或实际Minecraft窗口验收。

`CriticalMenuItemTests`通过实际 Sprout 菜单投影和原生 interaction 验证危险项的鼠标／Space／无障碍调用只执行一次，并先关闭菜单再执行原回调；保持同一行时切入／退出高对比度，检查系统前景配对及关闭后的观察释放，禁用项不可聚焦或调用。只在测试侧用反射构造当前固定 Sprout 的内部 build context 并进入 protected 扩展入口，产品没有反射或自写菜单输入状态机；这些无窗口用例不代表真实子菜单展开或桌面手势验收。

`InstanceOperationFailureTests`保护实例失败的可读报告：结构化任务错误、仅含原始错误的记录和直接异常均保留实际原因，已有错误码不丢失；显示、复制及导出复用该报告。真实 Core 登记验证非托管实例默认保留文件、不能打开自动删除，切回托管实例恢复原默认。`InstanceBackupRemovalTests`另以撤销托管和归属标记不符两个变体验证 `INSTANCE_FILES_NOT_OWNED`，拒绝时文件、登记和备份保持不变。这些用例不读取玩家实例，不代替 packaged App 实机验收。

`InstanceBackupRemovalTests`通过隔离的真实实例与ZIP归档验证两种实例移除模式都会清理归属备份，同时保留其他实例和归属不明的损坏归档。篡改manifest ID与改名归档保护实际删除路径；共享读取但拒绝删除的文件句柄验证失败后保留登记并重试；真实metadata/game租约屏障验证排队中的创建／恢复在登记删除后拒绝陈旧实例。使用Core测试命令加`--filter "FullyQualifiedName~InstanceBackupRemovalTests|FullyQualifiedName~BackupReadTests"`运行；不读取玩家目录，不代替原生界面验收。

`SkinTransparencyWorkflowTests` uses a real editor session and separate CLI processes to verify mask drafts, cancellation, persisted history, transparent PNG pixels, and restoration of the original paint. Run the CLI test command with `--filter FullyQualifiedName~SkinTransparencyWorkflowTests`.

`LocalContentUpdateTests`以真实合成JAR、扫描、冻结审核序列化和内容事务验证本地包的在线更新，只替换远程HTTP响应。覆盖哈希精确识别、模组ID／名称候选须显式选择、同名错误归档拒绝、审核后原文件变化拒绝及远程失败后重试；成功路径核对重命名的禁用包替换、策略和备份保留、来源仅在提交后登记、确认后不再查询远程版本。真实Modrinth、NativeAOT CLI和packaged App仍须独立验证，不以受控用例代替联网或原生界面证据。

`CapePreviewLibraryTests` 通过隔离的真实皮肤工程和合成 PNG 验证共享搭配库。重新打开 Core 后，库收录专用搭配导入和有效的旧披风关联，普通人物 PNG 导入不进入库。使用 Core 测试命令加 `--filter FullyQualifiedName~CapePreviewLibraryTests` 单独运行。

`ConcurrentContentDownloadTests` 通过受控 loopback HTTP 正文屏障和真实 typed 任务／内容事务，验证同时传输、后发任务先提交、相同依赖合并、不同固定版本拒绝、单独取消及提交前玩家文件变化保护。取消其中一项后，变更保护须指出仍在下载的另一任务及准确ID，真实完成后才解除占用；`VisualPackSelectionTests`另保留实际活动游戏的写入拒绝及原因区别。候选元数据由合成冻结清单提供；不访问玩家目录，也不等同于公共 Modrinth 吞吐或游戏加载验收。使用 Core 测试命令加 `--filter FullyQualifiedName~ConcurrentContentDownloadTests` 单独运行。

`ScreenshotFileTests`保护截图目录的只读发现、Unicode/大小写改名、跨实例/陈旧选择拒绝、导出副本及直接删除的取消／部分失败；`AtomicFileTests.Copy_NoOverwriteAndDestinationAppearsDuringStaging_PreservesInterveningFile`在真实暂存写入后设置屏障，保护导出提交不覆盖后来出现的文件。`ScreenshotRecycleTests`使用测试专属文件进入真实Windows回收站并回读回收路径的字节，另在真实pre-delete回调拒绝操作，清理只恢复/移除该用例拥有的文件。`ScreenshotSelectionTests`链接App实际Store，保护刷新、筛选、排序和旧实例读完成后的精确选择；另以无窗口的真实ItemsView及生产选择桥验证单击不打开、原生双击计数触发、Ctrl／Shift／Ctrl+A不误开查看器和范围锚点保持。`ScreenshotWorkflowTests`通过真实CLI及外部MCP execute验证默认回收、显式--permanent、两种删除都要求--confirm、原字节副本和逐项结果，支持`RODEL_TEST_CLI_EXECUTABLE`切换NativeAOT产物。原生图片、文件选择器、剪贴板与默认查看器仍需独立packaged App验收；这些测试不运行Minecraft或读取玩家截图。

`LocalModIdentityTests`通过真实CLI与合成Fabric／Quilt／Forge／NeoForge包验证改名、同版本不同字节的本地更新必须审核后替换，旧文件可恢复、原禁用状态和玩家策略保留、旧平台来源不冒充新包；另覆盖同批重复ID、多模组JAR的次要ID／部分替换拒绝，以及手工包重新启用的ID冲突。使用`RODEL_TEST_CLI_EXECUTABLE`可对同一正式用例切换到NativeAOT CLI；这些检查不运行Minecraft，也不模拟原生拖拽手势。

`ViewThemeReadAnalyzerTests` 编译小型源码片段，保护手写只读 `IView` 计算属性的主题依赖及应用默认样式边界；生成 `[View]` 的同源码间接取色由 Sprout `SPROUT074` 负责，App 分析器不重复报告。手写视图：实时取色通过属性、样式工厂、字段初始化及值工厂传递时，日常 App 构建必须报告 `RMLUI001` 错误；显式外观输入、void 回调、绘制方法组、`nameof` 和 `Markup.Defer` 不误报。测试调用实际 SettingsGenerator 分析器，最小 Sprout 声明仅用于语义绑定，不模拟渲染；`RMLUI002`另保护宿主构建时固定的应用默认样式，拒绝直接或间接捕获实时主题；固定几何／token注册允许通过。真实热切、保留控件和弹层生命周期另用隔离签名 App 验证。运行 App 测试命令并加 `--filter FullyQualifiedName~ViewThemeReadAnalyzerTests` 可单独检查。

`RepairPlanningRegressionTests` 通过真实 Core schema、ZIP 索引、冻结方案和提交／撤销路径，覆盖修复类型与内容目录参数同名时的契约隔离、Fabric 描述原始换行、内嵌依赖候选与顶层重复、Modrinth 可读名称备用检索、下载后真实 ID 校验，以及保留已满足要求的本地依赖。仅候选 HTTP 响应受控；不访问玩家数据或公共服务，不将这些检查视为真实 Minecraft 启动验证。可使用 Core 测试命令加 `--filter FullyQualifiedName~RepairPlanningRegressionTests` 单独执行。

`MinecraftListenerTests` 使用真实套接字覆盖现代状态、两代旧状态、非游戏响应、调用方取消，以及显式备用地址的网络可达与游戏身份区分；自动发现仍须真实游戏状态。`MultiplayerRecoveryTests` 以实际陶瓦组件检查旧状态端口进入托管、明确拒绝后的新操作、未知结果保持原操作，以及备用地址失联与恢复；另在真实已建立流之后停止状态响应及接受新连接，检查旧流仍双向可用、游戏错误不被刷新清空，显式停止才撤销。Live 用例只替代游戏端点，不证明真实旧版游戏或跨网络入服。`NetworkProcessLifetimeTests` 在独立进程中链接生产 Windows Job 所有者，分别在 bootstrap 尚未派生和两代 detached 子进程已就绪时只终止测试 owner，观察网络后代退出及外部合成游戏保留；不依赖房间轮询清理。`RoomGameEntryTests` 另区分主动换址与被动刷新，验证旧TCP／UDP会话保留、新会话路由及最终撤销，并覆盖无远端状态资料时的现代／旧版首帧与模组后缀。

`PackWorkflowTests.Export_ForMultiplayer_ExcludesPersonalAndUnreferencedFiles` 实际运行 CLI，覆盖本地 JAR、CF 来源 JAR、资源包 ZIP 及 CF 格式中的光影 ZIP；读取导出清单，跨独立数据根导入、再次导出、拒绝错误补件、接受正确补件并检查最终文件。CF 格式使用 `rodelMissingFiles` 扩展，外部客户端可读伴随文本获取要求；不能把来源声明当作出版方身份验证。可通过 `RODEL_TEST_CLI_EXECUTABLE` 针对 NativeAOT 产物运行。

`MultiplayerEndingTests` 保护显式结束的控制所有权、冻结进程身份、正常窗口关闭及真实退出观察；持有关闭请求的合成窗口证明不强杀、不重复发送，结束进程丢失后从持久意图恢复也不重发。启动准备被精确屏障挂起时提交结束，验证同一次启动取消且不先建立网络。它不证明真实 Minecraft 保存，也不代替隔离游戏的保存、退出、重进及原生 UIA 验收。`MultiplayerAdmissionTests` 另通过真实 CLI 检查默认 stop 的网络范围与显式 `--exit-game`，可用 `RODEL_TEST_CLI_EXECUTABLE` 复验 NativeAOT 产物。

`MultiplayerPresentationTests` protects usable-room evidence, unknown membership, timestamp-only observation deduplication and mini-window dismissal across main-window hide cycles. It links the production projection and lifecycle policy; packaged UIA separately verifies minimize/tray/restore, settings, close, participant scrolling and save thumbnails with an isolated room snapshot. These checks do not establish cross-network joining.

`InstancePreparationProgressTests` protects the presentation of a large game-file transfer: byte progress must change while completed-file counts stay fixed, and the file percentage must match that count rather than the overall three-phase preparation. It uses production state projection with recorded-shaped snapshots; packaged rendering and real downloads are observed separately.

`TerracottaRuntimeTests` 使用实际 Windows Filtering Platform 应用标识验证两个临时目录入口具有相同身份，检查缓存损坏恢复和删除联接后共享映像保留；不监听网络、不写防火墙规则。真实组件生命周期还在正常连接及 worker 替换后核对固定程序路径和解包联接。

`VersionCatalogCacheTests` 验证跨服务并发共享、过期/强刷、损坏与离线恢复；`RoomGameEntryTests` 通过真实双向 TCP 验证固定入口、切换目标和旧流终止。`MultiplayerAdmissionTests` 运行实际 CLI，检查原生码验证、单房间租约、邀请保护与非重放任务。`PackWorkflowTests` 另核对联机导出不携带个人数据或无来源组件；`PackCompletionTests` 的既有恢复流程覆盖单文件暂存的错误/正确字节。Java桥 LAN 场景保护旧式／MultiplayerScope 原生发布、独立访客权限、幂等、旧世界拒绝和绑定缺失时效果前拒绝；它们使用受控 Java API／线程，不等同于真实 Minecraft 运行。

`MultiplayerRoomTests` 的 Live 用例需 `RODEL_MULTIPLAYER_LIVE=1`，启动随包陶瓦和公开发现服务，使用合成 Minecraft 状态端口/LAN 广播；验证真实组件端口/所有权、杀死已记录 worker 后有限恢复、同码和保留游戏。另一 Live 用例将已经存在的游戏监听与延迟发布收据交错，要求继续读取终态、世界从开放中变为已开放且仅提交一次。它不等于双机/跨网络/游戏入服，也不代替实际隔离 Minecraft 与原生 UIA/WGC 验收。

`MultiplayerPublicationTests` 保持原开放回执未知，交错手动 LAN／延迟世界观察、已连接后的多次回执刷新和网络重试，要求邀请码持续可用且不重放世界操作，随后观察原回执完成。另一用例只停发 UDP，保留真实 TCP 监听，跨实际发现超时后启用广播并恢复同一房间，检查独立组件阶段与脱敏导出。`MultiplayerPublicationWorkflowTests` 经实际 CLI 的后台房间拥有者、show/retry/export-log/stop 验证相同边界及源生成字段，可用 `RODEL_TEST_CLI_EXECUTABLE` 指向正常 NativeAOT CLI；三个类均以 `RODEL_MULTIPLAYER_LIVE=1` 选择真实随包组件／公开服务，替代的是游戏端口和桥回执，不证明跨网络入服。
`SavedRegionReadTests`通过独立NBT夹具验证实际保存的方块朝向、未知覆盖、Items／组件精确整数、未开启LootTable不求值，以及原版模型和物品图像读取；夹具运行时故意不可生成且没有种子。另一个行为保护收藏的实例／存档身份及跨修订JSON恢复。`WorldMapReadTests`在同一保存结构夹具中核对完整子件边界和附近结构过滤。它们补充真实只读存档及packaged App核验，不代替真实游戏定位或所有历史版本验收。

Automated tests for Core behavior, non-UI Assistant workflows, the command-line interface and the independent Minecraft skin editing library, using MSTest and Microsoft.Testing.Platform.

`ManagedPackageTests` 验证当前进程架构的托管 MCP 清单选择、错误架构在打开文件前拒绝，以及释放 pin 后的载荷篡改拒绝；小型字节夹具不冒充原生 EXE。`ReleaseNativeTests` 仅在显式设置 `RODEL_TEST_RELEASE_ROOT` 为已发布 CLI 目录时运行，使用真实 Core 子进程所有者、三个随包 MCP 与实际文件读取，无协议替身。发布 CI 在原生 x64／ARM64 Windows 分别执行这两组用例，另用准确 MSIX 验证 App UIA；不把这些检查称为游戏或商店验收。可用 Assistant 测试命令加 `--filter 'FullyQualifiedName~ManagedPackageTests|FullyQualifiedName~ReleaseNativeTests'` 执行。

`ScreenshotSelectionTests.KeyboardActivation_UsesGalleryFocusAndPreservesOtherKeyOwners`使用生产键盘绑定与真实CommandDispatcher／条目节点，保护Space选择、Enter仅打开焦点图片、不改变其它选择，以及文本输入／弹层／重复键／绑定释放边界；不创建窗口或注入主机键鼠。

`WorldMapPreviewTests` retains the real saved parser, revision checks and PNG export while substituting only the original tile producer. It protects actual saved pixels, native pre-surface versus damaged/empty chunks, separate prediction provenance, failed-preview recovery and source changes/cancellation at an explicitly held preview boundary. `WorldMapRevisionTests` checks metadata-only discovery of unqueried legacy changes and newly saved region, external, player and namespaced files. `WorldPreviewConfigurationTests` protects supported native generator settings and distinguishes resource-only packs from excluded generation definitions. These cases do not establish actual native generation; the CLI/Core native checks use isolated runtimes separately.

`PreparationProgressTests` crosses real pack downloads/override publication and Fabric base/library/profile installation with substituted HTTP responses, protecting batch progress against per-file resets and premature completion. `PackReadinessTests` rejects a vanilla plan for an uncommitted Forge pack, cancels at the actual launch/preparation boundary, reopens committed readiness and detects a missing library; it also checks automatic versus saved manual memory through actual launch plans with the reporting-Java fixture. `ContentDisplayInventoryTests` retains metadata without claiming a display hash, then verifies that mutation reads changed bytes; synthetic Forge/NeoForge archives also protect complete quoted names in both inventory and analysis. App `LaunchMemoryPreferenceTests` crosses the shared form baseline/save mapping, preserving automatic inheritance and the last valid manual value; it also reads the real home summary through manual → automatic → manual changes, checking that automatic sizing replaces the retained manual limit and switching back restores it. Native scrolling, realized row counts, footer placement and padding are verified separately through the packaged App/UIA; these cases do not run Minecraft.

`SkinLayerPngTests` protects transparent-layer sharing with independently encoded RGBA fixtures: frozen same-size imports preserve the existing stack and one-step history; exports retain raw pixels even when hidden, locked or partially opaque; invalid sizes, legacy/damaged files, cancellation and stale revisions preserve state. One actual MCP-import/CLI-export journey checks optional-field forwarding and sharing, and can target the published NativeAOT CLI with `RODEL_TEST_CLI_EXECUTABLE`. These cases do not establish native file-picker or menu behavior, which require separate packaged UIA validation.

`CapeSceneTests` checks rectangular PNG alpha/UV and mixed-resolution read-only picking. `SkinOutfitTests` creates the character through CLI and the blank cape through real stdio MCP, then crosses pairing/solo previews, stale association rejection and .rmcs storage, preserving the skin bytes while editing/exporting the cape and reopening its copy. `CapeRenderingTests` draws both HD textures through real D3D, swaps the active work, and checks the popup constant budget. These automated boundaries do not replace packaged UIA workflows or actual WGC captures.

`InstanceListLifetimeTests` protects instance inventory membership while configuration/content reads are pending. It deletes a real isolated Core registration, pauses two detail reads, checks that the deleted row disappears before either finishes, then releases obsolete success/error results last. This covers the App state boundary; packaged UIA separately checks dialog dismissal and the remaining instance's menu.

## Prerequisites

Core also carries the launcher-owned Fabric, Quilt, Forge and NeoForge mods. Building Core/CLI/App requires JDK25 or newer and the pinned Gradle dependencies; set `BundledModsJavaHome` or `JAVA_HOME`. The Forge1.20.1 build additionally needs JDK21–24, selected with `BundledModsLegacyJavaHome`, plus a JDK17 toolchain for ForgeGradle's internal Minecraft/MCP compilation. CI explicitly exposes the `setup-java` installations to Gradle through `org.gradle.java.installations.fromEnv`. The shared resolver also searches PATH, the user's `.jdks`, and Java/Eclipse Adoptium/Microsoft installation directories under Program Files, skipping stale or incompatible automatic candidates. An explicit MSBuild JDK directory must be valid; it never silently falls back. Runtime assets are built and copied into `Assets/BuiltInMods/<Minecraft>/<loader>` and `catalog.json`, including test outputs; the current [matrix](../mods/COMPATIBILITY.md) is registered in `mods/bundled-targets.props`. Player machines do not need a JDK or Gradle for this flow. `BundledModTests` uses actual shipped JARs in isolated synthetic instances to protect loader-specific dependency closure, provenance/idempotence, renamed disabled upgrades, same-version native-ID replacement, filename collision preservation, inherited runtime selection and frozen-review drift. Shared API retention also covers real Fabric tilde predicates, incompatible minor versions and an unmet patch lower bound, checking the frozen plan and retained bytes. It does not run Minecraft.

`GameBridgeJavaTests` executes the production Java dispatcher against small native-API fixtures, including actual recipe alternatives/layout/workstations, FTB visibility/team progress and Patchouli unlocks/custom pages. `GameBridgeMappingTests` protects exact official-to-SRG mapping, installed offline mapping reuse and corrupt-cache recovery. `GameBridgeMcpTests` forwards Unicode book searches, defaults and unavailable-provider results through real stdio MCP and accepts `RODEL_TEST_CLI_EXECUTABLE` for NativeAOT. The API fixtures do not establish game compatibility; real modpacks and loader clients require independent validation.

- A Windows development environment for launcher/CLI tests; the independent skin library tests need no desktop or launcher runtime.
- The .NET SDK selected by [global.json](../global.json).
- Access to the package sources configured for the repository when restoring dependencies.
- JDK 17 or newer for the embedded game bridge and its actual Java regressions. Set `GameBridgeJavaHome` for MSBuild, or use a compatible JAVA_HOME/PATH; the helper ignores an older JAVA_HOME and continues searching. The observation agent and historical native-API fixtures additionally use a JDK 8 compiler with `-source 6 -target 6`; select it with `GameObservationJavaHome`. `GameBridgeLegacyTests` accepts `RODEL_TEST_LEGACY_JAVA` to execute the same production agent on an actual Java 6, 7 or 8 runtime, including authentication, native-thread observation and ownership revocation. A class-file version check alone does not establish an older JVM run. Player installations need only their ordinary game JRE.
- Local inference is temporarily disabled by default; `LocalInferenceAvailabilityTests` checks rejection without initialization or provider substitution. The retained SDK-specific cases compile only with `-p:EnableLocalInference=true`, which also requires `git submodule update --init --recursive`. Default release builds do not reference or package this dependency.

Run the following commands from the repository root. The Core test project references Core and does not build or invoke the CLI. The CLI test project builds its command-line target and dependencies automatically. The skin library test project references and builds only the independent skin library. A separate application build or an existing publish directory is not required.

`SkinPalettePreferenceTests` in the Core test project links only the App's UI-independent palette preference model
and presets, without loading Sprout or a packaged window. It protects new empty palettes, independent editable copies,
adding a fresh editable slot from an already-present foreground seed, selection/RGBA round trips and preservation/retry after a failed commit. The write callback substitutes LocalSettings;
actual packaged persistence and control wiring are separate native UIA evidence, not claims of these cases.
Preset removal also survives later creation, deleting the final palette and independent reopening; a rejected preset-deletion commit preserves the prior selection and retries normally.

`SkinPaletteReferenceTests` in the App project crosses the real Windows decoder and WIC palette API with a small independently encoded HD PNG and synthetic color bands. It protects full-resolution straight RGBA sampling, exclusion of hidden transparent RGB, a non-padded palette capped at sixteen actual source colors, periodic opaque/transparent stripes larger than the sampling budget, and known pixel coordinates after fit, cursor-anchored zoom, pan and reset. Unequal source populations additionally protect merging close grays without inventing averaged colors or losing skin tints; reordered neutral/warm/green/blue bands protect stable color-family and lightness ordering instead of WIC output order. Run the App command with `--filter FullyQualifiedName~SkinPaletteReferenceTests`; the cases do not inject desktop input or claim native picker, magnifier rendering or gesture acceptance.

## Run Tests

GitHub发行更新：Core入口加 `--filter "FullyQualifiedName~GitHubUpdateTests|FullyQualifiedName~UpdateHandoffTests|FullyQualifiedName~ReleaseManifestTests"`，覆盖架构／语言／跳过版本、静默远程失败、实际文件流／摘要与取消、CI清单，以及真实Windows进程退出后才运行安装探针、安装探针退出后才删除。探针替代交互安装器，不冒充实际Inno Setup安装或GitHub公开发行通过；标题栏与弹窗另外使用Sprout.DevTools在隔离VM驱动。测试不读写玩家数据。

App入口加 `--filter FullyQualifiedName~UpdatePreferenceTests`，验证实际设置／更新Store重开后保留跳过选择，以及下载完成后取消仍撤销安装资格、关闭被拒后不能重放旧安装意图。后者独立于HTTP取消，防止晚到的Dialog continuation继续退出并安装。
完成快照回归还会挂住真实`Progress<long>`投递，允许下载完成后再逐条释放旧字节通知，确保已验证的总字节立即可读且不会倒退。进度准确性同时验证框架的真实填充绘制，不以UIA RangeValue正确替代像素比例正确。

`Fixtures/UpdateUi`是仅供VM的原生E2E入口，引用实际App的Shell、标题栏、Dialog和安装交接；不进入产品包。准备正常CI的release artifact（`release-note.json`与对应x64安装器），在该目录加一个UTF-8 `language`文件，写入`zh-CN`或`en`。发布该工程后，以`sprout-devtools drive-shell <publish> --exe UpdateUi.exe --devtools <DevTools发布目录> --data <release输入目录> --collect-data`运行；数据只落在VM的`C:\sprout\data`。夹具将当前版本设为`0.2611.4.0`，仅以本地CI文件替代GitHub HTTP，其余更新代码与真实Inno Setup安装器不替代；不把这项模拟称为公开稳定频道发布或真实GitHub下载吞吐验证。NativeAOT夹具显式启用发布优化，避免完整Shell在未优化原生调试构建中的栈开销。
`Fixtures/UpdateUi`是原生E2E入口，引用实际App的Shell、标题栏、Dialog和安装交接；不进入产品包。准备正常CI的release artifact（`release-note.json`与对应x64安装器），在该目录加一个UTF-8 `language`文件，写入`zh-CN`或`en`。发布该工程后，以`sprout-devtools drive-shell <publish> --exe UpdateUi.exe --devtools <DevTools发布目录> --data <release输入目录> --collect-data`运行；数据只落在VM的`C:\sprout\data`。夹具将当前版本设为`0.2611.4.0`，仅以本地CI文件替代GitHub HTTP，其余更新代码与真实Inno Setup安装器不替代；不把这项模拟称为公开稳定频道发布或真实GitHub下载吞吐验证。NativeAOT夹具显式启用发布优化，避免完整Shell在未优化原生调试构建中的栈开销。

可通过`app.update.open`、`app.update.notes`、`app.update.install`、`app.update.progress`、`app.update.later`与`app.update.skip`定位真实控件。跳过选择保存在测试输入的独立副本中，收集后可用作下一次VM输入；启动阶段、请求文件名及实际下载／清理结果同样可从收集目录观察。完整交互安装验收仍在租用的VM中完成，不在开发者桌面自动执行安装。用户明确要求本地界面演示时，可仅为演示进程设置`RODEL_UPDATE_FIXTURE_DATA`，指向独立的绝对数据目录；不自动点击更新或安装，实际安装仍由用户在向导中决定。
在测试输入目录添加空文件`progress-checkpoints`，可让真实安装包在25%／50%／75%／100%字节边界短暂停留，便于通过DevTools分别采集视觉填充与数值；该夹具开关不影响JSON元数据或产品下载器。

可通过`app.update.open`、`app.update.notes`、`app.update.install`、`app.update.progress`、`app.update.later`与`app.update.skip`定位真实控件。跳过选择保存在测试输入的独立副本中，收集后可用作下一次VM输入；启动阶段、请求文件名及实际下载／清理结果同样可从收集目录观察。完整交互安装验收仍在租用的VM中完成，不在开发者桌面自动执行安装。用户明确要求本地界面演示时，可仅为演示进程设置`RODEL_UPDATE_FIXTURE_DATA`，指向独立的绝对数据目录；不自动点击更新或安装，实际安装仍由用户在向导中决定。

`AccountSkinRefreshTests` exercises the real registry and protected credential files with substituted profile HTTP. It protects Microsoft bearer/public-profile selection, external-source binding, changed/reset skins, failure preservation and late responses after login/removal. `AccountSkinSchedulingTests` runs the App scheduler on its owned test context with controlled response barriers and time, protecting coalescing, provider isolation, cooldown, failure backoff and disposal/new-login precedence. `AccountAvatarRetentionTests` protects the shared last-good image across failed replacements and late completions after provider changes/removal; image decoding is outside that boundary. The existing `ExternalAccountWorkflowTests` also crosses the real CLI/Core loopback path for `account refresh --skin-only`, checking the new URL and unchanged credentials/default selection. These cases do not establish a personal account's remote propagation delay or packaged avatar rendering.

`ContentPlanWorkflowTests` additionally checks exact pack selections against Modrinth format tags: resource/shader packs must not receive a Fabric mismatch, while genuine mod-loader and Minecraft metadata advisories remain. `ContentFindingPresentationTests` checks the shared install/update projection's readable names, release numbers and conflict/advisory distinction without fixing translated prose. These tests do not establish packaged rendering or actual game compatibility.

`PackExportRuntimeTests` crosses real CLI export and archive inspection for vanilla, Fabric, Quilt, Forge and NeoForge in both archive formats. It protects effective profile overrides, base Minecraft identity, removal of stale recognized loaders, retained unknown declarations and unchanged source metadata. Actual Forge fmlloader and NeoForge 1.20.1 FML shapes prevent confusing the FML version with the loader release. The cases accept `RODEL_TEST_CLI_EXECUTABLE` for NativeAOT; their small synthetic profiles do not establish game startup or real local-mod/config round trips.

`AtomicFileTests` exercises the two production output profiles against real Windows files. A controlled source stops only after bytes have reached the temporary file, distinguishing failed/cancelled staging from accidental writes into the destination; another open handle denies replacement until retry. The cases also protect borrowed source position/lifetime and cleanup limited to the current attempt. The source producer is substituted; file creation, publication and failure cleanup remain real.

`LoaderPublicationWorkflowTests` runs the actual CLI/Core against a supported unmarked runtime with a cached synthetic profile and staged library. A held target permits backup reads while denying replacement; failure must retain the old bytes, releasing the handle permits retry, and another installation must reuse identical bytes without rewriting them or the historical backup. The local profile's unknown extension is retained byte for byte. `JavaRuntimeWorkflowTests` crosses real ZIP and gzip/TAR import, probe and removal with the reporting-worker fixture. These workflows accept `RODEL_TEST_CLI_EXECUTABLE` for NativeAOT; they do not run Minecraft or a real JDK.

`DownloadSourceTests` runs the actual source handler, metadata buffering and file downloader with a recording remote transport. It protects distinct BMCLAPI resource paths, official defaults, unrelated/lookalike origins, custom rules, sensitive-header exclusion, missing/failed mirror bodies and SHA256 rejection followed by original-source recovery and verified reuse. `DownloadConsumerTests` crosses real loader caching and public login-component preparation with synthetic Maven catalogs/JARs, protecting NeoForge game filtering and canonical recovery when mirror metadata contains a mirror URL. App `PreferencePersistenceTests` additionally reopens the actual source choice and preserves other network fields across changes. These cases do not measure public-service throughput or replace separate packaged UIA and real endpoint checks.

`EnumPreferenceTests` links the App's UI-independent settings base. It checks enum-name string persistence,
non-destructive fallback for unknown values, committed notifications and preservation after an invalid or failed write.
It does not load Sprout or prove packaged LocalSettings behavior.

`SeedExplorationStorageTests` covers required/default JSON input, lossless seeds, durable seed/place bookmarks, concurrent writes, cancellation, corruption and invalid search dependency graphs. `SeedMapExportTests` independently decodes the PNG, verifies its generation metadata and frozen rectangular coarse/fine tile crop, and rejects holes or mixed runtimes without replacing an existing destination. These storage/export cases do not require Minecraft.

`SeedMapReliefTests` protects opposing-slope readability, altitude-independent material colors, native water hue/depth, unknown coverage and actual-state top-model/tint/alpha interpretation using tiny synthetic resources. `SeedSurfaceNativeTests` opts into `RODEL_SEED_TEST_PROFILE` and compares a decorated dappled-forest map against a separately requested original FULL voxel volume, then reopens its cached raster. It never opens a player save; missing opt-in configuration is a skip, not rendering evidence. Packaged presentation and visual quality still require separate inspection.

`WorldMapPreviewTests` also checks that supported close surface refinement requests actual material sampling within the native 128-block budget, while quick previews, larger footprints, slices and older adapters retain base requests. The substituted producer supplies an independent material color, protecting request selection and color preservation without claiming real-JVM generation.

`SeedVersionAvailabilityTests` protects installed-instance version availability and the settings-to-seed-profile Java boundary: global paths, instance overrides, managed runtime references and explicit removal back to automatic discovery. It uses synthetic metadata and registration without probing Java or starting Minecraft; real original generation and packaged error/recovery remain separate validation.

`SeedVersionSelectionTests` protects the App's mapping from an opened Core runtime to its grouped version choice. Retained loader, Java, path and installation differences must preserve the actual opened profile, while an unlisted verified version remains selectable without changing earlier snapshots. The tests do not open Java; packaged history restoration and failure preservation require separate UIA validation.

`WorldMapRuntimeProfileTests` crosses the separate region and finite-world snapshot constructors to protect effective Java, relative-path resolution, managed references and automatic selection for saved-world previews. Malformed optional Java preferences must leave actual saved pixels readable with a warning; fixture hashes protect the source save. The cases do not start a JVM, so CLI original-generation and packaged source-switch validation remain separate evidence.

`SeedCatalogCacheTests` uses the real disk lease and atomic cache with a substituted original-catalog producer. It protects reopened hits without producer startup, exact-runtime isolation, repair after malformed data with genuine producer errors preserved, and usable original metadata when optional cache writing fails. It does not establish Java startup time or native editor responsiveness; those require production Core/CLI and packaged UIA measurements.

`SeedMappingCacheTests` substitutes only HTTP while retaining the real verified downloader, destination lease and cache publication. It checks cold preparation, repair and offline reuse, including the nested-lease deadlock boundary. `SeedCompatibilityGenerationTests` is `Live`: set `RODEL_SEED_COMPAT_PROFILES` to a JSON array of isolated installed `SeedRuntimeProfile` objects. Each profile crosses the actual selected native API family, verifies tile partitions and uncached recovery after a native height error, then freezes a FULL region and reopens the durable result after the worker closes. Profiles supply game dependencies only; these cases create their own worlds and do not open player saves. Missing opt-in configuration skips them and is not compatibility evidence. Historical games whose spawn uses randomness beyond the seed expose `SeedBoundSpawn=false`; saved-world inspection still reads the actual stored spawn.

The overview storage cases save and reopen the actual 65536-block scale for both seed and place bookmarks, then reject an out-of-range update without replacing the durable record. Viewport planning also constrains a restored or resized complete viewport at the world edge, including a window wider than its saved geometry.

`SeedWorldMapLoadingTests` holds the original-preview completion inside the production viewport scheduler: neighboring saved tiles must already be published, and finer work waits for overview completion. Cancellation rejects a deliberately late successful preview. `SeedViewportPlanningTests` additionally protects retained raster coverage across zoom changes. These cases substitute tile producers and do not measure native generation throughput or packaged rendering. Run the Core test command with `--filter "FullyQualifiedName~SeedWorldMapLoadingTests|FullyQualifiedName~SeedViewportPlanningTests"`. App `SeedMapLayersTests` inspects actual display pixels through neighbor/progress updates and saved-revision replacement. A literal negative-coordinate gradient protects one common zoom grid, whole-viewport refinement, matching-level replacement and zoom-back detail without mutating source images; marker-only updates reuse the composed image. Another saved world cannot inherit the backdrop. A bounded cache case protects active detail under capacity pressure and releases departed history. Tile producers and artwork are synthetic; these tests do not replace packaged pixel capture.

`SeedWorkerFailureChecks` calls the actual Java worker cancellation classifier. It distinguishes direct or JDK-future-wrapped cancellation from genuine errors containing the same words, absent causes and unrelated exception wrappers. It is one focused regression suite, does not initialize Minecraft, and is not packaged in the runtime JAR. Run from the repository root with JDK25 selected; Gradle `check` also includes it:

```powershell
./mods/rodel-ui/gradlew.bat --project-dir java/seed-explorer workerFailureRegression --no-daemon --console=plain
```

`NativeSeedSearchChecks` runs the production portable matcher with a small substituted native world. It checks bounded queries across independent roots, a shared parent witness with interleaved condition order and minimum counts, the first confirmed leaf-biome witness, and cancellation. It does not initialize Minecraft or establish generation throughput. Gradle `check` includes it; run directly with JDK25:

```powershell
./mods/rodel-ui/gradlew.bat --project-dir java/seed-explorer searchRegression --no-daemon --console=plain
```

`SeedNativeGenerationTests` is explicitly `Live`: set `RODEL_SEED_TEST_PROFILE` to an isolated `SeedRuntimeProfile` JSON containing installed original 26.3 metadata and a compatible Java25 executable. Its real JVM cases cover the native catalog/dimensions/heights/structures, corrupt-cache/error recovery, cooperative cancellation followed by an uncached request on the same session, and a durable cancellation cursor resumed in a new JVM. Missing external files skip these live cases; a skip is not original-generation evidence. The FULL-region cases additionally verify four original temple chest positions, real item/enchantment components, block-state properties, palette-section counts against a complete volume, corrupt-volume cache recovery, and cancellation from an observed FEATURES stage followed by an uncached map on the same JVM. The tests do not read player saves or prove packaged UI behavior. See [seed runtime boundaries](../doc/SEED-EXPLORER.md).

`SeedSpawnGenerationTests` uses the same opt-in profile and disposable storage. It compares the exact shared spawn against seed2 coordinates read independently from an unmodified 26.3 dedicated server's `level.dat`, reopens the completed cache without starting a spawn child, and cancels only after the real original global-selection log is observed. An uncached map on the same primary session and a short spawn-reference search then protect cancellation, deadline and uncommitted-cursor recovery. It does not promise the randomized landing position of each player. Ordinary storage/planning cases separately protect reserved `spawn` references, non-Overworld rejection, lossless drafts and the two nearby navigation anchors.

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj -c Release --filter "FullyQualifiedName~SeedExplorationStorageTests|FullyQualifiedName~SeedMapExportTests|FullyQualifiedName~SeedNativeGenerationTests|FullyQualifiedName~SeedSceneGeometryTests" --report-trx
```

`SeedSceneGeometryTests` uses tiny synthetic original-format models to protect parent/texture resolution, half-height geometry, exposed cut boundaries, explicit unsupported models and a visible right-drag projection contract. `SeedSceneRendererTests` in Rendering.Tests exercises the actual triangle D3D worker with an independently red texture, a changed camera frame and awaited stop/cleanup. MSTest `SeedSceneNavigationTests` checks matching primary/secondary pointer capture, literal drag deltas, foreign pointer/button isolation and release/cancel during unavailable states. `SeedScenePresentationTests` checks real production swapchain presentation, resizing the same surface and a caller-retained COM reference surviving worker teardown. The real-JVM surface case compares a larger request with serial small tiles in all dimensions; structure partition coverage separately compares confirmed coordinates, piece counts and bounds. `SeedViewportPlanningTests` links the production UI-independent planner to protect complete raster coverage, lightweight preview work, LOD retargeting, continuous bounded local structure queries and no automatic overview scans, priority collision hiding at literal anchors without clustering nearby types, and labeled block distances matching a bounded 1/2/5 map scale. These cases do not substitute for packaged viewport, physical-DPI, native pointer and pin/layout validation.

`VisualPackSelectionTests` covers actual resource selections and relative priority without dropping builtin or unmanaged choices; shader plan/apply/disable, Latin-1 and Unicode Java properties, recoverable originals, stale-write protection and active-instance refusal. `VisualPackWorkflowTests` runs the real shader command process through plan/select/read/clear and can target the published NativeAOT CLI. The ZIPs and configurations are synthetic; these cases establish configuration behavior, not Minecraft rendering.

The Windows-only `Rodel.MinecraftLauncher.Rendering.Tests` project links the real skin D3D worker and its rendering models, independently of the Sprout window host and Core test dependencies. It requires Windows graphics support (hardware or WARP) and the Windows SDK shader compiler used by the App. Its three lifetime cases cover stopping before any frame, stopping after a real submitted frame, and stopping after an attachment failure has already released native resources. The error callback is the recovery barrier; task completion proves worker teardown finished. These tests do not replace packaged window-close validation.

`SkinAnimationTests` protects the retained walking preview: loop position/velocity at the wrap and distal limb interiors throughout the cycle for Classic and Slim. Independent rest-mesh bounds and a half-texel interior margin distinguish severe penetration from contact at the proximal shoulder/hip joins.

A real D3D readback case renders two walking instants using a synthetic Slim128 skin, restores the exact static output after animation, and verifies the input composite is unchanged. A separate solid-white cuboid on black checks fractional silhouette coverage under preview AA and exact preservation of a fully covered interior pixel. These cases protect motion continuity and shader wiring; packaged popup close/reopen, physical-DPI output and layout are separate native evidence.

The popup uses the same scene geometry and ray shader through a retained D2D image effect. Its producer regression exercises 4K camera updates with stable image inputs and an allocation budget below 64 KiB per frame, while ensuring previously queued pose constants remain immutable. The real animated D3D output also serves as an independent pixel-coverage oracle for the popup's projected bounds: no visible limb pixel may be skipped by its background shortcut. Native cache lifetime, steady process memory and GPU utilization require separate packaged measurements; these tests do not establish them.

`WorldListStoreTests` links the production Sprout Store into the existing rendering/presentation boundary without
referencing Core or loading a native window. Controlled read completions protect instance binding, same-instance
refresh preservation, recovery feedback and disposal even when the reader ignores cancellation. A queued scheduler
and reactive observations additionally protect failure→new-target and A→pending-B→A atomicity before Loading commits,
not only after draining the queue. Those variants were observed failing on the original Store binding before its fix. The reader delegate
substitutes world discovery; these cases do not establish Core I/O, native list recycling or package behavior.

`HistoryBadgeTests` in the Assistant tests links the actual immutable history projection. Mixed typed turn snapshots
protect pending-input/responding/queued/unread priority and read receipts bound to both terminal identity and update.
They do not prove native widget identity or menu targeting; those require separate packaged UIA evidence.

The Windows-only `Rodel.MinecraftLauncher.App.Tests` project source-links the actual App state owners and generated
App resources, references real Core, and serializes reactive continuations on a test-owned context without launching
an application window. Native projection and control-lifetime cases additionally use the aliased App assembly,
not copied UI layouts. The test executable does not run the App's packaging targets; test-project PRI generation
is disabled because these tests do not consume packaged MRT resources.

`WorldCountLocalizationTests` crosses the actual Windows ICU cardinal-rule bindings and generated App resources.
It protects displayed-language switching, UTF-8 locale/UTF-16 keyword marshalling, English counts and the additional
Russian, Polish and Arabic categories with a different numeric culture. It uses no ICU substitute; packaged footer
text and accessibility wiring still require their separate NativeAOT/UIA validation.

`AiConfigurationOwnershipTests` protects window/project borrowing, eligible default seeding, independent chat and
configuration errors, the shared request gate, and non-cooperating artwork completion after disposal.
`AiSubmissionTests` holds the real Core services lease to check immediate first-message acceptance before preparation,
then verifies one durable stopped turn with the selected instance and permissions, or a readable preparation rejection
that retries without consuming a newer draft. It uses isolated storage and a loopback-only model address; packaged
rendering and the response indicator require their separate UIA validation.
`ContentInstallSelectionTests` controls candidate/review completions to protect target/version invalidation,
one-shot frozen bindings, review ownership and disposal. Its planner substitute does not establish real Core planning
or installation. `PreferencePersistenceTests` uses actual Core settings files with controlled write barriers/failures
to protect merge convergence, category editing deferral, unknown overrides, inheritance removal, failed-write retry,
and asynchronous disposal. These tests complement—not replace—independent NativeAOT/MSIX UIA evidence.

`PackImportSelectionTests` inspects real synthetic MRPACK and ZIP archives through the actual creation controller and
Core parser. Changing archives updates an automatically suggested name, preserves a player-entered name and starts
no installation job. This protects the source-selection regression reproduced in the packaged form; it does not
simulate a Windows drag gesture or a complete pack installation.

`GameVersionSelectionTests` links the shared catalog projection. Interleaved manifest types protect grouping by metadata, exact version identity after inserting nonselectable headings, unknown-type retention, search headings and an unchanged source snapshot. Packaged UIA separately checks the real disabled headings and selection callbacks.

`PopupPreparationCancellationTests` links the real popup close policy. Controlled owner notifications distinguish cancellation acknowledgment from terminal completion, close successful submissions before follow-up refreshes, protect retry after cancellation failure, retain picker/report guards, and suppress repeated or late closure after disposal. It does not replace packaged UIA close/Esc routing or actual Core job cancellation checks. Run with the App test project and `--filter "FullyQualifiedName~PopupPreparationCancellationTests"`.

`LauncherLogExportTests` exports real activity and rotated files without a game, independently reads the ZIP and
checks credential redaction, retained scope and continued export beside a locked/oversized source.
`LogExportSelectionTests` exercises the real App controller: launcher-only export, exact destination replacement,
instance grouping and invalidation of a previous run selection. Its historical game records are synthetic.
`LauncherLogWorkflowTests` crosses actual CLI processes, including NativeAOT via `RODEL_TEST_CLI_EXECUTABLE`, and
protects the session-free command, generated response metadata and preservation of an existing destination.

`HomeObservationTests` source-links the actual home controller and its dependencies. Real empty Core reads and
synthetic launch snapshots protect initial observation, quiet equivalent reads, instance removal, byte progress,
cancellation ownership, clock-derived window-wait updates and one-shot verified-window notification.
An isolated Core session record also checks that a crash from before the App process started stays in history,
while an exit during the current run reaches the home error card even if the game started earlier.
The cases cover state reads and publication. The polling timer, native window and idle GPU measurements belong
to packaged validation.

`InstanceLoadingTests` and `InstanceListLifetimeTests` protect registration-first publication, shared detail reads,
independent waiter cancellation, refresh/retry, and immediate removal despite late results or progress. Isolated Core
files and controlled producers establish the state contracts; packaged startup and tab/scroll retention require
separate native UI validation.

`GameWindowReturnTests` feeds an owned selected game and another running instance through the real home publisher and return policy: only the last exit permits one return. Initial/failed observations, unpublished/queued launches and manual cancellation preserve the intended behavior. These state tests do not simulate an OS tray icon; separate NativeAOT/MSIX validation launches two actual isolated clients and checks the real window after each exit.

```powershell
dotnet test --project test\Rodel.MinecraftLauncher.App.Tests\Rodel.MinecraftLauncher.App.Tests.csproj -c Debug --report-trx
dotnet format test\Rodel.MinecraftLauncher.App.Tests\Rodel.MinecraftLauncher.App.Tests.csproj --no-restore --severity info --verify-no-changes
```

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Rendering.Tests/Rodel.MinecraftLauncher.Rendering.Tests.csproj -c Release --report-trx
dotnet format test/Rodel.MinecraftLauncher.Rendering.Tests/Rodel.MinecraftLauncher.Rendering.Tests.csproj --no-restore --severity info --verify-no-changes
```

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj -c Release --report-trx
dotnet test --project test/Rodel.MinecraftLauncher.Cli.Tests/Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "TestCategory!=Live" --report-trx
dotnet test --project test/Rodel.MinecraftLauncher.Assistant.Tests/Rodel.MinecraftLauncher.Assistant.Tests.csproj -c Release --report-trx
dotnet test --project test/Rodel.Minecraft.Skins.Tests/Rodel.Minecraft.Skins.Tests.csproj -c Release --report-trx
```

The Core test project also links the small App-only avatar preference store and square-crop model for focused behavioral tests, without loading Sprout or the App. `AccountAppearanceTests` checks per-account persistence, replacement rollback/retry when the preference write fails, and centered/clamped crops for wide and tall inputs. It does not substitute for packaged UI or image-codec validation.

`MicrosoftAuthenticationRecoveryTests` checks the launcher-owned distinction between a failed WAM runtime, required reauthorization, other provider failures and cancellation, including exclusion of raw provider messages. Only MSAL exceptions are synthetic; these cases do not open Windows accounts or establish a successful live WAM/Xbox/Minecraft sign-in.

`MicrosoftSessionReuseTests` crosses real DPAPI account storage and the launch-credential path: repeated service opens reuse a valid game token without changing account files, cancellation still wins, and expired/near-expiry, missing/malformed expiry, wrong-player and empty-token caches enter refresh. Explicit refresh still authenticates and a known login-required account cannot revive its old cache. The fixture has no MSAL account, making an unwanted refresh fail locally without contacting public services; this does not establish live sign-in or Minecraft gameplay. Run the Core test command with `--filter FullyQualifiedName~MicrosoftSessionReuseTests` to select these cases.

`AccountSelectionTests` crosses real registration/selection/removal storage to select the first account, preserve later choices including explicit clearing, and reset after all accounts are removed. The existing external device-flow case additionally checks no default before role confirmation and automatic selection of the first verified profile. App's initial offline-name prompt, cancellation and continuation require separate packaged UIA verification.

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj -c Release --filter "FullyQualifiedName~AccountAppearanceTests" --report-trx
```

`GameLogTests` also crosses the actual session monitor and a file-barrier-controlled console subprocess to retain startup milestones after normal exit, deduplicate repeated markers, keep arbitrary console text out of the timing file, and include the evidence in an explicit export. Printed window/graphics markers cannot substitute for a verified process window. The producer is not Minecraft; actual game startup performance requires separate isolated measurements. `GameLogWorkflowTests` retains its independent NativeAOT-capable console retention/export checks; lightweight startup timing files are distinct from full console captures.

The repository uses the .NET 11 Microsoft.Testing.Platform runner. Pass test-platform options directly, without an additional `--` separator.

`AccessDefaultsTests` crosses the real AI configuration store: changing the new-chat mode survives reopening and independent capability edits, existing conversation bytes stay unchanged, and invalid/stale edits cannot partially write capability changes. It uses an isolated `AiSandbox`, without model requests or a persistence mock. UI creation, layout and native automation remain separate validation boundaries.

`JobReadTests` crosses real task-record and progress-sidecar storage. It checks lossless unknown/null/malformed
observation fields through task completion and verifies that malformed request parameters remain inspectable
but never reach an executor. Pack preparation's existing cancellation case also checks that an earlier typed
progress snapshot does not change when the mutable workflow is canceled. These are storage/dispatch contracts,
not evidence of a running game or native progress presentation.

`GameLogTests` protects the one-MiB per-stream memory bound, absolute cursors after eviction, UTF-8 reads through the
current-user endpoint, and seven-day cleanup ownership/failure reporting. `GameLogWorkflowTests` uses a real
synthetic stdout/stderr producer and the actual detached CLI monitor: live reads and both export formats,
normal versus abnormal exit, explicit stop, and independent crash-snapshot write failure. It checks that
normal capture creates no log/status files. These are process/storage/IPC tests, not Minecraft gameplay or FPS measurements.
`LaunchExperienceTests` also exercises generated CsWin32 window ownership and normal close behavior.
Its cleanup waits for the owned monitor process to exit, not merely for a terminal session record that can precede handle release.

`ProtectedStoreTests` exercises the account JSON envelope through real current-user DPAPI and rejects a damaged
ciphertext instead of returning plaintext or stale values. `ServerReadTests` includes an unmanaged synthetic
wide-record chain: `DnsQuery_W` metadata uses an ANSI pointer alias, so the test protects UTF-16 target decoding,
record filtering and SRV fields without a public DNS dependency. Core uses a concrete build CPU for these native
layouts and accepts the caller's RID through project references. Assistant credential, scoped-file and App-control
cases cover real protected storage, pinned handles and current-user pipe/process identities.

Assistant tests use isolated real Core storage, loopback HTTP fixtures and a substituted native-engine boundary.
Conversation queue cases hold a real loopback request open before enqueueing, then verify FIFO predecessor history,
idempotent receipts, cancellation before dispatch and deletion of an active conversation with queued work.
Transcript timing checks use explicit event clocks. Question projection checks join tool/prompt events by host invocation,
retain distinct answers and skips despite identical question text or reused provider call IDs, preserve failed/canceled
questions and omit human wait duration. They do not substitute for native animation or visual validation.
They need no accounts, API keys, downloaded models, inference daemon or managed MCP binaries.
Their scope and source references are described in [Assistant testing](ASSISTANT.md).
Operation authorization cases distinguish one-call and same-tool conversation grants, revoke them on mode changes,
and preserve unrelated-tool and directory boundaries. Reviewed writes ask once in safe mode and do not ask in
allow-all mode. The running-job read case uses real persisted job state and cancels at any unwanted observation
checkpoint; `job_show` must return the snapshot without polling. Mutation observation cases use persisted jobs
and the actual executor wait events to exclude a known live job's duration from the AI execution budget, restore
the remaining budget on completion and preserve caller cancellation without canceling or replaying the job.
An unobservable owner returns uncertainty instead of entering an unbounded wait. Deadline cases also exercise
the real timer across human waits. MCP dispatch is substituted where the case protects authorization or
observation rather than an external effect; these checks do not establish packaged split-button interaction.
The local message case substitutes only `IChatClient`, exercises the real MEAI aggregation and checks protected
continuation, function results, declaration-only tools and JSON argument fidelity. Complete-message persistence
is checked independently against same-binding reuse, revision changes, edits and public archive projection.

CLI AI cases start their own Core host and actual launcher stdio tools. They cover redirected prompts,
temporary request parameters, resumed observation and owned-client exit using a synthetic HTTP model.
`SkinAssistantWorkflowTests` uses that same real MEAI/CLI/MCP pipeline with scripted model replies to check both
host-reviewed candidates and scope-bound direct skin editing. It checks durable pixels, receipts and
ordinary undo; it does not establish a real model's drawing quality. `SkinCandidateWorkflowTests` additionally
checks whole-document direct grants cannot silently override read-only or partial restrictions.
`ProgressiveToolWorkflowTests` also observes the actual context-read result sent to the HTTP model:
existing direct editor grants disclose effective layer-creation authority, while candidate-only grants retain their layer restriction. The actual skin system message must not also claim that generic safe-mode write approval is required; ordinary launcher messages retain that rule.
The direct CLI skin workflow runs in safe mode without a broad operation grant; scoped skin tools must commit and return their real preview without confirmation events.
`SkinAiConversationTests` links the actual skin workspace and shared App chat owner to Core storage: exact review replaces the old chat with one scoped draft, stale review retains it, unrelated chats and the full skin project remain unchanged. Its active-response case holds a real loopback provider request, waits for the shared UI observer to consume `model_started`, and requires that actual observer task to finish before replacement returns; it protects against deleted-history errors leaking into the new chat. These cases do not load a native window and do not establish packaged UIA or capture acceptance.
`SkinActivityContentTests` checks strictly consecutive tool groups separated by thinking and responses, recorded face/edit targets,
and the distinction between an old continued attempt and an unrelated new turn. The layered candidate case checks
real duplicate/hide/preserve behavior and one named undo step; model compliance with the skill is separate live evidence.

`ConversationWorkflowTests.RunQueuedNow_InterruptsCurrent_PreservesRemainingOrderAndExecutionHistory` holds actual loopback model streams to check interruption, duplicate-click idempotence, preserved queue order and context following execution order. It exercises the real coordinator and persistent events; it does not establish physical IME key behavior or model drawing quality.
`ConversationWorkflowTests.QueuedInput_ContinuesAtNextModelBoundary_OnlyForItsOwnConversation` holds a real ask_user in the middle of a MEAI tool batch, then verifies that the next HTTP request receives the queued clarification and every completed tool result. Its foreign-only variant protects conversation isolation; canceled/later inputs stay excluded and each submission retains its permission snapshot. The endpoint substitutes provider output only; these checks do not establish native scrolling.
`ReasoningEventTests` covers both Responses summaries and DeepSeek plaintext reasoning, including final-only fallback
and a real HTTP barrier that withholds completion until a durable delta is observed. It also rejects duplicated
final-snapshot text and exposing opaque continuation as thinking.
Reference-image cases check complete MEAI persistence/provider switching and all four image wire formats.
Tool-image cases additionally check full-batch result ordering, correlation, native PNG byte fidelity,
current-image preservation during actual compaction, exact archive-image retrieval and both persisted
interruption windows. The local case checks the SDK input boundary; the SDK/native engine is substituted.
Public event/turn/effect projections retain metadata while private intents retain recoverable image bodies.
The CLI case crosses actual file snapshot, host, HTTP and failure/retry boundaries, including three-image
admission, same-client-key receipt recovery, changed-input conflict and unchanged original image bytes.
Its loopback provider and small synthetic images do not establish a real model's visual understanding.
The four-protocol probe/discovery workflow also checks typed request/result serialization, explicit nulls,
omitted optional request fields and unchanged saved bindings. The same CLI cases can target a published
NativeAOT executable with `RODEL_TEST_CLI_EXECUTABLE`.
`ModelSelectionTests` additionally protects the documented `/v1` routes for the bare official OpenAI origin,
while preserving explicit version paths, third-party roots and nonsecret queries. These routing cases do not
contact a public provider or establish that a private key is valid; native dialog sorting and failure recovery
are verified separately with an isolated packaged App and loopback provider.
`test\Common` contains only shared endpoint source, linked by both projects. No test reads `.local`;
that directory is ignored and must not become a source dependency or a committed fixture store.

## MEAI Pipeline and Evaluation

Providers remain launcher-owned `IChatClient` implementations; these tests do not require supplier SDKs.
The actual agent loop is MEAI `FunctionInvokingChatClient`, not a launcher-owned generation/tool loop.
`AgentLoopProtocolTests` covers four-protocol multi-tool continuation, signatures, the strict shared model-call
budget, non-executable incomplete/refused calls, audited rejection and compaction after completed tools.
`AgentLoopGuardTests` covers real durable approval waits/cancellation, fatal storage failure before remaining
effects, and ambient-agent telemetry isolation. Core permission/effect tests remain separate from these driver cases.
Structured selection uses the source-generated schema in the already-budgeted packet and MEAI typed parsing.
The CLI context-rejection case crosses actual host/schema/parser boundaries and can target the NativeAOT
executable with the same `RODEL_TEST_CLI_EXECUTABLE` setting; selection batches and the single ordinary retry
share its explicit four-call budget rather than assuming one selector request.

`AiTelemetryTests` installs only in-process Activity/Meter listeners. It checks actual usage/error records and
excludes messages, tool values/descriptions, keys, endpoint query and absolute/file-URI model references in request
options and responses. Its explicitly
unsafe synthetic control demonstrates that the privacy oracle would detect message capture; it never enables
that setting for the product or sends telemetry to a collector.

The evaluation/reporting package is test-only. Run the synthetic Chinese evidence calibration with:

```powershell
dotnet test --project test\Rodel.MinecraftLauncher.Assistant.Tests\Rodel.MinecraftLauncher.Assistant.Tests.csproj -c Release --filter "FullyQualifiedName~ContextEvaluationTests" --report-trx
```

The test prints a unique directory under its output `TestResults\meai-evaluation`, attaches official scenario
JSON files to the MSTest run and independently reopens them through the MEAI result store. The report contains
the original fixture evidence, candidate text, exact-retention metric and interpretation. Deliberately broken
negation, identifier and unknown-effect candidates are expected to have failed metrics; that is calibration,
not a failing product test. This is **not** a semantic model-quality evaluation: no judge model/account is used,
and response caching is explicitly off. Source/hash/permission/effect assertions remain separate.
Reports and TRX files stay ignored; no evaluation library or report is bundled with the launcher.

## Real Model Integration Tests

The existing CLI test project also contains opt-in C# MSTest cases under `Live`;
there is no PowerShell/Python test runner, wrapper project or dependency on local probe scripts.
They use the same CLI process harness, actual Core data, real launcher MCP tools and owned-process cleanup.
The real cases are serialized because they share model/GPU resources and live-provider quotas.
The current target is this Windows device, not a CI/remote-host matrix. The default run builds this repository's
CLI automatically; no separately installed test executable, rin server, API-format proxy, Python/Node runner,
App installation or VM is required. Existing model weights and online account credentials are normal product inputs.

```powershell
$env:RODEL_TEST_LIVE = '1'
$env:RODEL_TEST_LOCAL_MODEL = 'hf:ggml-org/Qwen3-0.6B-GGUF:Q4_0'
# Select an actual online account/model explicitly. Set its secret in the environment, never here or in arguments.
$env:RODEL_TEST_ONLINE_PROTOCOL = 'openai-chat'
$env:RODEL_TEST_ONLINE_ENDPOINT = 'https://api.deepseek.com/v1/'
$env:RODEL_TEST_ONLINE_MODEL = 'deepseek-flash'
$env:RODEL_TEST_ONLINE_CREDENTIAL_ENV = 'DEEPSEEK_API_KEY'

dotnet test --project test\Rodel.MinecraftLauncher.Cli.Tests\Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "TestCategory=LiveLocal" --report-trx
dotnet test --project test\Rodel.MinecraftLauncher.Cli.Tests\Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "TestCategory=LiveLocalTools" --report-trx
dotnet test --project test\Rodel.MinecraftLauncher.Cli.Tests\Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "TestCategory=LiveOnline" --report-trx
dotnet test --project test\Rodel.MinecraftLauncher.Cli.Tests\Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "TestCategory=LiveMixed" --report-trx
```

| Category | Compound behavior and real oracle |
| --- | --- |
| `LiveLocal` | Runs stored-reference and direct-file variants using the caller-selected existing GGUF. Generates independently expected answers, checks engine reuse, cancels only after persisted text in an active turn, retains partial events and unloads. The file variant resolves the source through the SDK without copying or importing it. No fake engine or inference daemon. |
| `LiveLocalTools` | The selected actual local model chooses real launcher MCP mutation/readback, preserves unrelated persisted settings, then continues read-only without another write and unloads. Unlike generation-only coverage, this crosses the SDK/native tool-call translation boundary. |
| `LiveOnline` | Two cases: actual provider-driven settings mutation/readback, preserved unrelated fields, client-key reuse and read-only continuation; and cancellation after real persisted partial text followed by successful recovery in the same conversation. No fixed HTTP model replies. |
| `LiveMixed` | One conversation switches local → real online model/tool → local. Checks earlier context and independently observed tool values survive both transitions without reloading the local owner. Requires both sets of prerequisites. |

There are five methods and six data-expanded cases across these four categories. `RODEL_TEST_CLI_EXECUTABLE` optionally selects
this product's published NativeAOT CLI for the same cases; it is not an external-tool prerequisite.
Otherwise they use the normal managed build. Select an existing GGUF reference or absolute file path with `RODEL_TEST_LOCAL_MODEL`;
the stored-reference variant requires that reference in the SDK store, and the file variant resolves it to the existing file.
When an absolute path is supplied, only the file variant runs; the stored-reference variant is explicitly skipped, not duplicated.
Tests do not download models, alter global model configuration, start rin, install App packages or use a VM.
Online cases consume actual API quota. Temporary data roots, processes and model owners belong only to that case.
The isolated launcher bindings use temperature zero; local cases normally leave the context override unset to exercise Core's
16K application default. `LiveLocalTools` verifies the saved override is absent and the actual turn reports that default,
rather than passing a hidden test-only `--context-length`. No personal model-store settings or SDK global defaults are edited.
Select a real model suitable for the requested scenario: generation with a small fixture model
does not establish autonomous tool quality. The actual local tool workflow has passed with Qwen3.8-27B, not just Qwen3-0.6B.
Model answers are still checked against exact independent values, not relaxed aliases or repeated until passing.
Real-model failures remain failures: a pass is not an all-model quality or losslessness guarantee.

No opt-in means no live request or weight load. Missing explicit prerequisites are reported as inconclusive/skipped,
not substituted successes. Selecting only disabled/missing live cases produces zero executed tests
(Microsoft.Testing.Platform exit 8), which is **not** real integration acceptance.
After a live run unset `RODEL_TEST_LIVE` or keep the default `TestCategory!=Live` filter for routine regressions.
## Java Reporting Fixtures

The small [Java reporting worker](Fixtures/JavaProbe/JavaProbe.csproj) is a separate executable project explicitly referenced and deployed by each Java test project's own build target. Java helpers belong to their respective test projects' `Infrastructure` directories, without linked Java helper sources or shared target imports. The worker substitutes Java version/property output to test real subprocesses, cache invalidation, cancellation and registered-runtime ownership without requiring an installed JDK. Core tests own service behavior; CLI tests retain cross-host contracts and permanent-removal file effects instead of duplicating every Core scenario. Injected catalog HTTP responses are synthetic and do not establish public-server or JDK-download availability. These tests do not establish JVM or Minecraft compatibility.

Each scenario owns its temporary files and reporting workers. Full `DiscoverAsync` tests, tagged `HostDiscovery`, still enumerate and may execute installed Java candidates from the host's environment, registry and known directories; they are host-discovery integration tests, not hermetic subprocess tests. Their assertions target the synthetic runtimes and do not require particular host versions or ordering. Use a controlled Windows environment for those cases, or exclude the category when host probing is undesirable. Tests do not alter the registry or system Java installations.

Tests that hold a reporting barrier cancel and await their in-flight operations in `finally` before disposing runtime files, including when setup or assertions fail.

## Localization

Run `./scripts/check-localization.ps1` to validate all fifteen App, CLI and AI command resource sets, including missing/empty/extra keys, composite format tokens and executable option names. `LauncherLanguageTests` protects Chinese script/region resolution, shared Portuguese translations, unsupported-language fallback and game codes. `LanguageSelectionTests` crosses actual CLI processes for all fifteen explicit selections, regional environment fallback, precedence and the `--` positional boundary; it also accepts the normal NativeAOT CLI via `RODEL_TEST_CLI_EXECUTABLE`. Packaged live switching and persisted App preferences require separate UIA verification.

## Filter Tests

Run integration tests:

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj -c Release --filter "FullyQualifiedName~JavaDiscoveryTests" --report-trx
dotnet test --project test/Rodel.MinecraftLauncher.Cli.Tests/Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "TestCategory=Integration" --report-trx
```

Use MSTest's `--filter` expressions to select other categories, classes, or methods as needed.

Run independent skin drawing tests without building the launcher or CLI:

The skin test project has its own [execution and isolation notes](Rodel.Minecraft.Skins.Tests/README.md).

```powershell
dotnet test --project test\Rodel.Minecraft.Skins.Tests\Rodel.Minecraft.Skins.Tests.csproj -c Release --report-trx
```

Run the isolated skin MCP profile contracts through actual CLI/stdio processes:

```powershell
dotnet test --project test\Rodel.MinecraftLauncher.Cli.Tests\Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~Skin" --report-trx
```

The library cases use small synthetic textures and the real editor, with no launcher, CLI or MCP reference. They protect pixel/alpha semantics, cuboid-edge strokes, mask-aware transfers, complete paint-layer history, model-space ray picking and the absence of an embedded HTML client. MCP cases use actual CLI/Core processes and their storage adapter, including masks, clipboard data, PNG previews and explicit visibility settings. Neither suite starts Minecraft, authenticates an account, installs a skin mod or proves that another player can see a skin. Native Sprout interaction and actual game loading are separate validation boundaries.

`SkinEditorMcpTests` crosses real CLI/stdio for 128 templates, import/history recovery and a host-bound candidate connection. `SkinCandidateWorkflowTests` uses actual Core storage for scope expansion rejection, stale/revoked/rejected candidates, no-op acceptance, frozen content, publication failure and two accepting clients synchronized at a held lease. Candidate pixels and receipts share one atomic project record; retries after undo must not reapply the candidate. These Core calls remain managed when other CLI tests select a NativeAOT target. `SkinToolAuthorizationTests` uses the real coordinator, policy, editor and journal; its safe direct-grant case dispatches actual Core tools, while selected authorization/journal cases substitute dispatch. A real confirmation event immediately cancels the candidate variant to prove it has not published pixels. It does not call a real model.

`SkinStorageWorkflowTests` lives in the launcher test project because it references Core. It checks two Core service clients against a held real resource lease and a read-only target publication failure with temporary-file observation. These cases execute in the managed test host, even when `RODEL_TEST_CLI_EXECUTABLE` selects a NativeAOT executable for other cases; they do not claim two MCP processes or power-loss recovery.

`SkinProjectFileTests` additionally crosses the actual `skin save-as` CLI and reopens its editable copy, retaining a redo
branch while preserving source bytes. It covers cancellation, confirmed replacement and stale-save rejection.
The shared-texture archive case checks that unchanged history does not duplicate pixel blobs, decoded mutable arrays
do not alias current/history state, and corrupt texture data is rejected. Current-format plain JSON input remains readable;
the compressed container does not change library document or CLI/MCP JSON contracts.

`CatalogSummaryWorkflowTests` exercises offline game, loader, and Java catalogs through actual CLI processes. Synthetic cache files verify that typed summary serialization preserves provider extensions, explicit nulls, optional-field absence, and source-cache bytes. The Java import/list/removal workflow also checks registration extensions through the actual managed-list command. These cases require no public metadata service and do not establish online catalog availability.

Run Core tests without system-wide Java discovery:

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj -c Release --filter "TestCategory!=HostDiscovery" --report-trx
```

## Configuration

`InstallationDownloadTests` uses real CLI/Core and loopback HTTP barriers to establish overlapping client/library/native/logging requests, enforce configured concurrency, verify exact bytes and reuse, and cancel a stalled sibling after a permanent failure before repairing the same instance. It never downloads Minecraft.

`LaunchExperienceTests` uses the reporting worker's real hidden-then-visible Win32 window to distinguish process creation from visible-window evidence, including exit before appearance. It checks plan-only immutability and actual pre-launch `lang` updates for Chinese, English, Arabic, legacy resource spelling and omitted overrides while preserving UTF-8 BOM, line endings and unrelated options. Saved fullscreen=true must yield to the launcher default, explicit fullscreen, persisted global choice and one-shot override. A real read-only replacement failure must preserve the options and report language/window-mode warnings without preventing startup. These cases also accept `RODEL_TEST_CLI_EXECUTABLE`; the worker is not a JVM and does not prove Minecraft rendering, translated menus or world readiness.

| Environment variable | Purpose | Default |
| --- | --- | --- |
| `RODEL_TEST_CLI_EXECUTABLE` | Absolute path to an existing CLI executable to test instead of the managed build. Publishing is a separate step. | Run the CLI built with the test project through the .NET host. |
| `RODEL_TEST_CODEX_EXECUTABLE` | Absolute path to a Codex executable for `CodexIntegrationWorkflowTests`. | Without this variable, the test reports inconclusive. |

Unset `RODEL_TEST_CLI_EXECUTABLE` to return to the managed target. This variable affects only the CLI tests. The CLI harness uses a separate temporary data directory for each scenario, passes process arguments without shell interpolation, and sets the child process UI language to English. Tests do not require personal configuration or game data.

`CodexIntegrationWorkflowTests` registers, rebinds, and removes an owned MCP entry through the launcher CLI and a real Codex executable. It uses an isolated Codex home and checks that an unrelated server's comments, empty arguments, integer timeout, and extension field survive unchanged. Use the CLI test command with `--filter FullyQualifiedName~CodexIntegrationWorkflowTests`.

`ClaudeIntegrationWorkflowTests` runs the launcher CLI with an isolated Claude command fixture that rejects the candidate add. It verifies the unchanged configuration, connects to the previous binding through real stdio MCP, and checks that a successful retry changes only the user-scope value. It requires no Claude subscription or executable. Use `--filter FullyQualifiedName~ClaudeIntegrationWorkflowTests`.

`ExternalFileIntegrationWorkflowTests` runs the actual CLI installation, rebind, and owned removal for Copilot JSON and Harness block and flow YAML. It compares neighboring bytes and uses isolated client homes. Use `--filter FullyQualifiedName~ExternalFileIntegrationWorkflowTests`.

## Results

`--report-trx` writes a TRX report to the test runner's results directory and prints its location. Test results, build outputs, and temporary data are not source files and should not be committed.

## Contributing

Check formatting and suggestion-level style rules, including collection-expression diagnostics:

```powershell
dotnet format test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj --no-restore --severity info --verify-no-changes
dotnet format test/Rodel.MinecraftLauncher.Cli.Tests/Rodel.MinecraftLauncher.Cli.Tests.csproj --no-restore --severity info --verify-no-changes
dotnet format test/Rodel.MinecraftLauncher.Assistant.Tests/Rodel.MinecraftLauncher.Assistant.Tests.csproj --no-restore --severity info --verify-no-changes
dotnet format test\Rodel.Minecraft.Skins.Tests\Rodel.Minecraft.Skins.Tests.csproj --no-restore --severity info --verify-no-changes
```

Follow the [Test Design Guidelines](GUIDELINES.md) when adding, changing, or reviewing tests. Keep test sources and necessary small synthetic fixtures under version control; do not depend on untracked verification scripts or machine-specific assets.
`TranscriptFollowTests` links the UI-independent follow policy and checks that upward reading intent survives automatic growth/clamping until a deliberate return to the bottom. `TranscriptLayoutTests` in the rendering project exercises the actual per-turn layout with variable-height widgets: unseen history slots do not inherit a tall turn's height, realization stays bounded, expanded/collapsed slots survive revisiting, and the pinned Sprout inline list reports its complete height during measurement. These headless checks do not prove physical wheel smoothness or focus-highlight latency in a packaged window. The title metadata fixture returns reasoning-only truncation for a 128-token request, distinguishing the observed short-budget failure from successful model summaries and preserving concurrent manual renames.

`UserPromptWorkflowTests`跨真实Core/MEAI/存储/HTTP验证单选、多选、自由补充、明确跳过、停止、执行超时暂停、幂等/冲突、过期问题与无拥有者恢复；仅远端模型用loopback替代。`ImageOnlyTurnTests`验证有效图片的空文本消息可进入模型请求，真正空草稿仍拒绝；已有协议图像与CLI冻结/失败继续用例继续保护原字节。`UserPromptCommandTests`跨实际CLI命令树、JSON输入和后台Core验证自由文本答案及关联ID；同一用例可用`RODEL_TEST_CLI_EXECUTABLE`指向正常NativeAOT CLI。以上不声称完成新问答卡及剪贴板的原生UIA验收。

普通聊天的问答回归同时检查system交互规则已注入；SkinAssistantWorkflowTests检查皮肤scope不暴露ask_user或普通聊天问答规则，保持绘图职责。模型是否会自然选择工具另有真实模型证据，不能由脚本模型按既定工具名回包推定。

AI repair cases use the real Core incident store, reversible settings/content operations and the actual CLI/MEAI/MCP path. They check immutable failure evidence, stale-plan rejection, rollback conflicts/inheritance, tagged verification-run ownership, quick-play suppression, no per-step approval, and reuse of ask_user. A Java reporting worker and scripted HTTP provider replace Minecraft and model behavior; a normal reporting-process exit must remain inconclusive. The direct in-process Core case additionally protects receipt handling that CLI JSON transport can mask. These cases do not establish real Minecraft initialization or in-world reproduction.

## External authentication

`ExternalIdentityTests` uses only a substituted HTTP peer, with real Core ownership, protected attempts and DPAPI storage.
It covers role/source isolation, cancellation at an observed in-flight boundary, identity-preserving reauthentication,
known expiration versus remote failures, escaped Unicode diagnostic redaction, and LittleSkin polling/role-token exchange.
`ExternalLoginComponentTests` protects publication rollback and owned-byte corruption detection with synthetic, non-executed JARs.
`ExternalLaunchTests` crosses the real planner/monitor/subprocess boundary with the reporting Java worker, and checks both
local and explicit-server task admission retain their original account after defaults change. The worker is not a JVM or Minecraft.

`ExternalAccountWorkflowTests` runs the real CLI against a loopback Yggdrasil peer for stdin-only credentials, cross-process
protected role selection, default preservation and refresh. Its separate MCP case ensures credential input never consumes
JSON-RPC stdin and the transport remains usable after rejection. These cases support `RODEL_TEST_CLI_EXECUTABLE` for NativeAOT.

```powershell
dotnet test --project test/Rodel.MinecraftLauncher.Core.Tests/Rodel.MinecraftLauncher.Core.Tests.csproj -c Release --filter "FullyQualifiedName~External|FullyQualifiedName~ServerLaunch" --report-trx
dotnet test --project test/Rodel.MinecraftLauncher.Cli.Tests/Rodel.MinecraftLauncher.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ExternalAccountWorkflowTests" --report-trx
```

A valid LittleSkin device-flow client remains necessary for public OAuth integration; fixture success does not establish
real authorization, skin visibility or multiplayer login. Packaged UIA and actual JVM/agent behavior require separate validation.

## Game Bridge

`GameBridgeTests` crosses actual bounded TCP, current-user pipes, HMAC challenges and Core durable intents. It checks remote refusal before dispatch, interrupted receipt recovery without replay, malformed handshakes with subsequent valid recovery and preservation of terminal receipts against late queued observations. The shared peer substitutes only Java; it is not a Minecraft compatibility test.

`GameBridgeJavaTests` compiles and executes the production Java sources with controlled executors and small test-only Fabric API sentinels. Its cases distinguish queued cancellation, changed world identity before execution, TCP remote authority, failure after mutation, Fabric bootstrap timing, interface-owned mappings and accidental client static initialization. `GameBridgeMcpTests` uses real stdio MCP, canonical CLI routing and the actual MEAI loop with scripted HTTP model replies; a separate CLI reads the durable effect. Assistant cases protect operation permission, world-bound turns and unresolved effects. These controlled peers/models do not establish real game/model quality.

`GameAiConversationTests` crosses the game monitor and actual Core/MEAI with a loopback model: independent save slots, restored context, idempotent submissions/resets, physical history removal with unchanged game files, shared console/panel context with overlapping submissions observed by their own identity and final-only console text, stale-world rejection and game-only plain-text instructions. Its operation variants execute a real MEAI tool call without human approval under the unchanged safe default, while a remote world still refuses before native dispatch; only provider responses and the Java peer are substituted. `GameAiWorkflowTests` exercises the typed CLI ingress without a model; it also supports `RODEL_TEST_CLI_EXECUTABLE` for NativeAOT. Reverse transport tests perform a nested game observation before replying to the original game request; the Java ingress case swaps the world while queued and disconnects an outstanding request without replay. These do not claim game UI or real-model acceptance. The isolated combined client tests separately check `/rai` Enter, retained chat/draft beyond 200 native ticks, fresh replacement age without changing unrelated messages, bare `/rai` leaving its AI panel open, and native Esc/ordinary-command behavior; no model is connected in those UI checks.

Representative real-MC/NativeAOT and packaged UIA evidence is documented separately in [Game bridge](../doc/JAVA-GAME-BRIDGE.md). Downloaded binaries, saves and reports stay outside Git. Build outputs contain test classes only; no test fixture is bundled in the agent.

Game bridge regressions also JVM-verify the rewritten getter, preserve physical keys and revoke bridge input at the native read boundary before a lifecycle poll. Controlled game ticks distinguish immediate movement from delayed attack/use and final release. Core rejects commands when the real world reports cheats disabled, and remote control/command/container actions never reach the peer. Actual MCP/CLI omission tests protect optional paging/input defaults; AI root identity permits new observations after travel/respawn without transferring authority to another local server. Representative gameplay, original agent-order failures/retests and native statistics remain separate real-game evidence.

`McpContractTests.DiscoveryProfile_LoadsOneSchema_AndExecutesTypedCoreWithoutPublishingDomainTools` connects the real stdio host through the official SDK, reads one actual schema and verifies a typed Core write plus rejection without fallback. `ContextSummaryTests` reuses its existing malformed-instance and mixed-job fixtures across CLI and Core/MCP aggregation, protecting section failure envelopes and scoped continuation semantics. These checks do not establish external-client permission policy.

`DiscoveryTask_AfterMcpExit_CompletesInDetachedCliHost` holds a real source-directory lease across MCP process shutdown, then releases it and observes the original detached CLI job copy its marker. The barrier makes process-lifetime regression observable even on fast machines. It does not establish download or crash-recovery policy.
