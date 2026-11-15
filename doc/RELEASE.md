# 发布包准备

本地及普通打包入口只生成产物；正式更新由独立手动工作流直接提交 Microsoft Store，并在商店接受后创建无程序包附件的 GitHub Release。正式更新不上传 Actions artifact。

## 身份与版本

商店身份由 App 的 `Package.appxmanifest` 和 `Package.StoreAssociation.xml` 声明。产品 ID 为 `9MW9ZMS2K47C`，包族为 `60520B029E250.RodelMinecraftLauncher_5aa7k9th7aafp`。不在仓库保存签名私钥或商店登录凭据。

应用及商店显示名称统一为 **RMCL**，十五语言不翻译品牌；MSIX 的 Properties／VisualElements／ShortName、AppTitle资源及商店的shortTitle／voiceTitle／sortTitle保持一致。包 Identity、Publisher、StoreId、可执行文件名和命名空间不改。用户当前指定的商店更新版本为 `0.2611.4.0`，末段固定为 `0`；本地开发测试包的较高版本不决定商店发布号，不擅自卸载或降级用户的本地安装。

首版 `0.2611.0.0` 已上架，源码基线登记于 `.github\store-release-baseline.json`。正式更新省略版本时，在实际已发布版本上递增第三段；显式版本必须高于已发布版本。Sprout 仍验证四段 UInt16 与末段为 `0`，不改号或修改 NuGet 缓存。App 及相关测试通过根 `SproutPackageVersion` 固定同一正式框架版本。本地产包、商店接受提交、认证及实际发布分别判断。

## 微软登录发行配置

正式打包要求环境变量 `RODEL_MICROSOFT_CLIENT_ID` 为获准应用的非零 GUID；CI 从同名 Actions secret 注入，传给 Core 的 `RodelMicrosoftClientId` 属性并生成内嵌资源。默认资源让玩家无需配置即可发起微软登录；Client ID 是公开OAuth标识，不是凭据，不注入 Client Secret。实际值不写入源码、日志或审计JSON。

运行时仍保留显式选项 → 进程变量 → Windows当前用户变量 → 内嵌资源的优先级；无效覆盖明确报错，不偷偷回退。开发构建允许无默认ID，离线身份保持可用。打包审计扫描最终NativeAOT EXE中的注入值，仅记录 `microsoftLoginConfigured`；这不代替真人WAM授权、Xbox／Minecraft权限及游戏运行验证。

## 构建入口

构建主机为 Windows x64，使用 PowerShell 7.5+、Windows 自带的 `curl.exe`／`tar.exe`、`global.json` 指定的 .NET 11、Visual Studio x64 C++ 工具链，以及 JDK 25、JDK 21–24、JDK 17、JDK 8。ARM64 交叉构建还要求 VS ARM64 C++ 工具链、Rust 1.89.0 与 `aarch64-pc-windows-msvc` target。Forge 1.20.1 的 Gradle 进程使用 21–24，内部 Minecraft／MCP 编译仍要求真实 JDK 17；旧游戏观察桥使用 JDK 8。这些工具不打进玩家包。

自有 Roslyn 生成器固定为 AnyCPU，由构建主机的编译器加载；不能随 VC 环境的 ARM64 应用目标生成 ARM64 分析器 DLL。

```powershell
.\scripts\package-release.ps1 -Version 0.2611.1.0 -Artifacts Sideload
.\scripts\package-release.ps1 -Version 0.2611.1.0 -Artifacts Sideload -Architectures x64,arm64
.\scripts\package-release.ps1 -Version 0.2611.1.0 -Artifacts Both
```

可通过 `JavaHome`、`LegacyJavaHome`、`ObservationJavaHome` 指定构建 JDK，未指定时复用现有解析器。默认 NuGet 源为 v2 与仓库的 Sprout Azure feed；不复制本机 NuGet 缓存或参考项目的证书。所有模组使用正常 Core 构建路径与 `mods/bundled-targets.props` 登记矩阵。正式发布强制重新执行模组目标并先运行各目标的 Gradle `clean`，关闭配置／任务输出缓存；Loom 的清理在配置 `assemble` 前单独完成，避免旧类名仍进入 JAR 清单。普通开发构建仍保留增量与缓存，不清理玩家目录或全局依赖缓存。

陶瓦固定上游归档由 `curl.exe` 下载，连接／单次传输有超时，瞬时网络及适用HTTP错误最多重试三次；成功退出且固定SHA256匹配后才替换缓存，失败清理临时文件，不更换来源或跳过校验。该重试只适用于公开依赖GET，不扩散到商店创建／上传／commit。

