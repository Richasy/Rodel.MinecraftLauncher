# 托管 AI 工具与外部客户端

本页记录当前非 UI 实现、选型依据和交付边界。依赖方向保持 **CLI / Sprout → Core → 内部 Assistant**；内置AI通过Core工具直接执行；对外通道为 **独立CLI/MCP → 共享Core工具契约与服务**，仅execute等CLI专有入口使用命令树。只有 CLI 与 App 是产品入口，`Assets\AiMcp` 内的程序是固定版本伴随组件，不是新的助手产品。

## 从玩家目标到真实效果

Core 按工具的实际效果决定能否执行，不把模型文字、技能或第三方 annotations 当作授权。会话默认只读；`ai chat send --allow-operations` 只授权当前目标，持久模式另由 `ai chat mode` 修改。撤销增加 operationRevision，尚未执行的操作会重新核对。

`config_patch` 复用已有 Settings.UpdateAsync，仅改选定字段，支持显式 null、移除覆盖和保留未知数据。内容安装/更新先形成冻结审核；安全模式将操作权限与精确参数、审核和状态指纹合为一次宿主决策，可仅允许本次或在本会话内允许同名工具。允许全部模式不再追加人工审批，Core仍校验目标范围与审核是否过期，模型自行传入的`confirm=true`不能扩大权限。直接CLI调用继续遵循各命令的显式确认选项。

```powershell
rodel-mc.exe --data-dir C:\launcher-data ai chat confirmation CONVERSATION TURN INVOCATION
rodel-mc.exe --data-dir C:\launcher-data ai chat confirm CONVERSATION TURN INVOCATION --fingerprint HASH --approve true
rodel-mc.exe --data-dir C:\launcher-data ai chat effects-check CONVERSATION TURN
```

每次源数据修改先持久保存 intent，再标记 dispatching，最后保存实际结果和可用的 Core 回读。丢失响应不等于没有效果；相同目标的重试复用已记录结果，不重放写入。后台调用通过 `_meta.rodel.clientRequestId` 与真实 Core job 关联，重复请求返回同一个 admission；找到作业不等于恢复原响应，更不等于作业已经完成。

无法核实的第三方效果继续保留 `unknown_effect`。用户核查后可以明确接受剩余不确定性，允许另一个新目标继续；此操作**不证明成功/失败、不回滚、不清除未知归属，也不允许原目标 retry 重放**，模型没有这个确认工具：

```powershell
rodel-mc.exe --data-dir C:\launcher-data ai chat effects-acknowledge CONVERSATION TURN INVOCATION --fingerprint HASH --approve true
```

## 文件范围与可恢复编辑

文件/网络开关互相独立，也不控制所选模型 API 本身。文件能力默认可用。当前数据根为固定 read_write 授权；App由WinAppSDK提供的当前包数据目录为固定 read_only 授权，用于日志等诊断。两项均不可编辑或删除，不扫描其他包或全盘。额外根由玩家明确选择，默认 read_only；写入仍需要 read_write 与操作许可同时成立。

```json
{
  "expectedRevision": 0,
  "path": "C:\\selected-folder",
  "mode": "read_only"
}
```

```powershell
rodel-mc.exe --data-dir C:\launcher-data ai file-root save --file root.json
rodel-mc.exe --data-dir C:\launcher-data ai file-root list
rodel-mc.exe --data-dir C:\launcher-data ai file-root remove ROOT_ID --expected-revision REVISION
```

读取缺少目录，或已授权目标需要升级读写模式时，回合进入 `awaiting_access`。完成匹配的明确配置后，原调用自动续接并记录修订，不要求重复发问。未等待配置的调用不能悄悄扩大范围；删除/收窄范围会阻止分派并丢弃尚未送给模型的源数据。

实际发现的文件工具包括 `list_directory`、`search_files`、`search_files_content`、`read_file_lines`、`edit_file` 和 `write_file`。Core 为实际 schema 增加小范围宿主参数 `rootId/expectedSha256`，分派前移除并转换成组件真实参数，不建立第二套文件工具目录。

Core 使用 Windows 句柄检查并钉住源目录/文件身份，排除 junction、symlink、ADS、遍历、产品 AI 控制/凭据、账号 token 和已知客户端秘密路径。社区 MCP 只收到私有工作目录中的严格解码、脱敏快照；它的 cap_std 根不直接指向玩家目录。仅列表/文件名搜索时使用名称快照；内容/行读保持明确范围，大小筛选按**原始字节**而非 UTF-8 转码后的长度计算。

