# Rodel Minecraft Launcher App

使用Sprout构建的Windows原生启动器界面，页面直接消费Core服务和typed后台任务。AI回合也在Core中执行；其启动器工具由Core进程内受控工具直接执行，不在页面复制命令解析、授权或模型循环。

## 构建与运行

需要Windows、根global.json指定的.NET SDK、Windows SDK与NativeAOT C++构建工具，以及用于构建内置模组的JDK25或更新版本。Sprout正式版本及包源以[工程配置](Rodel.MinecraftLauncher.App.csproj)和根[NuGet.Config](../../NuGet.Config)为准；还原时需具有该Azure包源的访问权限。Core内部模型依赖来自已固定的`external\Rodel.Inference`源码submodule，克隆后先执行`git submodule update --init --recursive`。

正式Sprout引用及相关测试工程使用同一固定发布版本，以工程中的PackageReference为准，不在此重复维护版本号。构建使用已声明的包依赖，不自动导入本地联调目标或开发者专属路径。

内置模组和种子适配器的构建还需要JDK25或更新版本。构建脚本依次检查`JAVA_HOME`、`PATH`中的`javac`、用户的`.jdks`与Program Files下的Java/Eclipse Adoptium/Microsoft安装目录，跳过失效或过旧候选；不会修改系统环境变量。也可用`BundledModsJavaHome`、`SeedExplorerJavaHome`及`GameBridgeJavaHome`显式指定；显式目录无效时直接报错，不偷偷改用另一套JDK。种子构建未单独指定时沿用`BundledModsJavaHome`。

在仓库根目录运行：

```powershell
dotnet run --project src/Rodel.MinecraftLauncher.App
dotnet publish src/Rodel.MinecraftLauncher.App -c Release -r win-x64
```

若自动发现不到JDK25，可将`JAVA_HOME`指向有效的安装目录。CsWinRT默认读取`10.0.19041.0`的Windows SDK元数据；若只安装其他版本，可给上述命令增加`-p:CsWinRTWindowsMetadata=<已安装的SDK版本>`，不改变工程的最低Windows版本。首次切换到`win-x64`发布时保留正常还原，不使用缺少该RID目标的旧assets配合`--no-restore`。

默认调试和Store发布使用`SproutPackageFormat=Msix`与`Package.appxmanifest`。run/watch通过Developer Mode注册并以包身份启动；默认publish生成NativeAOT程序及开发签名MSIX。正式Store身份单独配置。

网吧或不能使用Store的电脑可使用单文件EXE安装包。构建机另需Inno Setup 6.7或7及发行使用的公开`RODEL_MICROSOFT_CLIENT_ID`，执行：

```powershell
./scripts/package-exe.ps1 -Architecture x64
# 自动发现不到Inno Setup时增加 -Compiler 'C:\path\to\ISCC.exe'
# 只有较新Windows SDK时增加 -WindowsSdkVersion 10.0.26100.0
```

输出为`artifacts/release/<version>/win-x64/exe/RMCL-<version>-win-x64-setup.exe`及SHA256文件。安装包携带完整unpackaged NativeAOT、Windows App SDK运行库、资源和内置模组，不携带CLI，不要求玩家安装.NET、JDK、MSIX证书或Store。默认安装至`%LOCALAPPDATA%\Programs\RMCL-x64`，不请求管理员权限，创建开始菜单快捷方式，可选桌面快捷方式；卸载保留游戏、存档与偏好。只需把安装EXE放上云盘，到目标电脑下载运行即可。网吧若禁止运行外来EXE，仍受其实际策略限制。

## 数据与偏好

皮肤编辑器顶部“预览”打开手动关闭的模态Popup，渲染区域零内距铺满Popup，关闭图标叠放在视图内部右上角。读取打开时的当前工程合成、完整人物及外层，按MC标准步态无缝循环；支持拖动观察与滚轮缩放，不改变工程或历史。走路直接使用Sprout的D2D像素shader和一个复用的2048方形画布，真实RGBA纹素与姿态组成57968字节独立常量，不逐帧读回位图，也不经外部纹理的多输入图像管线。按实际DPI计算相机，保留四个独立子像素光线的覆盖抗锯齿；随姿态更新整个人物及各身体盒的投影范围，跳过空白背景与不相关部位，保留两像素AA余量。最多30fps，最小化暂停，关闭停止调度。静态导入/新建预览保留最长边512的按需读回路径。