输出按架构隔离在 `artifacts\release\<version>\win-x64` 或 `win-arm64`：`sideload-test` 使用 Sprout Development 分发模式，含测试自签名 MSIX、公开 CER 和安装脚本；它不是可信生产签名。`store` 使用 Store 模式，产生无签名 MSIX 及 `.msixupload`。正式入口将 SDK 默认带符号的外层提交归档替换为仅含准确同一 MSIX 的归档，并在上传前审计；不修改 MSIX 或 NuGet 缓存，不关闭本地调试信息生成。PDB／`.appxsym` 不进入商店提交包或 CI 上传物。构建脚本分别选择 x64／x64→ARM64 的 VC 环境，不将 x64 EXE 作为 ARM64 载荷。

三个固定 MCP 的来源分别登记于 `ManagedMcp\components.win-x64.json` 与 `components.win-arm64.json`。ARM64 的网页和检索组件使用上游固定摘要的原生资产；文件组件的同版本无 Windows ARM64 资产，按校验过的官方源码归档、锁文件与上游 Rust 1.89.0 构建，静态链接 CRT，并记录源码及构建配方摘要。缓存按源码／工具链／配方隔离；源码、Cargo 缓存和构建工具均不发布，MCP 载荷仍精确为三个 EXE、三份许可证和 `runtime.json`。Core 按当前进程架构而非 OS 架构匹配清单，ARM Windows 上的 x64 进程仍使用 x64 组件。

源码、工具链与配方摘要相同不等于不同构建宿主生成相同 EXE；NativeAOT、源码构建 MCP 和包签名不承诺逐字节可复现。比较须分别核对固定来源、实际载荷摘要、JAR 内部内容及准确包的运行证据。

本地推理暂禁用；正常包保留在线 AI 及 `Assets\AiMcp`，不携带 Rodel.Inference、模型权重、推理 worker、CLI 或独立助手产品。内置模组为 AI、World Reroll、BetterTools 及矩阵所需 Fabric API；Rodel UI 不进入正常分发。

每个成功生成的 MSIX 必须经 `scripts\verify-release-package.ps1` 检查真实 manifest、语言、签名状态、精确 JAR 集合、对应架构的 App／MCP／陶瓦 PE、固定 MCP 来源／摘要及许可证，并输出相邻 `.audit.json`。内置背景须与当前 JPG 源文件逐项匹配且不残留 PNG；Store 提交归档只能包含准确同一 MSIX，不得包含调试符号。独立运行审计时，ARM64 包须传 `-Architecture arm64`。此检查不等同于安装／启动、WACK 或商店认证。

## CI

仓库仅保留两个工作流，均只在 `main` 手动触发：

- `.github/workflows/release-store.yml`：Store 双架构构建与发布，以及既有提交的只读检查和 GitHub Release 恢复。
- `.github/workflows/package-exe.yml`：独立 EXE 安装包，成功后将安装包与 SHA256 文件上传到本次运行的 Actions Artifacts，保留 14 天。

两个入口共用 `launcher-packaging` 并发组，不取消已有任务；构建使用标签 `self-hosted, Windows, X64, rodel-minecraftlauncher-build`。自动准备 .NET/JDK，SDK 安装到 runner 临时目录。ARM64 Rust 安装隔离在 runner 工具缓存，不切换主机用户的默认工具链。EXE 工作流还会校验官方安装程序 SHA256，以当前用户安装固定的 Inno Setup 7.1.1，并保留编译器缓存供后续运行复用。构建主机仍须具备前述 PowerShell、Visual Studio C++ 和 Windows SDK。

Gradle 通过进程级 `org.gradle.java.installations.fromEnv` 显式发现 `setup-java` 安装的 8／17／21／25 工具链，不修改 runner 全局 Gradle 配置，也不把已有映射缓存当作缺失 JDK 的替代。

正式构建显式禁用本地推理，checkout 不拉取私有的 `external/Rodel.Inference` 子模块；发布无需该私有仓库的跨仓库读取权限。仅开发本地推理时才按固定提交初始化子模块。

两个构建入口都要求仓库 Actions secret `SPROUT_AZURE_READ_PAT` 具有私有 Sprout Azure Artifacts feed 的 Packaging Read 权限。仅构建步骤将其映射到 `sprout` NuGet 源的进程环境凭据，不写入源码、包或上传物，也不依赖 runner 的交互式登录。不要将令牌写入 workflow、共享文档或聊天。