这个分工是有意的：复用社区的实际搜索、分页与编辑引擎，但不把其直接写文件误称为拥有预期哈希和事务保证。源文件提交由 Core 做 revision 检查、同目录临时文件、可恢复替换、备份和字节回读，保留 UTF-8/UTF-16、BOM 与统一换行。拒绝模糊/歧义 patch；原生编辑器会全局规范化的混合换行文件可以读/预览，不冒充无关字节都保持不变。完整替换仍需明确内容和原始 hash。

现有限额为单文件 256000 字节、行读最多 400 行、有界目录快照 500 项/深度 8/约 1 MB 文本。截断、排除、大小和无法解码均显式返回，不能将候选快照当成完整目录索引。真实本地 Windows 根已验证；不宣称 UNC 或重解析点形成安全授权根。普通工具事件只保留引用、范围、hash 和结果，不另存正文/diff；模型回答本身可以引用任务需要的已授权内容。

文件替换不是任意外部程序之间的通用 filesystem CAS：Core 串行自己的操作并在提交前后检查，最后替换窗口若发生外部竞态，保留捕获的旧版本并报告冲突/未知效果，不假称能回滚所有并发写入。

## 无玩家密钥的检索与网页读取

默认检索使用 **Mwmbl 匿名公开 API v2**，通过随包 `doh_fetch` 的真实 URL schema 读取：

```text
https://api.mwmbl.org/api/v2/search/?q=ENCODED_QUERY
```

只接受固定 API、一个非空 q，禁止自定义 resolver/目标和伪造时间/分类过滤。Mwmbl 的 v2 源码明确 `auth=None`，API key 是可选配额追踪，不在产品中嵌入共享供应商 key。模型直接消费其真实 JSON 标题、URL、摘要及原生UTF-8字节截断标记；这不是服务器分页，不能编造“第一页”或把结果总数当作当前片段数量。Core不添加搜索provider适配体系。独立索引并不等同于Google/Bing覆盖，零结果、服务不可用与实际截断需要如实说明。

数据接收方包括所选模型、Mwmbl、访问的页面站点和 Cloudflare 公共 DNS。结果引用 Mwmbl 与原始 URL，遵守其服务条款、公平使用和数据许可；不批量采集索引、不绕过限流，不承诺商业用途或服务 SLA。当前按数据根限制逻辑检索频率，并对进程故障只做有界读取恢复；不会把 429/站点拒绝当作需要绕过的故障。

普通页面继续使用实际 `web_scrape`，强制尊重 robots，不执行脚本、不携带浏览器 cookies/模型密钥或本机文件。Core 认证代理按**实际解析并拨号的地址**检查公网范围，DNS 采用固定 Cloudflare DoH/TLS，不依赖开发机 fake-IP 解析；限制传输、超时和可访问主机，关闭时取消在途请求并扣留结果。

玩家指定的本机/局域网 URL 使用地址、端口和解析地址快照的单次精确确认。后续调用不继承，重定向不能借用另一私网地址/端口。网络失败保留普通模型对话和文件能力，不提示玩家安装 MCP 或配置搜索 key。

**选型纠正**：DuckDuckGo Lite 曾真实返回结果，但其 robots 明确禁止 `/lite` 与 `/html`，因此未作为默认工具开放；“请求成功”不是默认后端使用依据。其他候选的 Windows 资产、stdio、许可或可用公共 API 不满足时也没有直接纳入产品。

## 生命周期与第三方边界

三个内置组件由版本清单、下载校验和、运行文件 hash、许可证和正常 publish 入口管理。运行时不下载组件、不依赖开发目录，不要求安装 Node/Python/Go。启动或拥有的进程故障时只做有界重连；恢复读取前重查许可、实际名称/schema，并保持原模型别名。写调用不自动重发，业务 journal 继续决定未知效果与恢复。

SDK 传输 I/O 与会话记录 I/O 区分：组件断连是具名工具故障，不能误报为整个会话存储损坏；已经退出的自有子进程清理时发生 SDK 发送错误会显式记录，不伪造操作成功或终止无关进程。

内置AI的MCP和技能目录固定由产品提供，不开放自定义注册或启停。旧第三方MCP/技能配置文件原样保留，但有效配置和实际连接/加载路径均忽略这些注册；新增、启停和删除返回 AI_BUILTIN_EXTENSIONS_ONLY。外部客户端安装启动器自己的MCP是下节的独立流程。