Core默认数据根为用户目录下的`.rmcl`。设置可选择覆盖目录，重启生效，不自动搬移已有数据；App使用各自部署方式的偏好保存目录选择。独立CLI可用`--data-dir`选择其Core数据根。EXE安装包不包含个人实例、账户或存档，也不会自动携带这些数据到另一台电脑。

应用语言、主题、壁纸、窗口位置及窗口行为由AppStorage按真实包身份选择存储：MSIX继续使用当前包的WinAppSDK LocalSettings；EXE使用`HKCU\Software\Richasy\RMCL`下的分容器偏好，自有文件在`%LOCALAPPDATA%\Richasy\RMCL\LocalState`、缓存在同根`LocalCache`。两种部署不自动迁移或覆盖对方的App偏好。每次启动将当前15种应用语言对应的游戏语言传给Core，仅在真实Java启动前更新游戏`options.txt`的`lang`，不修改已运行游戏或其他游戏选项。Core全局/实例配置使用自己的JSON和SettingsService。损坏或未知WindowPlacement回退默认几何，保留最小客户区800×560 DIP；强制终止不保证保存本次窗口变化。

## 功能入口

账户管理提供微软系统登录、离线与外置身份；发行版内置公开Client ID，不要求玩家注册应用或配置环境变量，配置覆盖与WAM失败后的浏览器替代边界见[CLI说明](../../doc/CLI.md)。首页和实例页提供“本地实例/服务器”分段、收藏/关联、首选客户端、状态图标和直接连接。红底错误显示游戏/加载器原文，以原生SelectableText提供选择及右键复制/全选；卡内展开与无错误卡时的兜底图标互斥。不把构建或配置注入扩称为业务通过。

| 页面 | 职责 |
| --- | --- |
| 首页 | 实例选择、真实启动任务、取消、错误恢复与会话观察 |
| 实例 | 新建原版、完整复制迁入、配置、备份恢复与运行环境 |
| 内容 | 本地内容、世界及实例截图管理，统一Modrinth发现安装、冻结审核与更新 |
| AI助手 | 同壳流式对话、模型/实例选择、历史、停止、精确确认与失败恢复 |
| 设置 | 功能分类、有效编辑自动保存、搜索定位、Java管理、AI服务与权限、缓存、日志导出与关于此应用 |

实例运行环境属于实例子页，Java管理属于设置；任务进度留在原操作Dialog，不提供独立下载页。全局设置合并并串行提交有效编辑，保留无效输入及错误，恢复默认移除覆盖；关闭前Flush失败时保留窗口。实例任务仍有明确确认。

Java移除使用独立确认：托管运行库及登记永久删除，外部Java只解除登记；仍被全局、实例或游戏引用时由Core拒绝。App不提供Java恢复入口，也不依赖恢复目录结果字段；任务详情保留Core删除警告。下载目录回退或缓存不可写时保留可用候选并显示实际警告。

目录导入调用Core只读预览，确认后以`ImportInstanceTask`完整复制迁入；来源不删改，全部完成后登记。安装或Java准备失败/取消保留同一实例和已完成文件，重试不重复创建。版本列表来自真实目录与缓存，不能以虚构版本或摘要失败阻断可恢复场景。

在线内容先形成确切版本与依赖审核，最终确认后应用；输入或基线变化需要重新审核。保留本地来源、未知内容和禁用状态，不覆盖不同字节的同名文件。资源包/光影的文件启用不等于游戏内选中，世界管理不使用模组启停模型。

世界之后的截图页复用Core截图服务：16:9缩略图、文件名搜索／排序、原生多选、双击系统查看器，以及复制、改名、导出、回收站和明确确认的直接删除。图片按实例组织，裁切不改原文件；导出不覆盖，回收失败不永久降级。内置模组只提供Rodel AI、World Reroll和BetterTools，Rodel UI已撤下但不自动删除玩家已有安装。