普通提交／PR 不自动打包。成功构建及包审计不等于安装／启动、真实 ARM64 运行、Minecraft、WACK 或商店认证通过；实际 runner 执行与包验收按对应运行结果判断。

## 正式商店更新

`.github\workflows\release-store.yml` 仅在 main 手动执行，与 EXE 打包共用 `launcher-packaging` 并发组，不取消已有任务。单个既有自托管构建 job 串行构建并审计 x64／ARM64，直接上传商店，不经 Actions artifact 或 GitHub Release 附件中转。GitHub Release 使用 `v<四段版本>` 标签，绑定准确源码，不冒充已通过认证／上架。

| 输入 | 默认及行为 |
| --- | --- |
| `mode` | 默认 `release`；`inspect` 只读检查既有商店提交，`receipt` 读取原 runner 的回执 |
| `version` | `release` 模式使用；留空时递增已发布 patch，也可指定合法的更高四段版本 |
| `publish_store` | 默认 `false`，只构建和验证，不创建草稿；设为 `true` 才上传并 commit，认证通过后自动发布 |
| `generate_release_notes` | **可选，默认 `false`**；不调用AI、不更新或清空商店原有说明，只替换包。勾选时才生成并写入全部15语言说明 |
| `submission_id` | `inspect` 模式必填，使用回执中的准确商店提交 ID |
| `release_run` | 原始 `runId-attempt`；`receipt` 模式必填，`inspect` 模式可选，用于商店接受后仅恢复 GitHub Release |

Store凭据使用 `STORE_TENANT_ID`、`STORE_CLIENT_ID`、`STORE_CLIENT_SECRET` Actions secrets；微软登录及私有feed使用上述独立secrets，不能把商店服务主体ID当作Minecraft OAuth ID。AI使用variables `RELEASE_AI_ENABLED`、`RELEASE_AI_ENDPOINT`、`RELEASE_AI_MODEL`和secret `RELEASE_AI_API_KEY`。初始禁用／未配置，用户填入HTTPS OpenAI兼容基础地址、模型及key后，将enabled设为 `true`；未勾选日志无需配置AI。勾选但配置不完整时在商店写入前失败，不伪造更新说明。AI只收到公开commit标题，不发送源码或凭据。

预检核对实际Published包、15语言、源码基线且无待处理草稿；冻结源SHA、版本、包与说明摘要后，再核对基线才创建草稿。更新深复制服务端字段，保留图片、描述、价格及未知字段；仅替换双架构包及明确生成的说明，上传后回读核对。`commit`前持久保存意图，POST不自动重试；回执使用与Core一致的WriteThrough／Flush及原子ReplaceFile，保留已有读者的旧快照，只对明确的临时文件共享失败有界重试，不重发Store请求。

