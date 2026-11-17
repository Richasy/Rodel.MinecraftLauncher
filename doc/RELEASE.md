# GitHub Releases 分发与更新

RMCL 不再通过 Microsoft Store 分发更新。正式渠道是 [GitHub Releases](https://github.com/Richasy/Rodel.MinecraftLauncher/releases/latest)，提供 Windows x64／ARM64 的当前用户 EXE 安装包。默认 App 调试及发布为无包 `SproutPackageFormat=None`；MSIX 工具仅供显式包身份验证，不作为玩家分发或更新入口。

## 发布流程

在 Actions 手动运行 **Release EXE installers**（`.github/workflows/package-exe.yml`）：

- `version`：留空取 App 工程的 `SproutAppxVersion`，或指定更高的规范四段 UInt16 版本，末段为 `0`。版本同时用于 EXE、安装器、标签和更新清单。
- `x64`／`arm64`：默认均选。`publish=true` 必须从 `main` 构建全部两种架构。
- `publish`：默认开启。关闭后允许在功能分支构建验证，只上传 Actions Artifacts，不发布或改变稳定更新频道。

提交前维护 `.github/release-notes.json` 的全部15语言说明。CI 校验语言与非空说明，从两个实际安装包计算长度、SHA256及当前源码SHA，生成 `release-note.json`。日志采用简短纯文本，不能将内部构建通过描述为Minecraft实际运行通过。未完整生成全部安装包与清单时不发布。单架构验证仅生成该架构artifact，不生成可被玩家发现的不完整更新清单。

全部构建成功后，先将 `v<version>` 标签绑定准确源码SHA，再以该既有标签创建draft，附加以下五个文件，再公开为latest。GitHub不会为无标签draft立即创建Git引用，按tag读取的REST入口也只返回已发布版本，因此草稿回读使用准确的release ID，不以published-by-tag接口检查draft。

完整附件为：

```text
RMCL-<version>-win-x64-setup.exe
RMCL-<version>-win-x64-setup.exe.sha256
RMCL-<version>-win-arm64-setup.exe
RMCL-<version>-win-arm64-setup.exe.sha256
release-note.json
```

不覆盖已有标签、draft或发行版；失败时先检查准确tag和附件。上传失败遗留的draft不对普通玩家可见，核对并删除本次失败draft／tag后才能重新运行；发布结果未知时先只读确认，不自动重放。旧的无安装包Store发行记录不满足更新契约，App会静默忽略。

工作流使用 `launcher-packaging` 并发组及 `self-hosted, Windows, X64, rodel-minecraftlauncher-build` runner，串行构建架构。构建需要 PowerShell 7.5+、`global.json` 固定的.NET 11、Windows SDK及Visual Studio x64／ARM64 C++工具链、JDK 8／17／21／25，ARM64文件MCP另需隔离Rust 1.89.0 target。CI准备SDK、JDK、固定摘要的Inno Setup 7.1.1，不修改全局Rust默认工具链。安装包／校验摘要及完整release资产作为Artifacts保留14天，不上传payload、测试、符号或凭据。

所需Actions secrets：

| Secret | 用途 |
| --- | --- |
| `RODEL_MICROSOFT_CLIENT_ID` | 获准的公开OAuth应用ID，构建时内嵌，玩家无需自行配置；不是Client Secret |
| `SPROUT_AZURE_READ_PAT` | 私有Sprout包源Packaging Read，限构建进程环境 |

发布使用工作流的 `GITHUB_TOKEN`／`contents: write`。不需要Store凭据或AI服务凭据；发布说明来自随源码审阅的本地化文件。

参考 `electerm/electerm@7ba113beb18758777f7de10eed086efa4a86045e` 的 `.github/workflows/win-nsis.yml`、`src/build/bin/build-win-nsis.js` 与分架构 `win-<arch>-installer.exe` Release资产组织；RMCL继续使用现有Inno Setup与NativeAOT，不引入Electron、NSIS或其自动更新框架。

## 本地构建

```powershell
.\scripts\package-exe.ps1 -Architecture x64
.\scripts\package-exe.ps1 -Architecture arm64
```

构建环境须设置公开的 `RODEL_MICROSOFT_CLIENT_ID` 并安装 Inno Setup 7.1.1 或更新版本；必要时通过 `Compiler`、`WindowsSdkVersion`、`JavaHome`、`LegacyJavaHome`、`ObservationJavaHome` 指定已安装工具。默认NuGet使用v2及Sprout Azure feed。输出在 `artifacts\release\<version>\win-<architecture>\exe`。

安装包自包含NativeAOT、Windows App SDK、背景、三个固定MCP与许可、陶瓦、矩阵中登记的内置模组及Fabric API；不携带CLI、Rodel UI、本地推理、JDK或开发目录。`Assets/AiMcp`依实际目标架构准备，不将x64文件当作ARM64。安装器不做Authenticode签名；同源SHA256用于下载完整性校验，不冒充独立出版者签名。

默认安装到 `%LOCALAPPDATA%\Programs\RMCL-<architecture>`，不需要Store、管理员权限、.NET或信任MSIX证书。卸载不删除游戏、存档或App偏好。已有Store与EXE版的App偏好隔离；游戏数据仍按所选Core数据根使用，切换前记录自定义路径，不先卸载旧版或假定会自动迁移偏好。

从 Windows 设置或安装目录卸载时，先使用 Inno 的原生确认框并说明未保存更改会丢失；确认后由卸载器按完整可执行文件路径核验并强制结束此安装的全部 App 进程（包括托盘），不等待任务、草稿或窗口关闭回调。核验时持有进程句柄防止 PID 复用，确认进程已退出后才删除程序；不能核验或终止时中止卸载，`AppMutex` 继续提供运行中保护。取消最初的确认不会关闭 App。不按进程名批杀，不结束游戏进程，也不关闭开发目录或其他安装的实例；只清理自身安装协调登记，保留已经落盘的 App 偏好、游戏及存档，不能保证正在编辑或保存中的内容不丢失。

强制终止挂在 Inno 的 `usAppMutexCheck`，位于确认之后、运行中检查及文件删除之前，调用随安装包携带的 `Assets/Setup/stop-installed-app.ps1`（系统 Windows PowerShell，无需额外运行时）；`CloseApplications=yes` 本身不是卸载退出机制。时序依据[当前固定版本的官方卸载源码](https://github.com/jrsoftware/issrc/blob/2527da77ff7983514f535d19391a748c5d898336/Projects/Src/Setup.Uninstall.pas)。旧安装的卸载器不会被在线修改，需先用新安装包覆盖安装。

## App 更新契约

启动后异步读取固定仓库的GitHub latest稳定发行版，20秒内完成元数据检查；404、限流、离线、损坏JSON、旧版／跳过版本、缺少精确附件均安静忽略，不阻止窗口或普通业务。仅下载小型说明清单，不预下载安装包。`release-note.json`的版本必须与tag一致，schema为1，含源码SHA、`notes`语言映射、`installers`按x64／arm64映射的`fileName`、`size`与`sha256`。缺少当前语言时使用英文，简繁中文沿用LauncherLanguages。

有更新才显示标题栏图标；稍后不记为跳过，跳过只保存确切版本，更新的版本仍可提示。弹窗使用当前语言说明；更新时按当前应用进程架构选择安装器（ARM Windows上的x64应用仍更新x64安装，避免悄悄另装一份ARM64应用），在临时`.part`文件流式下载，显示实际字节进度，完成长度／SHA256校验后才提交。取消与错误清理半成品，不运行损坏或不匹配的文件。

安装前提交有效偏好，未完成任务或编辑器草稿保留正常关闭守卫，托盘偏好不代替真正退出。窗口Closed事件是交接授权，独立Windows PowerShell等待原进程身份退出，再次核验安装包摘要后打开正常交互式安装向导；不强杀游戏、不静默安装。向导结束（包含取消）后删除下载的安装包，并清理授权标记；文件暂时被占用时有界重试。交接进程被中断后的遗留包，仅在后续启动发现目标已安装且文件超过一天时清理，避免误删其他实例刚完成的下载。下载、安装向导和新版本启动是不同阶段，不把下载100%写成安装完成。

## 验证边界

正式回归源代码在 `test`，临时驱动、截图、TRX与安装产物不提交。更新检查／下载的受控HTTP、真实文件和故障回归，与隔离VM中的原生标题栏／Dialog／安装交接分别验证。不能从构建成功推定原生ARM64执行、真实Minecraft、微软真人登录或跨网络联机通过。