对应领域首次被tools_discover请求时才连接托管组件，普通问答不启动它们；内置启动器/皮肤工具直接调用Core，不经这些MCP进程。

## 外部 AI 工具安装维护

四个入口的标志随 App 打包在 `Assets/AiClients`，SVG 使用同一透明轮廓按当前文字颜色着色，覆盖浅色、深色和系统高对比度。Codex、Claude Code 分别采用 models.dev 的 OpenAI、Anthropic 标志，另两项使用 GitHub Copilot、DeepSeek 标志。素材来自 models.dev 的 `2c4f761f`，只把外层尺寸规范为 viewBox 自适应，MIT 许可随素材打包。[上游素材](https://github.com/anomalyco/models.dev/tree/2c4f761f7d61180feb17852b37cb664e8d62b458/providers)

外部客户端默认登记`--tool-profile discovery`，初始仅有`capabilities`、`tool_call`、`execute`、`result_read`、`result_image_read`五个入口。先用capabilities查看相关名称/单个toolName的schema，再以tool_call(name, arguments)调用同一Core契约；CLI专有操作才用execute及对应topic帮助。这个profile不动态依赖客户端刷新tools/list，也不把完整目录schema塞入连接上下文。显式`--tool-profile launcher`仍提供完整具名启动器工具，`skins`提供独立皮肤目录；外部客户端负责自己的授权。初始化说明与技能索引均要求普通问答无需加载操作状态。

此流程不需要任何模型。`LauncherClient.Assistant.Integrations` 提供 `PlanAsync/StatusAsync/ApplyAsync`；CLI 消费同一 typed 请求：

```json
{
  "client": "codex",
  "action": "install",
  "launcherExecutable": "C:\\Tools\\RodelCli\\rodel-mc.exe"
}
```

```powershell
rodel-mc.exe --data-dir C:\launcher-data --json ai integration plan --file install.json
rodel-mc.exe --data-dir C:\launcher-data ai integration apply --file install.json --fingerprint HASH
rodel-mc.exe --data-dir C:\launcher-data ai integration status --file install.json
```

Codex 通过用户级 `mcp list --json` 观察注册，使用客户端 app-server 的 `config/value/write` 注册、更新或清除 `mcp_servers.rodel-minecraft`，保留其他 MCP 设置。TOML 由 Codex 保存，客户端可能统一换行符。需要选择支持单键写入的 Codex 客户端。Codex 配置遵循 `CODEX_HOME`，用户技能默认 `~\.agents\skills`。Claude Code 配置和技能遵循 `CLAUDE_CONFIG_DIR`，默认 `~\.claude`。[Codex 配置 RPC](https://learn.chatgpt.com/docs/app-server#config-rpc-examples-for-app-settings)

GitHub Copilot CLI 的 `client` 为 `copilot`，使用 `COPILOT_HOME`，默认 `~\.copilot`。技能安装到该目录的 `skills`，MCP 只编辑 `mcp-config.json` 的 `mcpServers.rodel-minecraft`。服务为本地 stdio，使用 `tools: ["*"]` 暴露 Rodel discovery profile 的入口。[Copilot CLI MCP](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers)

DeepSeek Harness 的 `client` 为 `deepseek-harness`，使用 `DSH_HOME`，默认 `~\.dsh`。技能安装到该目录的 `skills`，MCP 在全局 `cordis.patch.yml` 写入一条独立的 `@deepseek-ai/dsh-mcp-client` stdio 插入项，CLI 与共享该 Home 的 Desktop profile 可读取。安装器只管理独立的 Rodel 行，发现已有手工 Rodel 行时保留并报告冲突。使用已初始化的 Harness Home，CLI 可用 `clientConfigRoot` 选择其他已存在的 Home。[Harness CLI 配置层](https://github.com/deepseek-ai/deepseek-harness/blob/dsh-v0.2.1-rc.5/apps/cli/reference/README.md) · [Harness MCP 客户端](https://github.com/deepseek-ai/deepseek-harness/blob/dsh-v0.2.1-rc.5/packages/mcp/mcp-client/README.md)

安装只拥有`rodel-minecraft` MCP条目和产品技能文件。App不携带CLI；MCP接入需要独立发布的完整CLI目录，并在请求的launcherExecutable或App文件选择器中明确选择程序。登记直接引用该绝对路径，不复制EXE或资产；Core冻结程序hash、数据根和配置指纹，真实MCP握手后才登记。程序改变需要重新预览。未选CLI时计划为cli_required，应用不写任何技能/配置；仍可选择skillsOnly。数据根变化先显示旧绑定，只有应用精确计划才重绑。

计划核对当前内容/hash/配置，保留未知字段、其他 MCP 和人工文件。人工修改需要明确选择替换并重新计划；移除只删除仍匹配记录的文件/条目，修改过的保留并返回partial，独立CLI安装始终不删除。客户端不存在仍可安装技能并导出**未注册**配置，不报告 MCP 已安装。

多文件操作逐项保存阶段与所有权。Claude 首次注册和移除沿用客户端命令。换绑时先在独立临时配置中调用 Claude 添加并校验候选，然后原子替换用户配置中 `mcpServers.rodel-minecraft` 的值，保留其他字节。候选添加失败时旧绑定继续可用。精确匹配尚未完成 intent 的登记可按“当前绑定已观察到”恢复，不伪造原命令收据。外部客户端的工具权限仍由该客户端负责。

## 固定来源与采用判断

| 来源 | 固定版本 / commit | 核对入口与采用范围 |
| --- | --- | --- |
| modelcontextprotocol/csharp-sdk | 2.2.0 / `6fa3825973949a9c4f0cd8af344e15a8db09dc35` | Client、stdio/stream transport、RequestParams.Meta；实际 SDK 初始化/目录/调用/取消与 NativeAOT，不根据宣传推断兼容 |
| rust-mcp-stack/rust-mcp-filesystem | v0.4.5 / `ef4797360ea03eec5375e03a6f10d092dfc6a5e0`，MIT | `src/fs_service/core.rs`、`io/edit.rs`、`io/write.rs`、真实工具 schema；cap_std 限定私有快照，Core 补源文件身份/严格 patch/提交 |
| Role1776/mcp-retrieval | v0.1.9 / `98eadccee4f374d7c178e9bcab82b3a906b84efe`，MIT | `app/internal/adapter/web/retrieval.go`、MCP web tools、配置；只开放网页读取，代理凭据仅在内存注入，不使用其 DDG Lite 搜索 |
| 99apps-id/doh-mcp | v0.1.0 / `cb8e483dc06435bf7f5cddb2caac68f4cbae3d80`，Apache-2.0 | `mcp.go`、`fetch.go`、`resolve.go`；复用真实 doh_fetch 读取固定公开 API，Core 认证代理补完整地址/重定向与实际取消边界 |
| mwmbl/mwmbl | `503e4134d7e2b368d789085c92d52699c9d29fb9` | `mwmbl/tinysearchengine/search.py` 的 v2 匿名认证和真实字段、API 组合；只调用服务，不打包服务器源码。实际服务结果与源码核对分别记录 |
| openai/codex | 0.101.0 / `b0aa1ce407b741c9041e7b4dc52b3cc658848935` | `codex-rs/core/src/skills/loader.rs`、`codex-rs/cli/src/mcp_cmd.rs`；真实用户技能位置、MCP 管理和 JSON 查询 |
| Claude Code 官方技能/设置/MCP 文档 | 实际客户端 2.1.92 | 用户技能根、CLAUDE_CONFIG_DIR、用户级管理命令；隔离根实际读写验证，不运行会启动未受信任项目 MCP 的 get 健康检查 |

软件许可与搜索服务/结果许可不是同一件事。清单中的下载 hash 在产品 manifest 内；源码核对、实际 SDK/OS 行为、模型完成目标和 VM 证据是不同层级。详细脚本/报告只保留在本地忽略目录，不作为共享文档的依赖。

当前证据包括正常 Windows x64 NativeAOT、真实 Core job/OS 宿主异常退出、真实 DeepSeek 文件读改读/匿名检索、真实 Codex/Claude 用户配置隔离，以及干净 offline Hyper-V 的完整自包含包/网络失败降级。VM 不联网，不冒称 VM 中公开检索通过；正式 Anthropic/Google 账号、本次不可用的本地 Inference 服务、App MSIX AI 资产与 UI/Java/BOT均不由这些证据推定完成。压缩算法继续保持[专项文档](AI-CONTEXT-COMPRESSION.md)的可演进实验定位。