本地MRPack/CF ZIP导入与目录迁入分开，检查不创建实例，缺件及保留实例的运行准备使用真实typed结果。整合包维护、服务器连接及游戏控制的实际验收范围按[当前边界](../../doc/ALIGNMENT.md)处理，不由已有接口推定交付。

AI页面和全局AI设置复用Core的服务/模型发现、配置、会话事件、token预算、原文归档、标题生成与精确确认。官方MEAI驱动Core中的模型→工具→继续推理，自研provider保持真实流式输出；等待许可、取消、未知效果及失败恢复仍由Core保全。App只做展示与typed调用，不因估算token较大擅自禁用发送，也不在前端复制循环。正常发布不引用或携带CLI，仅保留按需使用的`Assets/AiMcp`文件/网页/检索组件。外部客户端接入默认可仅安装技能，MCP登记需要选择独立安装的`rodel-mc.exe`；程序在原安装目录使用。

待审批请求显示在输入框正上方的紧凑横幅，默认仅本次，允许显式选择会话范围；普通权限可直接批准或拒绝。只有查看详情才打开只读Dialog，关闭不会提交决定，具体变更审核仍保留完整读取门槛。横幅与详情共享固定调用ID/指纹、忙态与错误，不把旧请求的按钮转绑到下一项。观察到终态时补读尾部事件，避免快速批准后已完成的工具结果和最终回答漏显。

切换到其他页面不释放AI页、选中会话或Core回合；后台继续观察，隐藏页不自动追底。返回及会话间切换恢复各自保存的阅读offset，草稿和展开状态保留，不重新创建会话。拒绝不等待只读详情加载，关闭详情始终不作授权决定。所有共享线框按钮的按压态保持平面边框，不用凹陷阴影盖住高亮顶边。

## 窗口、主题与资源

标题栏复用Sprout原生caption按钮、非客户区命中和HTMAXBUTTON，不自造窗口操作。窗口退出/托盘偏好与游戏启动后最小化/托盘/保持显示分别配置；Java创建后先显示“等待游戏窗口”与不定进度，Core确证游戏进程树的可见窗口后才执行启动后偏好。无法观察时保持启动器可见并显示原因；提前退出沿用真实错误。隐藏窗口保留Core监控，不隐式停止游戏，窗口出现也不代表主菜单/世界/入服完成。

头像菜单中的皮肤编辑器打开独立非模态窗口，全应用只保留一个实例。编辑器使用独立的`SkinEditor`偏好容器记录大小、位置和最大化恢复状态；首次在主窗口当前屏幕打开，后续恢复自身位置。原生D3D11视口经同版本Sprout.SwapChain承载，已接Core工程新建/打开、Classic/Slim与64/128、3D画笔/橡皮/填充/取色、镜像、部位/外层显示、相机和单笔保存/撤销重做。打开窗口仍为空态，明确创建或打开后才读取工程。纹理仅随内容更新，静止不持续Present；WARP仅3D使用最多512K离屏像素再nearest输出，atlas直接按完整物理尺寸绘制，保留完整atlas与CPU拾取精度。保存期间仅缓冲下一笔有序命中，失败保留草稿与待处理输入，菜单支持重试或一起放弃；未处理时阻止关闭。主平面与仅3D可见的UV小窗共用像素预演和历史，投影笔刷轮廓复用实际镜像足迹；图层可选并显示真实缩略图，添加、删除、行内重命名、显隐与拖动排序共用绘制的撤销历史；行菜单提供上移/下移，背景层固定底部。失败的图层操作保留输入，可重试或明确放弃。PNG导入/导出已接线：导入确认左侧切换平面/3D预览，右侧信息、模型及固定操作；导入进入同一撤销历史，导出当前会话合成纹理且不增加历史。无工程时是模板网格/命令与真实近期工程的左右分栏开始页，不挂载核心编辑器；logo仅作图片，工程菜单含文件选择器打开及保存恢复入口。库内工程沿用身份，外部JSON保留完整历史复制到库内新身份，来源不变。状态栏显示操作状态、当前层、表面与模型/尺寸。新建窗口同样采用左右分栏，模板、模型及尺寸更新左侧平面/3D预览，选择及取消不写工程，确认才创建。界面已按提示克制准则清理：字段标签成组、正文与按钮统一面板；仅保留操作决策所需信息和实际错误，AI操作区提供模型、参考图、输入、新会话和发送/停止，复用统一Core管线直接修改当前皮肤，绘图无需额外授权；每个工程只保留当前会话，新会话图标仅在有对话时显示，确认后停止并删除旧对话，保留皮肤、图层和绘图历史；完整chat消息、思考和流式历史保留，输出间工具调用汇总Expander，展开显示真实读画部位、图层与步骤。Enter发送/Shift+Enter换行、不显示常驻提示；无消息显示居中空态，发送图标居中。真实工具结果后同步所有视图与命名撤销历史，随包技能指导分层修饰及保留原作。右侧编辑/AI/历史共用原生Sizer调节宽度；完整OS输入矩阵须独立验证。渲染路径与性能边界见[渲染说明](../../design/skin-editor/RENDERING.md)。