**商店名称来自包，不来自提交JSON。** [微软StoreBroker固定说明](https://github.com/microsoft/StoreBroker/blob/47d06c77d80b6a47d1b66a78d63d1609934d7104/Documentation/PDP.md#appstorename)明确：应用已提供该语言DisplayName时，不应再提供listing标题，否则可能在commit后发生名称冲突。两个准确MSIX均须通过RMCL名称审计，并声明全部Store listing语言；随后移除提交JSON中的`baseListing.title`，让Store从新包派生，不反复PUT“修正”标题。未知或未打包语言不得被静默套用这项规则。

更新草稿后使用独立GET核对可写数据，不以PUT回显代替回读。包按精确文件名对应，数量必须相同；服务追加元数据或调整包顺序不视为丢失，说明、实际价格及未知可写字段仍逐项保留。状态、上传地址、提交friendlyName及旧包派生targetPlatform不作为可写字段发送；`pricing.isAdvancedPricingModel`是[明确只读的能力标记](https://github.com/MicrosoftDocs/windows-dev-docs/blob/a3812316d9d6678be4e526ffb4b1c4f3958d956d/uwp/monetize/manage-app-submissions.md#pricing-object)，不是实际定价。变更类别记入`readbackMismatches`后停止。

`PendingUpload`或commit回包不代表新包已被处理。[StoreBroker说明](https://github.com/microsoft/StoreBroker/blob/47d06c77d80b6a47d1b66a78d63d1609934d7104/Documentation/USAGE.md#the-easy-way)指出后台要到进入认证后才显示变更。提交后最多观察十五分钟；到可读的认证／后续阶段，独立GET必须证明仅保留目标x64／ARM64两个已处理包、精确版本／架构且两包均覆盖全部目标语言。对应listing必须存在；API可省略包派生的`title`，此时名称依据是已审计MSIX的DisplayName，不要求服务端回显。显式空值或错误名称仍拒绝。回执记录`processedNames`的listing／auditedPackage来源及处理后包的语言，全部一致才记录`packagesVerified`并创建GitHub Release。处理未完或不符不得冒称成功，更不重传／重新commit；已有提交继续通过下述只读入口观察和补齐GitHub交付。认证完成、真正上架和包处理完成仍是不同事实。

原生HTTP传输对API请求显式使用JSON类型，包括不带正文的创建／commit POST，避免PowerShell默认标成表单；OAuth令牌表单及Azure ZIP上传分别保留自己的类型。失败只保留有长度上限且已去掉已知凭据、令牌及URL的服务诊断，不输出原始错误响应或签名地址。收到HTTP拒绝仍须先只读确认实际草稿状态，不能直接重跑写入。

回执、计划、说明和准确包保留在checkout之外的 `RUNNER_WORKSPACE\launcher-store-releases\<runId>-<attempt>`，步骤摘要给出位置和状态。未知commit结果不得重跑正式写入。在同一 Store 工作流选择 `mode=inspect` 并填写 `submission_id` 只读检查；如需补建GitHub Release，再提供原始 `release_run=runId-attempt`。恢复会核对原workflow、main、SHA、准确draft和回执，只完成GitHub交付，不重新上传或commit商店。相同标签／正文幂等复用，不覆盖不同源码或说明的Release。

失败日志没有草稿ID时，可在同一 Store 工作流选择 `mode=receipt` 并填写原 `release_run`，仅从原runner读取已完成运行的准确回执，输出阶段、草稿ID与commit意图，不读取上传凭据或执行Store／GitHub写入；此模式不需要 `submission_id`。不能把产品摘要缺少pending字段当作某个已知草稿不存在的证据。

参考为 `Richasy/Rodel.Player@c02f4ea76cf02cf9a733f45d7f765e5001de1893` 的发行配置；本仓库状态、基线和包由自己的真实读取与审计决定，不能由脚本回归通过推定真实发布成功。

## 首版包体积

对 `677b0817` 首发准确MSIX重新分析。下表为 **ZIP条目压缩大小（MiB）**，不是安装大小或商店实际下载量；外层 `.msixupload` 为 x64 107.93 MiB、ARM64 103.63 MiB，原始安装条目合计207.45／204.13 MiB。

| 类别 | x64 | ARM64 |
| --- | ---: | ---: |
| NativeAOT主EXE | 35.77 | 34.07 |
| 其他图片／壳图标 | 17.74 | 17.74 |
| 固定MCP | 12.89 | 11.58 |
| 25张JPEG背景 | 12.73 | 12.73 |
| 内置模组／Fabric API | 12.42 | 12.42 |
| 原生DLL | 8.23 | 7.83 |
| 陶瓦联机 | 7.79 | 6.91 |
| resources.pri | 0.70 | 0.70 |
| 其他 | 0.54 | 0.53 |

优先候选是PNG：其他图片中壳图标／Splash约10.35 MiB，实例图标2.25、种子图标2.00、普通图标1.54、头像1.23。内存重编码试验中135张可节约约4.73 MiB，尺寸／色模／像素字节相同，但尚未核对gamma／ICC等颜色元数据，**未替换产品素材，不称完整视觉无损**。下一步应保留必要颜色元数据后再核对实际显示及包内压缩收益。

背景此前已由PNG改同分辨率JPEG，当前12.73 MiB，不以继续降画质作为默认优化。大于10KB的重复条目约3.42 MiB，其中Fabric／Quilt共用JAR约1.97 MiB；去重需调整目录／目录契约并验证精确目标，不能直接删掉。主EXE可另行实测AOT体积偏好与运行影响；不能仅为缩包删WAM、固定MCP、游戏兼容目标或必要原生DLL。当前分析未承诺这些候选收益可叠加。

## 商店资料维护

本地 `publish` 统一组织商店资料：`listings` 放一个 CSV 及其引用的多语言图片，供 Partner Center 的“导入列表 → 上传文件夹”使用；`packages` 按架构放商店上传包；文案与编排输入放在 `source`。按用户当前要求，整个目录均由 Git 忽略，不上传语言资产或程序包。素材覆盖应用的 15 种语言，最终图片为 3840×2160；短标题面向缩略图阅读，真实界面与局部组件放大保留原始内容。生成式主视觉不冒充应用或游戏实机截图，原生界面截图仍使用 Sprout.DevTools。

当前轻量交付目录为 `publish/listings-compact`，包含一个 CSV、75 张 4K PNG 和一张 300×300 应用 Logo；旧 `listings` 在用户上传期间保持不变。近无损压缩保留 RGB PNG 与分辨率，实际大小和逐图色差见本地验证摘要，不能标成逐像素无损。文案覆盖全部语言的功能介绍、短标题、语音标题和简短描述。补充字段上限分别为 50、255、1000 个字符，简短描述保持低于建议的 270，依据为[官方字段说明](https://github.com/MicrosoftDocs/windows-dev-docs/blob/a3812316d9d6678be4e526ffb4b1c4f3958d956d/hub/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info.md#L98-L123)。

版权和商标信息填写 `Richasy`，第三方商标及非官方声明留在许可条款。开发者显示名使用清单中的 `云之幻`；不为此更改 `Identity.Publisher` 或签名身份。真实列表 CSV 不含开发者字段，不能添加伪造字段；包清单核对不代替后台账户显示名验证。

CSV 必须基于当前产品从后台导出的模板，完整保留行列数、列顺序、`Field`、`ID`、类型与 `default`，只填写实际存在的语言列；不能从应用语言推导地区代码并新增列，`default` 也不是语言列。上传程序包或变更语言后重新导出，以实际表头为准；元数据 `ID` 与印尼语 `id` 是不同列，语言匹配只能在语言列内进行。

**新资产路径必须包含上传时所选的根文件夹名**：选择 `listings` 时，CSV 填 `listings/image.png`，不能只填 `image.png`，即使图片与 CSV 同级也如此。这是 CSV 协议中的路径，使用 `/`；不要填本机绝对路径，重命名根文件夹须同步引用。依据为[微软官方说明](https://github.com/MicrosoftDocs/windows-dev-docs/blob/a3812316d9d6678be4e526ffb4b1c4f3958d956d/hub/apps/publish/publish-your-app/msix/import-and-export-store-listings.md#L69-L71)。已上传的素材在后续导出中会变为 Partner Center URL，可复用这些 URL。

网页导入用于送审前审阅，不需要先调用 API `commit`；导入后以网页草稿为准，不再混用 API 编辑。列表导入不代替程序包上传、定价、属性及年龄分级，不把 API 回读或 GitHub 文案提交当作后台可见的填写结果。

手工资料草稿与正式更新工作流分开操作；网页编辑进行中不混用API。用户明确选择正式workflow的 `publish_store` 才授权本次上传与commit；仅保存文案、登记图片或向SAS上传ZIP不自动授权发布。程序包须来自已审计的同一冻结源码，不混入符号、工具或验证报告；`PendingUpload` 不等于商店已解包或校验。

## 独立 EXE 安装包

在 Actions 中手动运行 **Package EXE installer**，选择 `main`：`version` 留空时使用 App 工程的版本，也可输入末段为 `0` 的规范四段 UInt16 版本；`x64` 和 `arm64` 是两个独立勾选项，默认均勾选，可单选或一次构建两种架构，至少选择一项。所选架构在同次运行中串行构建，每种架构分别上传 artifact。它复用 `scripts/package-exe.ps1` 生成当前用户安装的自包含 NativeAOT 安装包，不读取 Store 或 AI 凭据。

成功后从运行页面的 `rmcl-<version>-win-<architecture>-exe` artifact 下载安装包及同名 `.sha256`，保留 14 天。上传路径仅包含这两个准确文件，不上传 `payload-*`、编译器、调试符号、凭据或测试执行物。工作流不创建 GitHub Release；需要正式发行时，再按下述要求发布已核验的安装包。

### GitHub EXE 发行版

GitHub Release 附带当前用户安装的 `win-x64-setup.exe`、同名 SHA256 校验文件及 `LICENSE.txt` / `NOTICE.txt`。发布前核对安装包版本、字节摘要与标签对应源码；不上传临时 `payload-*`、符号、签名私钥或机器数据。独立 EXE 安装方式见[App说明](../src/Rodel.MinecraftLauncher.App/README.md)。

启动器自有代码使用根目录 `LICENSE` 中的 AGPL-3.0-only，独立组件范围见 `NOTICE`。后续 CLI/App 构建会携带这两份文件；已有构建作为附件发布时，许可与组件声明同时提供。开源标签的源码归档由 GitHub 生成，框架依赖的公开状态与可构建范围继续如实说明。