工程菜单提供“工程另存为…”及Ctrl+Shift+S，调用同一Core编辑会话与原生SavePicker保存完整`.rmcs`快照，保留像素、图层和全部撤销/重做，不包含独立AI聊天/参考图，不改变来源或把自动保存绑定到外部文件。新保存使用去重压缩容器，既有当前格式3纯JSON仍可打开；外部工程重开复制到新的库内身份。纯显隐、改名和排序不重建未变像素的缩略图，像素修改及撤销仍同步刷新。

颜色区的省略号打开精简色板管理，新建空色板、复制及删除收进菜单；复制内置或自定义色板后均独立编辑，内置五套预设只读。末尾完整线框加号以当前前景色新增独立颜色项，已有同色也不吞掉操作；管理页直接进入选色，内置预设上的加号先创建副本再新增。点击自定义小色块同样直接打开与前景色共享的原生HSV/Alpha ColorPicker，HEX仅为选色器中的可选精确输入，删色在选色器头部，不另铺底部编辑按钮。支持行内改名，无效输入不写入。返回管理时恢复内容自然高度，四边统一12 DIP内距，操作与标题顶部对齐；删除图标使用共享critical角色。集合与当前色板ID使用当前包`SkinEditor`的LocalSettings偏好原子提交，成功后才更新界面，保存失败保留原设置及输入。色板跨工程与重启保留，但不进入皮肤文件、像素、历史或业务MCP。

调色盘弹层的header/body/footer各自统一12 DIP内距，分区之间使用全宽Edge分割线，不再叠加外层padding。无footer的管理页保留右上关闭；新建/复制/删除有底部取消时隐藏右上关闭。创建为primary，删除确认则以取消为primary，按钮两列等宽。

皮肤AI的思考与工具组使用聊天页同款内部非动画展开头，回答不另加“响应”小标题或复制按钮。输入区仅一个图片图标按钮用于选文件，紧挨发送按钮左侧；输入框焦点使用AI聊天的共享Focus暖黄色，Ctrl+V识别剪贴板图片、无单独粘贴按钮；最多3张PNG/JPG/WebP参考图，草稿可移除，发送后以冻结快照随用户消息保留；Ctrl+V只在AI输入框聚焦且剪贴板有图片时附图，不抢普通文本或画布选区粘贴。图片通过Core的标准多模态消息传给所选模型，不只是本地缩略图。模型视觉能力与原生粘贴/滚动实测分别记录，不由构建成功推定。

皮肤视图支持Ctrl＋左键平移、右键或Alt拖动旋转／平移与滚轮缩放；有选区时，画笔／橡皮／填充首次在区外按下只清除选区，同次拖动不补画。透明色作用于可撤销的合成蒙版，保留底下原像素；橡皮优先恢复蒙版区域，工程与PNG包含实际透明效果。操作和快捷键见[玩家指南](https://rmcl.richasy.net/docs/create/skins/)。

界面与CLI共用15种语言范围，包含分别可选的简体／繁体中文和RTL阿拉伯语；技术原文使用实际LTR作用域。主题跟随Windows或保存浅/深偏好，高对比度使用系统颜色、隐藏场景图片并关闭装饰效果，不按主题名称硬编码配色。

运行资源自包含于Assets，内置背景位于Assets/Backgrounds，不从design读取。壁纸导入复制到包LocalState并验证后提交，不修改来源；两主题槽独立。壁纸属于启动器，不再通过`rodel.ui.theme`传入游戏；各内置模组独立管理自己的主题。

HTTP图片缓存位于包LocalCachePath/Images，与Core数据根和自定义壁纸隔离。清理只处理明确归属内容，失败和残留不可报成功。首页可读性shader由`scripts/compile-app-shaders.ps1`调用Windows SDK工具增量编译，生成二进制不提交。

## 代码组织

| 目录 | 职责 |
| --- | --- |
| Features/Shell | 页面组合、窗口偏好和生命周期 |
| Features/Home | 首页状态、typed启动、取消与实际会话归属 |
| Features/Create、Import、Packs | 创建、完整迁入和本地包流程 |
| Features/Content、Runtime、Settings | 各自页面、状态协调、Core调用与弹层 |
| Features/Wallpapers | 启动器两主题壁纸选择与受管理副本 |
| Features/Ai | 根目录保留页面组合、共享Dialog外框与请求互斥；按下表导航，不按类型名建立层级 |
| Features/Skins | 单实例皮肤窗口、原生3D/平面/UV输入与Core共享编辑历史、独立偏好 |
| Features/Tasks | typed提交、观察与取消，不复制Core业务 |
| Controls、Theming、Layout | 共用交互、报告、主题与布局 |
| Settings | 生成式包偏好；不替代Core业务配置 |

报告、原文/易读切换及复制/导出复用共用报告组件。保持模态输入边界、内部切页尺寸与原始回调；恢复焦点前检查实际可聚焦性。

### 全 App 组件组织

独立条目、卡片、表单或有自身交互/生命周期的控件，以可定位的组件名独立成文件。
只为该控件服务的短小投影、内部字段区、图标/Widget适配器可以同文件，不机械地拆每个record或渲染变体。
页面保留组合与协调，不在文件尾混放多个独立列表行；目录按玩家流程与功能职责分组，不增加转发层。
功能内部继续使用原有命名空间，移动源码不改变业务调用、资源、自动化ID、布局常量或生命周期。

| 功能 | 当前阅读路径 |
| --- | --- |
| Home | 页面与共享状态在根；`Instances`为快捷列表，`Launch`为启动设置/反馈/恢复，`Servers`为服务器快捷入口 |
| Accounts | 根为账户管理；`Avatars`为头像，`Menu`为菜单行，`External`为外部认证流程及其来源/密码/设备码/角色控件 |
| Instances | 根为页面/状态/协调；`List`为列表行、单元格和图标，`Operations`为操作及游戏桥弹窗 |
| Content | 根为页面/操作协调；`Installed`、`Discovery`、`Updates`、`Worlds`、`Screenshots`、`Dependencies`承接库存、发现安装、更新、世界、截图和依赖报告 |
| Settings | 根为页面/导航；`General`、`Preferences`、`Java`、`Maintenance`、`Search`、`About`、`Controls`分开保存表单、协调与共用设置表面 |
| Skins | 根为窗口/状态/组合；`Documents`、`Workspace`、`Panels`、`Palettes`、`Ai`、`Rendering`分别承接工程、工作区、检查面板、色板、AI及原生渲染 |
| Packs | `Import`与`Maintenance`分开；服务器客户端准备协调仍在根入口 |
| Wallpapers | 根为服务/偏好/设置入口，`Picker`为选择编辑，`Scene`为背景、对比度及标题栏采样 |
| Ai | `Chat`、`Transcript`、`Configuration`、`Interaction`，详见下节 |
| Create、Import、Runtime、Servers、Images、Shell、Tasks | 保留规模适当的功能目录，独立行/表单具名成文件，不为少量文件强建多层目录 |
| Controls、Imaging、Layout、Theming、Settings | 共用控件、图像服务、纯几何、主题及包偏好保留既有职责；控件专属底层实现可同文件 |

控件树按父子层级展开，较长构造参数和自动化元数据分行；同组短调用（如宽/高、字号/字重）可以保留同一行。
不以固定列宽或每次链式调用强制换行，也不以格式命令退出0代替检查实际源码和命中文件。
SDK的`dotnet format`负责缩进、空白与已启用的分析规则，保留手工组织的表达式及调用链换行，不自动按长度重排DSL。`max_line_length = off`不是合并行的指令：短修饰调用可以同排，长启动链按配置步骤分行，条件分支和多列定义按结构展开。不要用语法树的`NormalizeWhitespace()`整文件重写来代替格式化；它会丢失这些阅读分组。格式修订后用同范围的info级检查确认稳定，不能先压平再只展开控件树。

### AI目录的阅读路径

| 入口 | 职责 |
| --- | --- |
| `Features/Ai/AiPage.cs`、`AiWorkspaceView.cs` | 页面组合、导航、弹层与旧聊天通知的展示边界 |
| `Features/Ai/Chat` | 会话草稿与观察、输入/参考图、历史面板和捕获目标的会话操作 |
| `Features/Ai/Transcript` | 消息投影、Markdown/代码、展开、测量与滚动；不写配置或执行工具 |
| `Features/Ai/Configuration` | 窗口配置Store、服务/模型/注册项及配置表单；不借用聊天状态 |
| `Features/Ai/Interaction` | 审批、提问、精确审核、修复状态与按需详情；执行与授权仍归Core |

目录只表示同一功能内的职责分组，内部类型继续共用`Features.Ai`命名空间，不增加一层转发API。
有独立输入、呈现和交互职责的控件单独成文件，例如`Chat/AiHistoryRow.cs`；只服务宿主组合的小型辅助类型可以同文件，不机械地拆每个record或合并所有专属控件。配置模型与聊天状态分开。
会话改名/导出/删除和配置登记使用独立弹窗，不再让一个通用Kind表单同时依赖两种状态所有者。

## 设计与检查

界面遵循[设计规范](../../design/DESIGN.md)，功能契约见[Core API](../../doc/CORE_API.md)，运行保护见[限制边界](../../doc/RESTRICTIONS.md)。原型只表示布局与模拟交互，不代替真实业务验证。

世界计数按当前显示语言，通过CsWin32调用[Windows系统ICU](https://learn.microsoft.com/en-us/windows/win32/intl/international-components-for-unicode--icu-)的cardinal规则，选择`resw`中的zero/one/two/few/many/other完整文案；可见文字与无障碍名称共用同一结果。所需系统接口已包含在当前最低Windows 19041范围内，原生句柄在每次选择后释放；系统规则读取失败时保留数字计数，世界列表继续可读。

格式入口为`scripts/format-native-ui.ps1`（检查加`-Check`）及`scripts/check-style.ps1`。原生交互验证使用PID、AutomationId和UIA pattern，不使用Computer Use或全屏坐标。截图和局部夹具不证明屏幕阅读器听读、跨显示器DPI或所有游戏组合已通过。

正式测试见[test/README](../../test/README.md)。普通构建不依赖一次性探针、验证脚本、运行报告或开发机器路径，相关产物不得进入产品包。

本地账户头像：默认史蒂夫，28个内置正面头像与PNG/JPG/WebP上传裁切，按账号保存到当前包的LocalSettings及自有PNG文件；微软账户使用官方Minecraft皮肤头部，不能应用本地覆盖。快速账户菜单固定360 DIP，管理缩略图显示修改角标。实例列表省略号的“更换图标”使用28张透明方块图片，复用裁切选择器及Core既有SetIconAsync元数据存储，不依赖原上传文件。两类选择都只在保存时提交；取消保留旧图。

聊天代码块使用Sprout.Markdown的CodeBlockHeader原生控件扩展，App负责本地化语言、复制图标和窗口toast；代码正文仍归原Markdown跨段选择。该扩展随上述正式Sprout包还原，不再依赖本机codechrome补丁包或开发worktree。
