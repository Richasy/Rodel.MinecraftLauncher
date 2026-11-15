# CLI 使用

## 陶瓦联机

房间属于 Core，同一数据根只有一个活动房间。所有参与者各自运行本机游戏；世界仍在房主原实例中。默认进入所选世界后自动开放，未适配游戏可用 `--auto-open false` 并从游戏内手动开放。房主可用 `--world FOLDER` 直达支持的存档，或用 `--session SESSION` 明确选择已运行的游戏；多会话不自动挑第一个。世界关闭、切换或游戏退出会自动结束房间。

```powershell
rodel-mc multiplayer host INSTANCE --player Steve --background
rodel-mc multiplayer show
rodel-mc multiplayer join INSTANCE --code 'U/XXXX-XXXX-XXXX-XXXX' --player Alex --background
rodel-mc multiplayer launch ROOM
rodel-mc multiplayer retry ROOM
rodel-mc multiplayer change-code ROOM --code 'U/XXXX-XXXX-XXXX-XXXX'
rodel-mc multiplayer fallback ROOM --server-address play.example.org:25565
rodel-mc multiplayer export-log ROOM C:\Exports\connection.txt
rodel-mc multiplayer stop ROOM
rodel-mc multiplayer stop ROOM --exit-game
```

加入默认在网络入口就绪后启动游戏；`--start-game false` 只连接网络，再用 `launch ROOM` 显式启动。已有游戏需要正常退出再加入时，必须同时明确 `--session SESSION --restart-game`，不会静默关闭其它游戏。网络断开后不重复启动游戏。

`stop ROOM` 只停止联机；`--exit-game` 另行请求本房间对应游戏正常退出，不强制结束进程。控制返回表示请求已提交，实际结果用 `show` 观察：`ending` 等待退出，`exit-pending` 需要返回游戏处理或再次检查。已受理或结果未知的关闭请求不自动重发；结束时取消尚未完成的本次启动，已开始的游戏按正常窗口关闭收尾。保存完成必须有真实游戏退出证据，不能由控制请求成功推断。原生主操作明确“结束并退出游戏／离开并退出游戏”，更多菜单保留仅停止联机。

示例中的 X 是占位符，实际码必须通过陶瓦原生校验。`show` 仅在房主连接及目标端口核对成功后返回邀请；它是加入凭据，不应贴入公开日志。持久任务及控制文件只保存 DPAPI 保护的引用，不保存明文码。后台任务不重放未知的游戏开放或重复启动。

Windows 首次使用联机组件或新的 Java 路径时可能请求防火墙授权。组件原始二进制在当前用户固定目录按版本/架构复用，底层解包目录经受控目录联接指向同一引擎文件，普通重连/组件重启不另换程序身份；启动器不自动添加放行规则或关闭防火墙。

连接失败最多自动重试三次，保留游戏、实例和邀请码；组件退出时只替换已验证的本方进程。加入端固定本地地址在房间生命周期内保持不变，网络恢复后需在游戏中重新连接，不能续接已经断开的 TCP 游戏会话。备用入口必须由房主另行提供普通服务器地址；首版不创建隧道或承诺中继。`connected` 仅代表网络就绪，不代表身份验证或游戏入服成功。游戏保留原有身份验证，离线参与者需要房主提供接受离线身份的游戏环境。

备用地址的游戏状态查询是辅助观察，不是登录门槛：TCP 可连接但没有状态响应时仍允许尝试加入，并返回 `MULTIPLAYER_FALLBACK_STATUS_UNKNOWN`。后续探测失败不关闭已有游戏流；被动更新解析目标时，已有 TCP／UDP 会话保留原目标，新会话使用新目标。显式停止或更换备用地址仍撤销旧连接。游戏启动失败或需要处理已有游戏的错误，不会被后续网络刷新清空。

只有房间码时无法取得对方版本/整合包。共享环境使用标准包，由既有 `pack inspect/import/review/apply` 流程处理；普通服务器入口仍独立管理长期收藏。

```powershell
rodel-mc pack export INSTANCE C:\Exports\shared.mrpack --for-multiplayer
rodel-mc pack inspect C:\Exports\shared.mrpack
rodel-mc pack stage-file INSTANCE MISSING_ITEM C:\Downloads\missing.jar
rodel-mc pack complete INSTANCE --manual-directory STAGED_DIRECTORY
rodel-mc pack apply OPERATION_ID
rodel-mc versions list
rodel-mc versions list --refresh
```

`--for-multiplayer` 只分享启用的内容引用与共享配置/脚本，排除个人 options、服务器收藏、存档及无来源本地二进制；返回 `complete=false`/`excluded` 时应核对缺件，不将第一方随包模组转成社区下载。选择补件只做隔离复制，继续准备才按声明大小/哈希校验并提交。版本目录在相同缓存目录/来源间共享，默认六小时有效，普通打开合并并发请求，手动刷新显式强刷；断网保留可用缓存。

内置模组使用与App相同的Core审核及应用流程：

```powershell
rodel-mc content bundled INSTANCE
rodel-mc content review-bundled INSTANCE rodel_ai worldreroll
rodel-mc content apply-review INSTANCE REVIEW_ID --confirm
```

可选ID以当前实例目录为准，包括 `rodel_ai`、`worldreroll`、`bettertools` 的目标子集；Rodel UI 已撤下，不会作为依赖加入。审核不改实例，也不卸载玩家已有 UI。目录按实际Minecraft版本及Fabric/Quilt/Forge/NeoForge加载器选包，Forge1.20.1当前只有AI。提交后核对实际结果，来源为随包JAR，不调用社区分发接口。

游戏桥的`process game query SESSION --request JSON`支持`kind=recipes/quests/books`及可选`search`、`resource`前缀、`offset/limit`。FTB任务返回当前团队进度及可见目标，Patchouli返回已解锁手册页，配方来自实际RecipeManager；缺失API不等于空结果。示例与权限见[游戏桥](JAVA-GAME-BRIDGE.md)。

AI故障修复使用`ai repair start INSTANCE --session SESSION`，准备失败可用`--job JOB`。一次授权固定实例与原故障，自动复用会话并推进可回退修复和新运行观察；无模型时保存现场。`ai repair create/show/plan/apply/verify/restore`供脚本操作同一Core记录，`ai chat create --repair REPAIR_ID`绑定已有事件。它不替代原有`repair INSTANCE`必需文件修复。游戏内自动复现尚未实现，初始化通过不能当作原场景已修复。详见[AI修复边界](../design/AI-REPAIR.md)。

本文说明当前CLI命令与操作语义。Core业务可独立于界面运行；App专属控制需选择运行中的宿主，使用语义接口而非模拟点击。正式回归入口见[测试说明](../test/README.md)。

CLI以覆盖App能力的可编程入口为目标，按领域/资源/动作组织，不为AI重复业务命令。能力所有权与命令收敛见[CLI能力设计](CLI-DESIGN.md)。Core/CLI的App控制契约与受控传输保留，但Sprout App端接线已移除；当前App不注册控制宿主，不能将这些命令视为已可控制真实App。

当前为开发版，先使用独立 `--data-dir`。Core/CLI默认数据位于用户目录下的`.rmcl`（Windows为`%USERPROFILE%/.rmcl`）；App可在自己的包偏好中选择覆盖目录，CLI不读取该偏好，需用`--data-dir`明确指定。`--json`输出单行结构化结果，不加时输出易读JSON，编码为UTF-8。命令不会通过shell执行输入的JVM参数或内容路径。布尔开关可写`--force=false`；无效布尔值会明确报错。

## 原版种子地图与筛选

种子探索归`world seed`，与存档导入/备份分开：它调用本地精确版本原版Java，不读取玩家存档或账号。首个实际验证目标为26.3 / Java25；其他版本不做近似计算。可先列出本地配置，缺少文件时显式准备：

```powershell
rodel-mc world seed versions
rodel-mc world seed prepare --version 26.3
rodel-mc world seed catalog --minecraft-directory C:\Games\.minecraft --version 26.3 --game-language zh_cn
rodel-mc world seed map --request-file map.json
rodel-mc world seed spawn --request-file spawn.json
rodel-mc world seed structures --request-file structures.json
rodel-mc world seed region --request-file region.json
rodel-mc world seed export --request-file map.json --output C:\Exports\seed.png
rodel-mc world seed search start --request-file search.json
rodel-mc world seed search list
rodel-mc world seed search show SEARCH_ID
rodel-mc world seed search resume SEARCH_ID
rodel-mc world seed bookmark list
rodel-mc world seed bookmark save --request-file bookmark.json
rodel-mc world seed bookmark remove BOOKMARK_ID
```

`--minecraft-directory`指向选中版本所在的元数据目录；共享依赖按启动器引用解析。`--java`可指定已有Java。请求使用`--request`或`--request-file`二选一；复杂JSON建议使用文件，seed始终是字符串。最小地图示例：

```json
{
  "profile": { "minecraftDirectory": "C:\\Games\\.minecraft", "versionId": "26.3" },
  "seed": "9223372036854775807",
  "world": { "dimension": "overworld", "preset": "normal" },
  "minX": -512, "minZ": -512, "step": 16, "side": 64
}
```

`side`是每边采样数（最多256），`step`是采样间隔；不是逐方块完整存档。省略`y`查询基础地表，显式`y`查询该层群系；下界建议明确`y:64`。维度为`overworld/the_nether/the_end`。`structures:null`或省略读取全部结构，空数组关闭结构查询；具体ID从catalog读取，不把候选区块冒充实际建筑。

筛选请求沿用`profile/world`，增加`startSeed`、`maxSeeds`、`maxResults`、`timeLimitSeconds`、`biomeStep`及`conditions`。条件示例：

```json
[
  { "id": "village", "kind": "structure", "targets": ["village_plains"], "minimum": 1, "radius": 512 },
  { "id": "cherry", "kind": "biome", "targets": ["cherry_grove"], "reference": "village", "radius": 256 }
]
```

全部条件必须成立，每项`targets`为任一匹配；引用先前条件时，后续条件围绕同一个原版见证对象检查。群系命中是实际采样点，不提供不存在证明。预算和取消以实际候选检查边界为准，保存游标及已有结果；`resume`沿用原条件快照与精确运行时，拒绝在原版文件变化后混合旧结果。`prepare/map/search start/search resume`可复用既有`--background`任务通道；搜索记录ID与后台job ID分别保留。

结构查询`structures`接受`SeedStructureQuery`：`profile`、字符串`seed`、`world`、`centerX/centerZ`、`radius`、`targets`和`limit`。结果保留实际发现数及截断后的最近位置；不是仅检查候选区块。

区域查询`region`接受`SeedRegionQuery`，例如：

```json
{
  "profile": { "minecraftDirectory": "C:\\Games\\.minecraft", "versionId": "26.3", "javaPath": "C:\\Java25\\bin\\java.exe" },
  "seed": "-5495201639033488759",
  "world": { "dimension": "overworld", "preset": "normal" },
  "bounds": { "minX": -720, "maxX": -700, "minY": 48, "maxY": 79, "minZ": -1008, "maxZ": -988 },
  "includeBlocks": false,
  "includeLoot": true
}
```

XYZ边界均包含端点；水平各边最多256格，Y须处于当前维度。统计包含每个位置，`materials`按方块ID计数，包含空气/流体；不是挖掘掉落物。`containers`保留实际坐标、槽位、物品组件、loot table/独立loot seed；普通战利品使用无玩家、默认luck，宝库等动态奖励明确标识而不猜内容。`includeLoot:false`保留未结算状态。

`includeBlocks:true`另返回全部属性的`palette`与base64 `blocks`，按Y/Z/X顺序保存ushort小端索引，体积最多4,194,304个位置。`fixed_region_v1`表示新建隔离原版世界的固定范围生成规则：TERRAIN屏障、固定顺序FEATURES、FULL，再扫描；范围也是输入，改变范围可能影响边界装饰。不读取玩家存档，也不能重建任意已游玩的世界。取消/失败不发布半份缓存；损坏的完整性校验失败后重算。阶段进度只报告当前阶段单位，不拼造整体百分比。

收藏请求格式见Core的`SeedBookmark`，保存种子或地点的完整生成配置、位置与传送Y；两类均存于当前`--data-dir`，与Sprout共用同一业务数据。导出包含原始生成元数据并原子替换目标文件。原生窗口额外支持冻结当前矩形视野导出；CLI上述`export`导出请求中的完整瓦片。性能及准确性边界见[种子探索器](SEED-EXPLORER.md)。

## 参数与帮助

CLI使用正式版System.CommandLine，命令、位置参数、typed选项和终端/MCP帮助来自同一棵声明树，不再维护手写token解析器或第二份帮助语法表。使用`rodel-mc --help`查看分类，`rodel-mc help content install`或`rodel-mc content install --help`查看具体子命令；`capabilities --topic "content install"`返回同源的结构化帮助。版本信息用`version`命令查询，子命令的`--version VALUE`仍表示游戏版本或本地profile，不被启动器版本开关截走。

每个具体命令在注册处直接绑定处理函数和对应后台typed工厂，不再按命令名二次switch。MCP具名工具与内部调用复用实际Command/Option符号，由`CliInvocation`生成标准token；编译器检查符号引用与选项值类型，初始化校验负责拒绝同级命令/别名、有效作用域选项/别名及工具名冲突。当前尚未发布，不承诺或实现开发中废弃格式的兼容。

源码按功能组织在`Commands/Groups`，各组持有自己的注册和可复用符号，如`LauncherCommands.Content.Install`；根类只组合命令树、处理公共解析与帮助，不再集中注册全部业务。`CommandFactory`只复用注册、必填和确认声明，不添加第二层业务分发。

App与CLI统一提供15种显示语言：`zh-CN`（简体中文）、`zh-TW`（繁體中文）、`en`（English）、`ja`（日本語）、`ko`（한국어）、`de`（Deutsch）、`fr`（Français）、`es`（Español）、`pt-BR`（Português / Brasil）、`ru`（Русский）、`it`（Italiano）、`pl`（Polski）、`tr`（Türkçe）、`id`（Bahasa Indonesia）、`ar`（العربية）。CLI普通与AI命令的帮助、参数说明和命令层错误分别存于`Strings/CliStrings*.resx`和`Strings/AssistantStrings*.resx`，由MSBuild GenerateResource在obj生成强类型访问器。

启动时语言优先级为全局参数`--language CODE`、环境变量`RODEL_CLI_LANGUAGE`、系统UI语言。显式参数接受上述15个规范代码，可放在命令前后，也支持`--language=ar`；缺值、不支持的参数值或重复指定返回参数错误。语言预解析与正式命令树共用同一个System.CommandLine Option，`--`后的位置参数不会被当成语言选项。

环境变量与系统UI语言接受地区/文字体系变体，由共用`LauncherLanguages`解析：例如`ar-SA`→`ar`、`en-GB`→`en`、`pt-PT`→`pt-BR`；`zh-Hant`、`zh-TW/HK/MO`→`zh-TW`，`zh-Hans`及其余中文地区→`zh-CN`，显式文字体系优先于地区。未支持或无效的环境文化回退英文，空环境值沿用系统UI语言。环境变量只影响该进程及其继承环境的子进程，不读取或改写App包内语言偏好。语言在进程启动时确定，持续运行的MCP宿主须重启并重新指定语言，不提供逐次execute调用的语言切换。

```powershell
rodel-mc --language zh-CN content install --help
rodel-mc help instance create --language=ar
$env:RODEL_CLI_LANGUAGE = 'zh-CN'
rodel-mc content install --help
$env:RODEL_CLI_LANGUAGE = 'ar'
rodel-mc help instance create
Remove-Item Env:RODEL_CLI_LANGUAGE
```

只设置`CurrentUICulture`，不改变业务数字格式。动态错误使用完整格式资源，命令名、选项名、协议字段及取值不翻译。System.CommandLine标准提示由库自身负责，Core和第三方返回的原始错误保留，不宣称所有结果文本已本地化。输出始终为UTF-8；阿拉伯语的字形连接和双向排版由终端与字体负责，不向命令或JSON协议插入方向控制字符。

全局`--language`、`--data-dir`、`--json`、`--events`等可放在命令前后，功能选项放在相应命令路径之后。未知/不适用选项、必填项缺失、重复的单值选项、无效整数/布尔值在执行和后台排队前明确返回参数错误（退出码2）；不再静默忽略或退回默认值。只有帮助中声明`--plan`的操作支持预览，例如`content add ... --plan`和`pack import ... --plan`会拒绝，而不是执行写入后假称只读。

含空格的路径/名称须保持为一个token。可重复选项逐项提供：`--jvm-arg=-Dkey=value --game-arg=--demo --game-arg="space value"`，同样适用于`--env`、`--select-version`、`--library-mirror`和包可选文件选择。以`--`开头的位置参数放在命令路径和选项后的分隔符之后，例如`config set -- note "--原样保存"`；不要把分隔符放到命令词之前。不会展开`@response-file`或执行库的环境变量/调试指令。

MCP具名工具自动处理以上token边界，通用`execute.arguments`遵循同一语法。后台命令按解析结果保存规范化token，移除的只是实际`--background`选项，不会误删同名的游戏参数或配置值。任务仅保存当前`request`：Core typed任务或CLI标准命令任务，不重复写顶层arguments。旧工具任务、缺失request的记录及“选项、分隔符、命令路径”的旧顺序不会迁移或重放；取消与可重试任务的恢复使用当前request。

## JSON协议与工具schema

CLI与MCP共用强类型结果外壳：`schemaVersion`、`operation`、`status`、`data`、`warnings`、`error`。状态稳定为`succeeded`、`failed`、`canceled`；失败保留错误码、原始消息、网络原因/HTTP状态和可恢复数据。原生CLI错误及`tools list`现也包含operation与warnings，既有字段和退出码保持。业务数据及warnings允许未知字段和既有异构条目。

默认启动器目录的工具输入使用typed模型与System.Text.Json源生成反序列化，输入/输出schema由同一元数据导出，不再手写第二份字段类型/必填表。构造参数定义必填与默认值，范围/最少条目约束同时用于执行前校验与schema。未知顶层参数仍不猜别名：忽略并返回`IGNORED_ARGUMENTS`；已声明参数的错误类型、显式null或缺失必填项在业务执行前返回`INVALID_ARGUMENT`。皮肤专用目录复用源生成元数据，nullable可选字段按其schema接受null，不套用启动器参数的说明和省略约定。

`config_set.value`例外允许任意JSON且必须存在：字符串`"null"`与null不同，缺失不是null。游戏设置`changes`和启动`environment`只接受字符串、数字、布尔或null；数字不经double转换，空字符串保留，null沿原规则删除。空`resourcepack_select.names`和空`execute.arguments`仍可用，更新审核`files`至少一项。MCP结构化结果与文本内容来自同一份已序列化结果；分页、截断、保存与`result_read`继续保持原续查语义。

## AI：管道、后台回合与有界请求

先按[AI使用](AI.md)配置服务与默认模型。配置时`--model`是实际模型引用；生成/会话选择时它是返回的模型绑定ID，不要混用。普通启动器命令与MCP不要求模型配置。

一次性分析文本不创建会话、不提供工具。PowerShell 7可把UTF-8日志送入明确选定的模型；文本会发送给该服务，敏感内容应先脱敏：

```powershell
Get-Content .\latest.log -Raw -Encoding utf8 |
    rodel-mc --data-dir C:\launcher-lab ai request --prompt-stdin --max-output-tokens 512 --temperature 0 --timeout-seconds 60
rodel-mc --data-dir C:\launcher-lab ai request --service SERVICE_ID --prompt-file .\question.txt --max-output-tokens 256
```

`--prompt`、`--prompt-file`和`--prompt-stdin`互斥，不再让文件无声覆盖显式文本。输入上限64000字符，超出明确失败，不截断后发送。stdin仅供直接CLI的重定向输入，MCP的stdin承载协议，不能被读取为提示。`request`及`chat send`的输出上限只覆盖当前请求，不改玩家保存的模型配置；未提供时使用保存值或统一能力解析得到的真实provider/models.dev输出容量，没有可靠容量就省略参数。没有4096默认值或65536人为上限，显式正数原样交给服务判断；`request`返回`effectiveParameters`，服务不支持参数时保留实际错误。显式连接选择为`ai request --service ID`，旧`ai service test`不保留。

需要保存和继续讨论时，先创建会话，再发送并保留返回的turn ID：

```text
ai chat create --title "实例日志排查" --instance INSTANCE_ID
ai chat send CONVERSATION --expected-revision REVISION --prompt-file question.txt --no-tools
ai chat send CONVERSATION --expected-revision REVISION --prompt "参考服饰和帽子" --image outfit.png --image hat.jpg
ai chat turn wait CONVERSATION TURN --events --after 0 --timeout-seconds 30
ai chat turn events CONVERSATION TURN --after SEQUENCE
ai chat turn cancel CONVERSATION TURN
ai chat turn cancel CONVERSATION TURN --cancel-queued
ai chat turn run-now CONVERSATION TURN
```

`turn run-now`将指定排队消息移到下一轮，并停止同一会话的当前响应；其他排队消息保持相对顺序。重复调用同一已开始的回合不会重新发送或再次打断。上下文按实际执行顺序接续，优先消息的回答会进入随后恢复的队列消息。

`chat wait`只观察已存在回合，不重新发送模型请求；`--after`恢复事件游标。观察超时返回`AI_WAIT_TIMEOUT`及conversationId/turnId/eventCursor，原回合继续，稍后可重新等待。stdout仍是一份结果外壳；`--events`将AI事件与普通进度JSON写到stderr，AI事件以`kind/sequence`区分。完成、失败或取消沿用实际终态与退出码，不因观察结束就伪称生成成功。

默认回合由Core持有，CLI退出不取消。`chat send --wait --cancel-on-exit`才把本轮生命周期绑定到该发送进程；单独的观察者不取得控制权。`--client-key`可传32位GUID实现同内容幂等；`--max-turns`限制本轮模型调用次数（含压缩选择），不是历史对话条数。上下文优先使用服务端token计数和容量，无计数接口时采用本地Tiktoken估算及usage校准；已删除`--history-characters`字符门限。写操作需另行授权且仍保留产品精确确认。

`chat send --image FILE`可重复最多3次，接受真实PNG/JPG/WebP字节；发送前冻结图片，不把任意路径交给模型。相同`--client-key`和未变化的文件复用原附件与回合，变化则明确冲突，不悄悄改写已提交图片。`ai chat turn retry CONVERSATION TURN --expected-revision REVISION`沿用原提示和冻结图片，不重读已修改或删除的来源文件。没有额外的图片MB上限或静默降采样；模型不接受图片时返回其实际错误。

`ai local models/status/unload`操作Core拥有的Embedded引擎，不要求独立rin服务。服务/模型配置的`--model`接受SDK模型库引用或已有GGUF绝对路径，不另加导入命令；加载与推理归Inference，见[本地推理](AI.md#本地推理)。这些命令没有新增模型品牌或人工工具能力门槛。
`ai model save --context-length TOKENS`保存本地模型的显式加载上下文，由SDK传给worker；未指定保留既有值，
`--clear-context-length`只清除上下文覆盖，保留温度和输出设置；`--clear-parameters`仍清除全部参数。默认64K，若GGUF标注更小则取标注。`ai local inspect --path FILE.gguf`读取标注和默认预算，不加载权重。新配置与旧记录在Core请求捕获时采用同一默认，
不要求手动补传参数，也不写回用户配置；显式覆盖仍优先。它不是历史字符预算或输出token上限，也不向在线provider发送。

## 控制运行中的 App

业务操作仍用instance/content/runtime等命令；`app`只负责包偏好和真实界面宿主。
先用App实际数据根发现目标，不读写包LocalSettings文件，也不隐式启动App：

```text
app list
app show APP_ID
app preferences update APP_ID --changes '{"theme":"Dark","language":"en"}' --expected-fingerprint FINGERPRINT
app navigation set APP_ID --page settings --category general --expected-fingerprint FINGERPRINT
app window set APP_ID --state minimized --expected-fingerprint FINGERPRINT
app wallpaper list APP_ID --theme light
app wallpaper select APP_ID --theme light --wallpaper WALLPAPER_ID --expected-fingerprint FINGERPRINT
app wallpaper import APP_ID --theme light --source IMAGE --expected-fingerprint FINGERPRINT
app wallpaper rename APP_ID --theme light --name NAME --expected-fingerprint FINGERPRINT
app wallpaper remove APP_ID --theme light --confirm --expected-fingerprint FINGERPRINT
app cache show APP_ID
app cache clear APP_ID --confirm --expected-fingerprint FINGERPRINT
app close APP_ID --confirm --expected-fingerprint FINGERPRINT
```

每次修改前读取`app show`的新指纹；状态变化返回`APP_STATE_CHANGED`，不静默覆盖。
`preferences update`支持theme、language、afterLaunch、closeBehavior、dataDirectory，
可用`--file`提供同一JSON对象，与`--changes`互斥。数据根偏好只影响下一次启动，明确返回restartRequired，不搬移或切换当前实例数据。
窗口状态支持normal/maximized/minimized/tray，保留Sprout原生窗口与托盘恢复机制。
选择另一实例不能丢弃活动草稿；无关窗口操作不被草稿一概禁用。退出仍执行App现有的Flush和任务保护。
图像缓存只归当前包，不等于Core依赖缓存；壁纸使用真实目录、解码和副本，来源图像保留。

发现记录位于所选数据根`app/hosts`，含私有随机认证值；公开结果不返回认证值，AI文件快照即使获广目录授权也排除此处。
控制校验实际进程启动身份、OS包身份、当前用户管道、服务端PID与请求ID；关闭连接/超时不自动重放修改。
没有匹配宿主时明确返回`APP_NOT_RUNNING`，不把Core后台AI宿主当成App。
这里只保留Core/CLI/MCP侧契约和受控管道，Sprout端接线已删除；上面的宿主操作语法不代表当前App已提供端点。
没有有效宿主时发现为空或操作返回`APP_NOT_RUNNING`，不能猜写包偏好或回退到坐标点击。
## 皮肤绘图与3D预览

`skin`经Core存储适配调用独立`Rodel.Minecraft.Skins`图像编辑库，与未来手动编辑器共用绘图代码，不请求模型生成图片。支持Classic/Slim × 64×64/128×128及独立内外表面；128使用实际细分纹素编辑与同尺寸导出，人物几何不放大。Java标准账号上传仍需64×64，编辑/导出不上传账户。旧64×32继续按Java规则镜像肢体、规范化透明度；PNG导入明确选择模型。

```powershell
$created = rodel-mc --json skin create --model Slim --color '#263449' | ConvertFrom-Json
$id = $created.data.documentId
rodel-mc skin inspect $id
rodel-mc skin read-face $id --part Head --layer Base --face Front
rodel-mc skin edit $id --revision 0 --edits '[{"tool":"Pencil","surface":{"part":"Head","layer":"Base","face":"Front"},"x":2,"y":3,"color":"#FFFFFF"}]'
rodel-mc skin undo $id --revision 1
rodel-mc skin redo $id --revision 2
rodel-mc skin export $id --output .local\skin.png
rodel-mc skin preview $id --output .local\skin-preview.png --yaw 25 --pitch 10
```

`create --input FILE`导入PNG。`edit`必须且只能指定`--edits`或`--edits-file`，其内容为同一typed操作数组。绘图的`surface`指定`part`、MC内外层`layer`及`face`，坐标从该面左上角开始，左右均指角色自身。`inspect`返回全部面的尺寸、贴图矩形、文档版本和绘图层信息；`read-face`读取可见合成，加`--paint-layer ID`读取原始绘图层或`background`。颜色使用`#RRGGBB`或`#RRGGBBAA`。

`preview`只发布PNG快照，不生成HTML客户端，不提供网页编辑或查看器导出。手动编辑界面归Sprout App；绘图、几何与拾取继续由独立库提供。

基础工具为`Pencil`、`Eraser`、`Stroke`、`Line`、`Rectangle`、`Ellipse`、`Fill`、`Replace`、`Shade`、`Gradient`、`AdjustColor`、`Copy`、`Move`、`Cut`、`Paste`、`FlipHorizontal`、`FlipVertical`和`Layer`。完整参数由`tools list --tool-profile skins`中的typed schema及[独立库契约](../src/Rodel.Minecraft.Skins/README.md)说明：

| 操作 | 参数与行为 |
| --- | --- |
| 笔刷 | `brushSize`省略为1，`brushShape`为Square/Circle，`opacity`为0至1，`softness`为0至1；`lockAlpha`保留目标alpha且不在空白像素上产生颜色 |
| 连续笔划 | `points:[{x,y,surface?}]`，整数是像素中心；自动连线，同一动作重复采样不反复叠色；每笔1至4096点 |
| 镜像/跨缝 | `mirror`为None/Body/Part；`wrapFaces`使笔刷及连通填充越过同一部位的立方体边缘。相邻面笔段展开连线，不相邻面/不同部位/不同MC层断笔 |
| 形状/桶 | 形状使用含端点的`x2/y2`及`filled`；`fillMode`为Connected/Matching/Face/Part/Layer，支持0至255的RGBA逐通道`tolerance`及`sampleMerged` |
| 渐变/色彩 | 渐变用`color/secondaryColor`、端点及Linear/Radial `gradientKind`；`Shade.amount`为-255至255，`AdjustColor`使用`hue`角度及-1至1的`saturation/brightness/contrast` |
| 选区 | `selection`支持矩形、椭圆、多边形、像素mask和反选；当前选区限制在一个模型面，镜像反射该足迹。`skin select`可按几何或同色/连通像素生成 |
| 复制/移动 | `source/sourceSelection`与目标`selection`独立；可设`quarterTurns`、`flipX/flipY`和最近邻`destinationWidth/destinationHeight`。移动拒绝被目标选区裁掉的像素 |
| 剪贴像素 | `skin capture`返回typed `clipboard`，交给Paste，可跨面/跨文档使用；不代表操作系统剪贴板接入 |
| 绘图层 | `tool:Layer`无需surface，通过`layerChange.operation`执行Add/Set/Remove/Reorder/Duplicate/MergeDown；Id稳定，支持Name/Visible/Opacity/Locked。写入使用`paintLayerId` |

绘图层是独立RGBA纹理，**不是MC的Base/Outer两层几何**。背景Base保持不透明，半透明画笔正常混色；附加绘图层的Base区域可以透明，导出时叠加到必需背景上。剪切/移动若要清空背景Base，须用不透明`clearColor`；Eraser仅能擦除背景Outer或附加绘图层，基础身体用不透明颜色重画。图层合并按当前显隐和不透明度烘焙，撤销可找回源图层。复制/替换默认Replace，其它绘画默认Normal源覆盖；不提供任意混合模式。

Cut、Move及翻转必须使用`opacity=1`且`lockAlpha=false`；不适用的画笔设置会在清空来源前明确失败，不会静默丢像素。半透明或透明锁绘制改用Copy/Paste。批次取消传播到库内工作，取消和失败均不保存部分图层/像素。

```powershell
rodel-mc skin edit $id --revision 3 --edits '[{"tool":"Layer","layerChange":{"operation":"Add","id":"details","name":"Highlights"}},{"tool":"Stroke","surface":{"part":"Head","layer":"Base","face":"Front"},"paintLayerId":"details","points":[{"x":1,"y":2},{"x":6,"y":5}],"color":"#FFFFFF","brushShape":"Circle","brushSize":3,"opacity":0.5,"mirror":"Body","wrapFaces":true}]'
rodel-mc skin select $id --request '{"surface":{"part":"Head","layer":"Base","face":"Front"},"kind":"Ellipse","width":8,"height":8}'
rodel-mc skin capture $id --request '{"surface":{"part":"Head","layer":"Base","face":"Front"},"paintLayerId":"details"}'
rodel-mc skin pick $id --request '{"origin":{"x":0,"y":28,"z":20},"direction":{"x":0,"y":0,"z":-1},"layer":"Outer","skipTransparent":false}'
rodel-mc skin preview $id --output .local\head.png --view '{"parts":["Head"],"baseVisible":true,"outerVisible":true,"size":384}'
```

选择、捕获和拾取返回观察修订号，不修改文档。`skin pick`是真实模型空间射线求交，返回面、像素索引、可直接用于笔划的连续坐标和法线；未命中为`hit:null`，无效射线失败，不伪造目标。

一批1至512个动作全部成功后才保存，像素及绘图层共同作为一次撤销；保留最近32次完整状态快照。撤销/重做也递增修订号，有实际变化的编辑才清空重做栈，空笔划保留未来历史。手动客户端与AI必须提交最近观察的修订号；冲突时重读，不盲目重放。文档保存在数据根`skins`，文件租约和临时发布保护跨进程编辑。当前库文档格式3包含模型、尺寸、背景名称、事务描述及完整图层/历史，Core工程在同一原子记录中保存名称、更新时间和候选提交回执；不隐式读取/迁移旧开发格式1/2，也不删除旧文件；标准PNG仍可导入新文档。已有导出文件须显式`--overwrite`，PNG不保存绘图层/历史，导出不等于修改账号或游戏当前皮肤。

皮肤工程文件扩展名为`.rmcs`（Rodel Minecraft Skin），保存在数据根`skins/<id>.rmcs`；PNG导出仍使用`.png`。新保存使用`rodel-skin-project/1`压缩容器：`project.json`保留格式3文档及全部历史元数据，像素以SHA-256引用共享的压缩小端uint32纹理，当前内容与各历史状态相同的纹理只存一次。读入校验摘要，公开可编辑数组与历史仍独立，不因去重相互污染。已有的当前格式3纯JSON工程可继续读取，成功保存时使用容器；不减少历史步数，不恢复格式1/2。

PNG预览通过实际立方体面求交、深度和可见图层合成生成，不是平面展开图。`--view`为typed JSON相机/尺寸/内外层/部位配置，不能与简易`--yaw/--pitch`同时使用；`parts:null`显示全部，`parts:[]`隐藏全部。HTML查看器自包含贴图和同源几何，无网络资源、无需启动服务器，支持旋转、缩放、键盘、内外层及部位显隐。两者都是快照；编辑后需重新导出，不是可用鼠标绘画的手动编辑器页面。

皮肤专用MCP使用`mcp serve --stdio --tool-profile skins`，见[AI接入](AI.md#独立皮肤-mcp)。游戏内本地验证采用隔离实例中的CustomSkinLoader，不内置或自动安装该模组，不改正版账号，不保证其他玩家可见。

已核对CustomSkinLoader `v15.1`（commit `5830503dfce07eb272a58754f3d748ba77415107`）的[LegacyLoader.java](https://github.com/xfl03/MCCustomSkinLoader/blob/5830503dfce07eb272a58754f3d748ba77415107/Common/src/main/java/customskinloader/loader/LegacyLoader.java)及[Config.java](https://github.com/xfl03/MCCustomSkinLoader/blob/5830503dfce07eb272a58754f3d748ba77415107/Common/src/main/java/customskinloader/config/Config.java)：本地纹理位于游戏目录的`CustomSkinLoader\LocalSkin\skins\<玩家名>.png`，配置数组名为`loadlist`，条目是对象而非站点名称字符串。测试副本仅配置`LocalSkin`的`Legacy`来源，并对Slim明确设`model:"slim"`，避免在线源优先覆盖。不把该配置覆盖到玩家已有实例。

### 工程、手绘预演与候选审核

`skin templates`返回五套原创人物模板及一个空白披风模板ID和建议模型；披风使用`--template cape --model Cape`，尺寸以宽度64/128指定。`blank`与`cape`以逐面灰度和淡灰细线提供可编辑的起稿参考。创建可选`--template alex --model Slim --size 128 --name NAME`，模型与尺寸独立。`skin list/rename/clone`发现、命名和复制工程；复制得到新ID和独立内容，重新开始历史。`skin import ID --input FILE --model MODEL --revision N`替换现有内容，模型、尺寸和图层可一次撤销。增加`--as-layer`则将同尺寸PNG作为新顶层，保留全部RGBA和既有内容，不指定`--model`；可用`--name NAME`命名，缺省使用文件名。`skin export ID --paint-layer LAYER_ID --output FILE`只导出指定层原始RGBA，不受隐藏/锁定/图层透明度影响；省略`--paint-layer`仍合成可见层，已有目标需显式`--overwrite`。`skin history ID`返回观察修订与历史条目；`skin restore ID --index I --revision N`一次跳转，不按步数多次保存。

`skin edit` 和 `skin_edit` 支持在 `Pencil`、`Stroke` 或 `Eraser` 动作中传入 `"transparencyMask":true`。前两种隐藏合成像素，橡皮清除对应遮罩，三者使用硬边全强度笔刷和实际表面坐标。动作直接作用于工程遮罩，`paintLayerId` 留空。工程、撤销与重做保留遮罩，合成 PNG 输出零 alpha，指定绘图层的 PNG 输出该层原始 RGBA。局限于指定图层或像素的授权范围不允许变更全局遮罩。

`skin save-as ID --revision N --output FILE.rmcs [--overwrite]`另存完整可编辑快照，保留图层、当前状态及现有撤销/重做，不修改原工程。默认不覆盖目标，覆盖须显式指定；修订漂移、取消或发布失败不改来源。副本不携带原工程的执行回执或独立AI会话；重新打开外部文件时仍复制到数据根的新身份。它不是PNG导出，也不同于重置历史的`skin clone`。App工程菜单及`Ctrl+Shift+S`使用同一Core能力和原生保存选择器，选择器确认覆盖后才发布；当前工程继续在库内自动保存，不与另存文件建立实时链接。

`skin footprint ID --request EDIT_JSON`读取准确笔刷足迹，`skin preview-edits ID --revision N --edits EDITS_JSON --output FILE`输出未提交预演；`saved:false`表示没有保存工程。Pencil/Eraser的`points`与Stroke共用整笔采样和coverage去重，抬笔后使用一次`skin edit`提交，不能逐pointermove保存。

宿主通过`skin scope create ID --request LIMITS_JSON`创建持久、可撤销授权，`skin scope show/revoke SCOPE`查看/撤销。limits包含`paintLayerId`（null为background）、`surfaces`（null为全部，空数组为无）、atlas尺寸的`pixelMask`、`readOnly`及`allowLayerChanges`。表面范围与mask取交集。授权只允许目标层；明确允许时可新增该附加层或修改其元数据，不得删除/重排其他层，背景元数据始终保护。Core检查实际逐层像素和最终合成diff，镜像、跨面、全层填充、剪切与显隐/透明度变化同样受范围限制。

```powershell
$grant = rodel-mc --json skin scope create $id --request '{"surfaces":[{"part":"Head","layer":"Base","face":"Front"}]}' | ConvertFrom-Json
$scope = $grant.data.scopeId
rodel-mc --data-dir PATH mcp serve --stdio --tool-profile skins --skin-scope $scope
```

带`--skin-scope`的MCP仅提供绑定工程的读取、拾取、选区、历史、足迹和候选生成/查看/放弃/受管预览/回执查询；不提供直接编辑、创建授权、应用候选、任意文件路径、跨工程result_read或通用execute。另一个documentId、scopeId或proposalId在派发前拒绝，Core在暂存/应用时再次复核授权。未绑定的skins目录保留完整命令能力，供明确授权的宿主使用；MCP annotations本身不授予权限。

`skin proposal create SCOPE --revision N --edits EDITS_JSON`冻结候选像素，保持工程及历史不变；`show/preview/reject PROPOSAL`查看、预览或放弃。宿主审核后执行`skin proposal accept PROPOSAL --revision N`，一次保存内容和持久回执；`skin receipt ID --operation PROPOSAL`可核对连接中断的结果。同一候选重复accept只返回原回执，即便后来已撤销，也不会再次绘制。手动编辑使候选stale，授权撤销或候选放弃会阻止应用；重新生成使用最新修订，不能修改旧修订号绕过冲突。候选预览保存在数据根`skins/previews`，没有调用方选择的任意输出路径。

内置AI会话可用`ai chat create --skin-scope SCOPE`或Core的`AiChatCreate.SkinScopeId`绑定该授权；不能同时绑定instance。该会话只连接固定CLI的绑定skins目录，不装配通用launcher/第三方/托管文件与网络工具，继续使用现有provider、权限、intent journal与未知效果阻断。AI生成候选需要本轮操作许可，候选应用仍由宿主执行。

## 从游戏开始

```text
instance create NAME --version VERSION
instance import NAME --directory GAME [--minecraft-directory LIBRARY_ROOT] [--version VERSION]
instance list
instance show ID
install ID
launch ID --plan
launch ID [--use-cached-external-session] [--offline] [--java JAVA_EXE] [--player NAME] [--account ID] [--memory MB]
launch ID --wait
repair ID [--plan]
process list
process show SESSION
process stop SESSION [--force]
logs SESSION --tail 100
logs SESSION --bytes 65536 --stdout-offset 0 --stderr-offset 0
logs export SESSION report.zip
diagnose ID [--verify]
diagnose --session SESSION
```

CLI的`instance import`未带`--copy`时关联原目录；`--migrate`沿用可读的来源设置，`--copy`则完整复制迁入托管实例。App目录导入始终提交`Migrate=true, Copy=true`，不能把CLI的关联默认当作App行为。多版本目录可在导入时明确`--version`，启动也可作一次性覆盖。`--minecraft-directory`允许实例配置与共享游戏依赖分开。`--plan`只读，不安装、不解压。普通启动按需补齐依赖；`--offline`使用本地文件直接尝试，推荐Java的提示不构成硬性拒绝。显式`--java`始终优先；未选择Java时可按官方组件安装推荐运行时。

启动返回的 `running` / `readiness=process_started` 表示进程已启动。Windows会话观察在核对PID与启动时间后，以`readiness=window_visible`表示已找到该进程或确证子进程的可见主窗口；`windowObservationWarning`保留观察失败。两者都不能当作主菜单、世界或服务器已就绪。默认 CLI 退出后游戏继续运行；`--wait` 等到退出，非零游戏退出码作为失败返回并保留会话和日志。普通停止请求关闭窗口；`--force` 明确终止所记录的游戏进程树。

`launch --game-language zh_cn|en_us|ar_sa`显式选择游戏语言，也接受其他合法Minecraft地区语言代码。计划仅报告，不改游戏文件；实际启动由Core在准备租约内只更新`options.txt`的`lang`，保留其他行、编码和换行。旧客户端按已有资源索引保留`zh_CN`等大小写。CLI省略时保留玩家语言；App每次启动跟随当前15种应用语言，由共用LauncherLanguages映射到游戏代码（如简中`zh_cn`、繁中`zh_tw`、日语`ja_jp`、葡语`pt_br`），不修改已运行游戏。选项文件无法安全更新时保留原文件并继续启动，返回`gameLanguageWarning`及warnings，不静默假称语言已同步。

## 实例、配置与身份

```text
instance copy ID NEW_NAME
instance move ID TARGET_DIRECTORY
instance remove ID [--delete-files]
config get [KEY] [--instance ID]
config set KEY JSON_OR_STRING [--instance ID]
account add-offline PLAYER_NAME
account list
account current
account select ID
account clear
account login [--method browser|device-code] [--account ID] [--events]
account retry-login
account refresh ID [--skin-only]
account remove ID
account external login [ADDRESS] --username USER --password-stdin [--account ID]
account external device [--account ID]
account external poll ATTEMPT [--wait]
account external show ATTEMPT
account external select ATTEMPT PROFILE_UUID
account external cancel ATTEMPT
account external component show
account external component prepare [--file AUTHLIB_INJECTOR_JAR]
java list [--java PATH]
java managed
java available [--offline]
java install COMPONENT [--architecture x64|x86|arm64]
java import JAVA_EXE_OR_DIRECTORY_OR_ARCHIVE
java select JAVA_PATH [--instance ID]
java remove RUNTIME_ID
```

`java remove`永久删除未被引用的托管运行库及其登记，不建立恢复目录；外部Java仅解除登记，不删除原文件。仍被全局、实例或活动游戏引用时拒绝。文件占用等删除错误会保留登记以便重试，结果中的`warnings`说明保留的未知文件或目录清理问题。`java available`返回`source`和必要的`warning`，区分网络目录、网络失败后的缓存回退和显式离线；缓存不可写不应把已获取的新候选替换为旧目录。

默认移除实例只解除登记。`--delete-files` 仅删除确认属于托管实例的目录，关联的原目录保留。有效配置返回值和来源；常用键为 `minMemoryMb`、`maxMemoryMb`、`javaPath`、`playerName`、`accountId`、`jvmArguments`、`versionId`。初始内存高于最大内存时，会下调到最大值并说明原因。

未指定内存策略时默认`memoryMode=auto`，普通实例与整合包一致，按当前可用内存及自动分配边界选择堆大小。明确保存的手动模式、单独指定的手动最大内存以及自定义`-Xmx`继续优先，不由默认值改写。

微软登录是用户显式发起的可选操作。Client ID依次取显式选项、进程 `RODEL_MICROSOFT_CLIENT_ID`、Windows当前用户同名环境变量、正式构建注入的默认资源。正式包自带公开OAuth应用标识，无需玩家配置；源码不保存实际值，包和源码均不携带Client Secret。开发构建可省略默认值，全部未配置或选中的覆盖值无效时返回 `AUTH_NOT_CONFIGURED`，不偷偷忽略错误覆盖。CLI默认浏览器授权，设备码方式须加 `--events`，一次性授权提示仅发给前台stderr，不作为后台任务保存。原生App默认使用WAM，首次登录、重新登录及无缓存重试均绑定当前窗口；仅WAM运行组件初始化失败时提供显式浏览器替代入口。`retry-login`复用当前应用尚未完成的加密授权缓存，再核验游戏权限/资料，不要求再次同意；没有缓存时提示重新登录。离线身份不依赖微软登录；发行注入见[发布说明](RELEASE.md)。

微软登录链中的Xbox/XSTS/Minecraft HTTP请求安全连接失败返回`AUTH_TLS_ERROR`，其他传输故障返回`AUTH_NETWORK_ERROR`；诊断包含`stage`、服务`host`、`networkReason`及内层异常类型/HResult，排除令牌、认证响应和原始内层异常文本。网络恢复后可用原账户重试，网络错误不会将账户标为需要重新登录。

外置账户类型为`external`，LittleSkin预置来源使用设备码，其他Yggdrasil来源使用地址、用户名和密码；网站地址自动通过ALI定位API，不另设发现确认步骤，显式HTTP按原协议使用。`login --password-stdin`仅从直接调用的前台CLI读取一行密码，MCP/非交互宿主拒绝读取，密码不进入参数或持久任务。角色不唯一时返回加密尝试的句柄，`select`绑定所选UUID；`cancel`删除尝试，添加不改变默认账户。`account refresh`按账户来源派发；加`--skin-only`只查询并更新皮肤地址，不续期凭据或改变登录状态，失败保留已有资料。

App在打开、返回前台、切换账户及打开账户菜单／管理列表时静默检查在线角色皮肤，同一角色查询合并、普通冷却5分钟，失败按10／20／40／60分钟退避。登录刚返回的profile按同一新鲜度复用，不立即重复查询。旧头像保留到新图可用，不新增刷新按钮或失败弹窗；这些窗口触发与冷却属于App，CLI显式查询不受其冷却限制。

LittleSkin使用Rodel客户端ID`1645`，开发验证可用`RODEL_LITTLESKIN_CLIENT_ID`覆盖。设备流需LittleSkin开通白名单；当前真实请求返回`invalid_client / Client was not found or not whitelisted`，设备授权、真实角色交换和入服尚未实测。通用Yggdrasil的邮箱密码登录不依赖此OAuth白名单，包括手动填写LittleSkin API地址的路径；不借用其他启动器的应用身份。

外置启动自动准备并校验官方authlib-injector，或用`component prepare --file`复制并核对已有JAR；失败保留旧组件。`launch --use-cached-external-session`只显式尝试同一外置账户，要求受保护会话、元数据和完整组件齐全，不转换离线身份，不保证联机成功。会话保存选择标记和占位符，真实令牌只在启动内存中替换。

首个成功登记的离线、微软或外置账户自动成为全局默认，后续新增不覆盖已有或显式清空的选择。`select`设置全局默认，显式启动`--account`及实例覆盖仍优先。移除账户会清理对应全局/实例引用与本地凭据，不删除游戏、存档或Windows账户；不会自动改用另一个玩家。App首次启动游戏前若无身份，空账户列表直接要求创建具名离线身份，已有账户则要求选择，完成后继续原启动；取消不创建任务。全局设置不再提供重复的默认离线玩家名；Core/CLI已有离线身份、显式手工playerName与实例配置继续可用。有效在线会话只在游戏进程启动时注入内存；启动计划、会话文件及日志不持久化令牌。Windows DPAPI缓存绑定当前Windows用户，损坏/换应用ID后需重新登录。

一个实例的登记文件损坏不会挡住其他实例列表；该项返回 `status=unreadable`、文件位置和错误，方便单独修复。Java 架构能探测时用于库与 native 选择；自定义 Java 不支持属性探测时仍可使用，不因探针失败被排除。

保存的相对 `javaPath` 以实例游戏目录为基准，换一个终端目录启动不会变；临时 `--java` 相对路径以调用者当前目录为基准，绝对路径优先。Windows 命令行过长时，已识别的 Java 9+ 使用参数文件，Java 8 使用清单 classpath jar。计划会返回 `commandLineStrategy`，会话记录实际采用的方式。普通短命令保持原参数；自定义 Java 探针失败不会使它失去尝试机会。清单方式适用于 classpath 太长，不能承诺压缩任意超长 JVM 选项，也不把它说成与扫描 `java.class.path` 的所有模组完全等价。

迁移可以先看清再导入：

```text
instance inspect-import SOURCE [--profile PROFILE_ID] [--minecraft-directory SHARED]
instance import NAME --directory SOURCE --migrate [--profile PROFILE_ID]
instance import NAME --directory SOURCE --migrate --copy
```

预览识别标准`.minecraft`、官方profile、Prism的`instance.cfg/mmc-pack.json`以及受支持的HMCL/PCL配置，列出版本、Java、内存、JVM参数的来源和未知配置。CLI默认关联原目录；`--copy`复制完整游戏内容、所选版本继承链、共享资源和可读取Java，完成后才登记，来源始终不删改。App采用此复制路径。Prism组件清单不会被冒充成标准Minecraft版本JSON：不能直接转换的加载器会提示，仍可指定已有标准版本或另行安装对应加载器。多profile实际导入用`--profile`明确选择；HMCL/PCL更换版本后重新预览隔离目录，必要时用`--game-directory`明确游戏目录。

## 长任务与备份

```text
install ID --background
job list
job show JOB
job cancel JOB
job resume JOB
backup create ID [--scope worlds|config|all] [--include-downloads]
backup list [--instance ID]
backup inspect BACKUP
backup restore BACKUP [--instance ID]
backup rollback RESTORE_OPERATION
backup delete BACKUP
```

后台任务与 CLI/MCP 连接寿命分离。取消安装保留已完成且校验正确的文件；恢复下载按文件重跑，当前不声称逐字节 Range 续传。已成功任务不会再次执行。可能已产生不可区分副作用的创建、启动类任务不自动重放，可从记录检查实际效果后明确重发原命令。

下载正文连续两分钟没有数据时会有限重试，最多四次尝试；持续有进度的慢下载没有总耗时限制。响应头或正文超时耗尽都作为网络失败，用户主动取消另记为 canceled。

`job list` 和 `process list` 把损坏记录单独标为 `unreadable`，其余条目仍可使用。不能判断活动状态的记录不会被缓存清理当作“肯定空闲”；能定位游戏目录的会话疑点只保护对应目录。

游戏stdout/stderr各保留最近1MiB的内存窗口，不逐块写盘。正常退出或主动停止不保存输出；非主动停止的非零退出才原子保存这份有界快照，不能把它当成整次运行的完整日志。MC自己的日志及崩溃报告仍由MC管理。CLI/MCP与App共用Core的当前用户管道读取，监控宿主结束后，未保存的内存窗口返回`source=unavailable`及说明，不伪造空的完整日志。

`logs launcher export DESTINATION`单独导出启动器最近7个UTC日的活动日志及清单，无需游戏会话，不包含游戏输出或配置；常见凭据尽力过滤，读取不到的单个分段保留警告，其余继续导出。目标已存在则拒绝覆盖。App设置中的启动器日志为独立入口；游戏日志另按实例和启动时间选择，不将多次运行显示成多个世界。

日志默认返回最多64KiB原始文本预算（两流分配），`--bytes`范围8～1,048,576；JSON转义会使传输体积稍大。`stdoutLog/stderrLog`是文本数组，`streams`报告`source/firstOffset/offset/nextOffset/hasMore/truncated/warning`。偏移是脱敏后流的绝对字节位置，不因窗口淘汰而归零；过旧偏移推进到最早可读位置并报告截断，UTF-8起点对齐。`logs export`与`logs bundle`只在明确请求时写目标文件，附带可用窗口及截断信息，不承诺恢复已淘汰内容。

启动器数据根`logs`中具名会话捕获（含旧分段）超过七天后自动清理：监控开始及持续运行的每日检查、日志读取/导出的按日检查触发。活跃/活动不明会话、目录链接、未知文件、MC自己的日志和用户另存的ZIP不删除。清理或快照保存失败有警告，不改变游戏原退出码，也不阻断另一条流的保存。

添加 `--events` 可将任务开始、下载/安装进度和完成事件以逐行 JSON 写到 stderr；stdout 保持一个最终结果。后台任务的最新进度可通过 `job show` 的 `progress` 查看。

整合包文件获取遵循 `network.concurrency`（默认8，范围1～64）；可注入的CurseForge provider仍按其原有顺序调用。文件阶段在清单与覆盖文件大小齐全时累计真实字节进度，否则累计条目内的已知进度；并发上报不会使总进度倒退。`pack-extract`、`pack-verify`分别表示解压及校验，`bytes/totalBytes`属于当前活动，文件阶段完成不代表游戏、加载器或Java已经准备完成。

`instance remove`会永久删除该实例的登记及其可识别的备份，即使不带`--delete-files`也是如此；该选项仅控制游戏文件。备份被占用时保留实例登记，可解除占用后重试。损坏且归属不明的归档保留，不按文件名猜测删除。

备份恢复采用 overlay：恢复备份包含的文件，保留备份之外的当前文件。恢复前自动完整备份并保留原目录，可按返回的 `operationId` 回滚。运行中备份标记 `consistent=false`；恢复必须在游戏停止后进行。

备份可读取选中范围内的目录链接。恢复时，内层链接会在新的实例目录中展开为独立文件，并返回 `materializedLinks` 提示，避免改写外部共享目录；保留的原目录可用于回滚。排除的下载目录会在遍历前跳过。

## 加载器与内容

```text
loader list fabric --minecraft 1.21.1 [--offline]
loader install ID fabric --loader-version 0.19.5
content list ID [--kind mods|resourcepacks|shaderpacks|datapacks] [--world WORLD]
content add ID LOCAL_FILE_OR_DIRECTORY [...] [--kind KIND] [--world WORLD]
content enable ID FILE_NAME [...] [--kind KIND] [--world WORLD]
content disable ID FILE_NAME [...] [--kind KIND] [--world WORLD]
content remove ID FILE_NAME [...] [--kind KIND] [--world WORLD]
content recover ID
content repair ID --session SESSION [--plan] [--select-version PROJECT=VERSION_ID ...]
content repair ID --review REVIEW_ID --confirm
content search QUERY [--platform modrinth] [--type all|mod|resourcepack|shader] [--offset 0] [--limit 20]
content review-updates ID FILE... [--kind KIND] [--channel release|beta|alpha] [--select-version PROJECT=VERSION] [--select-source FILE=PROJECT]
content review-install ID PROJECT [--kind KIND] [--content-version VERSION_ID]
content review-files ID LOCAL_SOURCE [...] [--kind KIND]
content apply-review ID REVIEW_ID --confirm
content project PROJECT [--platform PLATFORM]
content versions PROJECT [--minecraft VERSION] [--loader LOADER]
content install ID PROJECT [--content-version VERSION_ID] [--loader LOADER] [--plan]
content update ID FILE_NAME [...] [--plan] [--select-source FILE=PROJECT]
content pin ID FILE_NAME
content unpin ID FILE_NAME
content policy ID FILE_NAME --channel release|beta|alpha
content identify ID FILE_NAME [--algorithm sha1|sha512] [--attach]
```

世界使用独立管理流程，不是可启停的包：

```text
world list ID
world directory ID WORLD_FOLDER
world import ID WORLD_DIRECTORY_OR_ZIP [--name TARGET_FOLDER]
world export ID WORLD_FOLDER OUTPUT.zip
world backup ID WORLD_FOLDER
world backups ID [--world WORLD_FOLDER]
world restore ID WORLD_FOLDER BACKUP_ID --confirm
world remove ID WORLD_FOLDER --confirm
world recovery ID
world recover ID RECOVERY_ID [--name TARGET_FOLDER] --confirm
world compress ID RECOVERY_ID
```

新移除的世界保存在游戏目录`.rodel/world-history/RECOVERY_ID.zip`：压缩完成后逐项验证文件集合、内容摘要和目录，再清理未压缩副本。找回会核对ZIP整体摘要并解压，成功后清理已使用的ZIP；旧目录记录保持兼容。`world compress`可将已有保留目录转换为同一格式，返回实际压缩前后字节数。压缩期间取消或失败不移除活动世界，已提交后清理失败会明确报错并保留可用副本，不静默删除数据。系统不会自动定期清空这些保留记录。

导入完整复制世界及未知文件，保留来源；只排除根目录的临时`session.lock`。ZIP可直接放存档内容或包装一个世界目录，拒绝越界、符号链接及路径冲突，不覆盖同名世界/现有导出文件。读取NBT未知字段跳过，损坏或缺失元数据保留可管理项和警告，不写回NBT。恢复按单世界完整替换，不将新版额外文件覆盖混进旧备份；当前整个世界先保存在实例`.rodel/world-history`，移除也采用同一可恢复路径。恢复记录目标被占用时可用`--name`找回到另一文件夹，不覆盖当前世界。游戏或实例任务活动、Java锁占用时不执行写入/归档，防止不一致存档。世界备份位于数据根`backups/worlds`，与旧整实例overlay备份区分；MCP通用execute调用同一命令。

截图独立于世界和内容包，直接管理实例实际游戏目录的`screenshots`：

```text
screenshot list ID
screenshot show ID FILE
screenshot open ID [FILE]
screenshot rename ID FILE NEW_NAME
screenshot export ID OUTPUT_DIRECTORY FILE [...]
screenshot remove ID FILE [...] --confirm [--permanent]
```

`list`按文件修改时间倒序返回PNG/JPEG/WebP/BMP/GIF及逐文件读取警告，不解码原图、不创建空截图目录、不按世界分组。`open`指定文件时使用系统默认查看器，省略文件时创建或打开截图目录。重命名保留图片扩展名，不覆盖其它文件；导出为原字节副本，包含提交时才出现的同名目标也不覆盖。操作不要求退出游戏，但真实文件占用、所选路径/长度/修改时间变化仍会明确失败。

`remove`默认走Windows回收站：在实际删除前的Shell回调拒绝非回收操作，不回退为永久删除。只有显式添加`--permanent`才直接删除，不经过回收站、不能撤销，仍校验精确选择并要求`--confirm`。直接删除的成功项没有destination；用errorCode是否为空判断逐项成功，不能因没有回收路径判定失败。导出及移除保留逐文件结果、实际目标和取消状态，已成功的文件不因后续失败撤销；部分失败返回`SCREENSHOT_PARTIAL_FAILURE`及非零退出码，调用方只应处理明确失败项。两种移除方式缺少`--confirm`都退出2。外部MCP的`execute`复用这些命令；本功能没有新增内置AI专用工具，也不把剪贴板呈现移入Core。

搜索默认`all`，在一次分页请求中查询MOD、资源包和光影，不需要先选择实例；显式`--type`仍限制技术类型。`review-files`默认逐项识别混合来源，只有需要手工指定同一类时才传`--kind`。`review-updates`为所选已安装文件准备联合更新/依赖计划，保留固定、本地来源和逐项失败原因；执行保留禁用状态，并校验运行上下文及文件/跟踪基线。`review-updates`、`review-install`与`review-files`只准备冻结清单，最终批准后再执行`apply-review --confirm`；内容、目标上下文或来源变化须重新审查。MCP通用execute使用相同命令，不另造安装流程。

本地导入且没有在线来源的模组，检查更新时先用文件SHA1查询Modrinth；精确命中后直接检查兼容更新。未命中则按归档声明的模组ID与名称搜索，返回`items[].sourceCandidates`，原生界面显示名称、作者和项目标识供选择。模组ID不等于Modrinth项目ID，不会仅凭名称相似自动绑定。CLI可对选定候选再次执行`content review-updates ID FILE --select-source FILE=PROJECT`；多个选择重复传参。选定后下载候选到缓存并检查其包含原模组声明的全部ID，确认审核后才替换实例文件、保存在线来源；保留禁用状态、更新策略及旧文件备份。没有候选、联网失败或取消均保留本地文件，CurseForge仍不默认联网。

加载器种类为 `fabric/quilt/forge/neoforge`。现有自定义加载器仍可按标准本地版本元数据尝试启动，不必注册到上述列表。本地未知元数据JAR不要求平台ID。单项内容导入先核对所选分类的包格式，配套文本不能冒充包；同内容重复导入复用，禁用副本原位恢复；同名不同内容报冲突，不覆盖也不另生成活动副本。移除内容进入回收目录。数据包必须指定世界。这里的启停控制文件是否对游戏可见；资源包的游戏内选择仍由游戏设置决定。完整实例迁入仍保留所有游戏配套配置/额外文件，不用包列表裁剪复制。

模组还按 Fabric／Quilt／Forge／NeoForge 元数据声明的实际 ID 检查，文件名、显示版本和来源不同不能绕过。同批本地审核出现重复 ID 时必须保留一个候选；与已安装包 ID 相同且内容不同的来源，`review-files`会列出`items[].replacements`并标记`action=replace`，确认后复用内容事务备份旧文件、保留原启用状态再替换。多模组 JAR 的替换必须保留其中全部声明的模组；未知元数据仍按原文件流程处理。改名但字节相同的单份已安装包直接复用；`content add`不静默替换同 ID 的不同内容，返回`CONTENT_MOD_ID_CONFLICT`并要求审核。重新启用也拒绝与另一启用文件声明重复 ID；停用旧包后仍可启用所选版本。

`review-install` 的在线安装允许同实例多个冻结清单同时下载，仍使用既有 `apply-review --confirm --background` 和 `job show/cancel`。下载完成后在同一内容事务锁内复核：原有文件、来源策略与运行上下文必须保持不变；其它任务新增的独立项目可以共存，同一依赖只有版本、哈希及启用状态一致才能合并。冲突返回 `CONTENT_REVIEW_STALE` 并保留已安装内容，需要重新审核；本地导入、更新与运行任务不因此解除互斥。Sprout 的最小化下载列表观察同一 Core typed 任务，窗口关闭会对仍在进行的下载单独确认。

`content repair`目前对已知版本的Fabric实例自动汇总当前缺失依赖，查找并核对Modrinth候选归档，递归解析为去重的一组清单。这里的`--plan`会下载到缓存以核对身份与哈希，并保存review，但不改实例内容；与普通`content install --plan`只读元数据不同。只有用户最终批准完整清单后，才执行带review ID和`--confirm`的整组安装。歧义、冲突、不支持的版本范围和无法匹配的开发依赖会列入unresolved并阻止此组安装，不猜测替换，也不自动重启游戏。review后的内容/运行上下文变化须重新规划。MCP通用execute沿用同一命令。

批量操作逐项执行；一项失败不会撤销其他成功项。返回每项结果，部分失败使用 `CONTENT_BATCH_PARTIAL` 和非零退出码，便于只重试失败项。

一次在线安装或单项目更新及其必需依赖作为一组处理：先解析完整计划，再下载并校验全部文件，最后提交。`--plan` 返回节点、依赖边、旧文件、变化和冲突，不改游戏目录；通常只读元数据，本地模组手动选定来源时需下载候选到缓存以核对真实模组ID。两个项目要求同一个依赖的不同固定版本时，明确报告冲突；可重复添加 `--select-version PROJECT=VERSION` 表达自己的选择，覆盖上游要求会有提示。`--no-dependencies` 表示依赖由你手工管理，不要求未知本地 jar 注册平台身份。

主文件失败时不留下已生效一半的依赖组；提交时取消或进程中断，可以 `content recover ID` 恢复，下次写操作也会先检查。恢复核对文件与内容锁，发现玩家在中断后另改了文件会保留并指出位置，而非静默覆盖。下载缓存仍可复用。批量更新多个项目仍是多个独立组，返回每组结果。

固定版本后自动更新跳过该文件；`unpin` 恢复。默认不限制发布通道，沿用原有兼容范围；显式 `release` 只接受正式版，`beta` 接受正式版和 beta，`alpha` 接受全部。设置通道不会立即替换文件；先看更新计划再决定。禁用状态在更新后保留，修改过的本地文件不会自动丢弃。显式版本可超出上游的游戏版本/加载器/通道声明，但会说明差异。

`identify` 用文件哈希向 Modrinth 查询来源，默认只读；`--attach` 才把匹配的平台与版本记入内容锁。找不到匹配仍是可用的本地文件。搜索可加 `--minecraft`、`--loader`、`--index relevance|downloads|follows|newest|updated`；分页建议 offset 为 limit 的倍数，实际返回位置以上游响应为准。Modrinth 小型 JSON 请求包含正文的单次期限为 30 秒，最多四次尝试；用户取消立即传播，404 不做无意义重试，429 尊重秒数或 HTTP 日期形式的 Retry-After。

加载器安装和整合包运行时准备可添加 `--library-mirror MAVEN_ROOT_URL`，可重复指定。镜像只用于有官方 SHA1 的相同 Maven 坐标，仍校验下载内容；默认不启用第三方镜像。目录形式的资源包/数据包禁用时会移出游戏扫描目录，启用时再移回。

已安装的精确版本 Fabric／Quilt profile 可直接复用，避免重复准备依赖时被元数据网站故障阻断。损坏或不匹配的 profile 才重新获取；小型加载器元数据请求每次最多等待 30 秒并有限重试。

加载器列表成功后缓存，`--offline` 使用缓存；联网刷新失败时可回退，并返回 `source/cachePath/cachedAt/warning`。缓存缺失或损坏会说明原因，显式 `--loader-version` 仍能绕过目录请求走精确版本路径。官方元数据、已安装 profile 与经过哈希验证的 Maven 镜像分别标明，不把镜像可用说成官方网络恢复。HTTP 使用 .NET 的系统/环境代理设置；不要把代理凭据写入实例配置或提交到仓库。

CurseForge在线能力暂缓。`--platform curseforge`的search/project/versions/install/update默认无网络返回`PLATFORM_UNAVAILABLE`；更新已有CF来源内容同样明确不可用。程序不读取`RODEL_CURSEFORGE_API_KEY`，无需配置该变量。Core保留可注入`ICurseForgeProvider`；Modrinth在线功能与本地内容继续可用。

## 整合包与日常管理

```text
pack inspect ARCHIVE
pack import ARCHIVE [--name NAME] [--offline] [--content-only] [--manual-directory DIRECTORY]
pack prepare ID [ARCHIVE] [--manual-directory DIR] [--offline] [--content-only]
pack export ID DESTINATION [--format mrpack|curseforge] [--include-local-mods]
pack review ID ARCHIVE
pack check ID
pack apply OPERATION [--content-only]
pack update ID ARCHIVE [--content-only]
pack rollback OPERATION
pack history ID
pack detach ID
server list
server show FAVORITE_ID
server add NAME HOST:PORT [--id FAVORITE_ID]
server remove FAVORITE_ID
server bind FAVORITE_ID INSTANCE [--preferred]
server unbind FAVORITE_ID INSTANCE
server set-instances FAVORITE_ID [INSTANCE ...] [--preferred-instance INSTANCE]
server resolve HOST[:PORT] [--timeout MILLISECONDS]
server status HOST:PORT [--timeout MILLISECONDS]
server diagnose SESSION_ID
server connect INSTANCE HOST:PORT_OR_FAVORITE [--account ACCOUNT_ID] [--plan] [--background]
server connect FAVORITE_ID [--instance INSTANCE] [--account ACCOUNT_ID] [--plan] [--background]
server connect INSTANCE --server-address HOST:PORT [--account ACCOUNT_ID] [--plan] [--background]
launch INSTANCE --server-address HOST:PORT [--account ACCOUNT_ID] [--plan]
cache status
cache prune [--older-than-days 30] [--plan]
version
self check-update
```

整合包缺件会逐项列出；`complete=false` 不算完整安装。默认本地及在线导入自动处理文件、已知游戏/加载器与Java准备，缺件则暂停并保留实例；`--content-only`或`--offline`保持明确处理范围，不自动联网准备后续环境。`preparation`保留每步完成/失败/取消与提示，不证明已经进入游戏。更新保留玩家存档、本地额外模组、配置修改，冲突的上游内容保存在返回的报告目录；本地/在线更新与冻结应用共用运行环境/Java失败及取消后的回滚策略，恢复数据包含operationId、原始错误及回滚结果。`consistency.filesAvailable`只表示文件齐全，`matchesRelease`及`differences`另行表达受管文件与发布清单是否一致；回滚后不沿用目标发布的一致性结论。导出是否嵌入无平台引用的本地内容由`--include-local-mods`明确选择。

`pack inspect` 会列出 mrpack 可选文件；导入/预览更新/更新可用 `--optional-policy include-all|exclude-all`，以及可重复的 `--include-optional PATH`、`--exclude-optional PATH`。路径使用 manifest 中的相对路径，例如 `mods/example.jar`。未指定时首次导入沿用全部可选文件的旧行为，后续更新保留已有选择；只改一个路径会保留其他选择，显式指定总策略则重置旧选择，再应用本次逐路径选项。必需文件不被当作可选文件跳过，server-only 文件不装到客户端。

一次继续准备使用`pack prepare ID [ARCHIVE] [--manual-directory DIR]`：对同一保留实例补件后自动处理游戏/加载器与Java；缺件、失败或取消保留恢复数据，不能重新import代替继续。该命令及`--background`共用`PreparePackWorkflowTask`。只想补文件时使用`pack complete ID [ARCHIVE] [--offline] [--manual-directory DIR]`，它不会准备运行环境，并保留已完成文件、可选选择和玩家修改过的overrides。更新前用`pack review ID ARCHIVE`，确认后按operation执行`pack apply`或`pack rollback`。

HMCL消费以v3.16.4官方导出形状为已核实范围：`server-manifest.json`保存`fileApi`、文件SHA1及addon声明，未知字段保留；不承诺旧full/normal模式。`pack check ID`读取保存的分发源并暂存精确文件审核，确认后`pack apply OPERATION`复用该清单和暂存内容，不再查询最新发布；半发布/缺件不能作为完整更新应用。玩家修改、额外内容和禁用模组保留，删除的受管文件可补回，差异仍须看`consistency`。`pack history ID`显示当前及可回退事务，不能越过后来事务回退旧操作。`pack detach ID`停止来源检查但不删除文件、来源证据或服务器归属；明确提供归档再次review/apply可接回来源，包括同版重接。原生连接会协调同一检查/审核能力；CLI/MCP按这些显式命令组合，不静默应用新发布或伪造交互确认。

CF ZIP 离线格式继续支持 manifest 的 projectID/fileID 与来源元数据、overrides、`--manual-directory` 手工补件和 CF 格式导出。默认 provider 不联网补取 CF 文件；没有本地补件的引用返回完整待补清单。格式接受与本地补件不代表平台身份已经核验。缺件、手工补件、实际字节与导出引用已通过当前原生 CLI 验证。

服务器状态采用 Java SLP，不登录、不加入服务器。收藏保存在启动器数据根，不改游戏 `servers.dat`。未显式指定端口的域名通过 Windows DNS 查询 Minecraft SRV，按优先级与权重尝试目标；没有记录或查询失败仍使用原域名。显式端口、IPv4/IPv6 保留原意；SRV 单独声明的点目标表示服务不可用。连接游戏仍传递玩家原始地址，由游戏处理实际加入流程。状态查询失败不阻止玩家尝试连接。

服务器专用客户端与普通关联分开：`server bind SERVER INSTANCE --dedicated [--preferred]`明确转移现有客户端的管理归属，不移动或复制文件；不加`--dedicated`仍只保存关联，普通本地启动不变。`server release-client INSTANCE`转回本地管理，`server remove SERVER`同样保留游戏数据并释放所属客户端。`pack import ARCHIVE --server-owner SERVER`及`instance import NAME ... --server-owner SERVER`可直接建立专用客户端。此类客户端的`launch INSTANCE`默认连接所属服务器，`launch INSTANCE --no-server`为显式普通启动；`--offline`只控制文件准备网络权限，不等于不连接服务器。明确的`--server-address`和`--world`仍表达本次目标。MCP的launch输入`noServer`映射同一CLI选项，其他归属命令可经execute调用。

`server diagnose`读取本次服务器会话的有界客户端日志证据，与进程/SLP分开。仅在具名客户端连接事件匹配目标后归属后续受支持事件；没有证据、截断或不支持的格式保留未知，不能从“Connecting”推定仍在连接或已入服，`joined=null`。当前26.3真实受控断开已验证，其他版本/模组/语言不保证自动识别全部原因；原生分类及恢复UI仍在实施。通用会话诊断也保留此连接证据。

收藏可以关联多个既有实例；同一实例也可关联多个收藏。`bind --preferred` 保存首选，`set-instances` 一次提交完整关联集合及首选，不带实例则清空关联；解绑和删除收藏都不删除实例、模组或存档。省略显式实例时，只采用首选或唯一关联，有歧义或关联已失效时要求重新选择，不擅自重装或选另一个实例。关联不声明内容已符合服主清单，也不修改已有整合包来源或版本。

服务器后台连接使用同一 typed `LaunchTask`，保存确定的地址与显式账户 ID；`job cancel` 后仍需读取终态，失败或取消后显式发起新尝试，不盲目重放已可能启动游戏的任务。计划与会话新增 `server` 和 `serverConnectionMode`，表示请求目标及参数方式，不是入服结果。`server connect INSTANCE --server-address`、`launch --server-address` / MCP `serverAddress` 明确将值视为地址，即使收藏文件损坏也不依赖它；旧 `--server` / MCP `server` 仍兼容收藏 ID。具名 MCP `launch` / `launch_plan` 现支持 `accountId`，不再忽略显式身份。

下载中断会保留当前文件的临时数据。存在上游哈希或有效资源验证器时，后续操作尝试 HTTP Range 续传；服务器返回完整文件、不支持续传或数据不一致时自动重下。完成后校验并发布正式文件，不把临时文件当安装成功。普通 CLI、后台作业和 MCP 共用此行为，无需额外开启。

缓存清理覆盖过期元数据、内容下载、加载器安装包/目录缓存，以及受管理实例中没有任何已安装版本引用的 libraries/assets。下载发布记录文件归属与摘要，清理前核对未被用户改动；缺少归属的历史库/资产、外部根目录、未知文件、游戏版本、Java runtime、存档与模组原件保留。所有本地版本参与引用检查，包括当前未选中的版本、继承、旧版 assets 和 native 路径回退。正在使用缓存、后台作业状态未知或游戏运行中，会保留受影响资源；只读计划仍可查看。单个占用或删除失败返回 warning，其他可清理文件继续处理。状态包含可回收空间、已登记安装文件大小和保留原因；默认 30 天，可用 `--older-than-days` 调整。

诊断可用 `diagnose --report PATH` 直接分析 Minecraft/JVM 崩溃报告。`diagnose --session SESSION` 还会检查会话时间范围内的 crash-reports/hs_err 文件，排除旧报告，并明确这只是候选关联。返回游戏/Java 版本、类文件所需 Java、异常证据和 Fabric/Forge 的依赖名称、请求者、版本要求；未知内容照常保留，不据关键词自动移除模组。单份报告最多读取 1 MiB，并标明截断。

会话诊断的 `reportedErrors` 保留可用日志窗口内的原始ERROR/FATAL。首页使用的 `problem.reportedError` 仅在游戏非零退出后提升进程日志；运行中或退出码0时不因日志报错显示游戏故障。已识别的目标连接失败独立保留，不要求游戏进程崩溃；异常退出时展示的日志也不等同于已证实的崩溃原因。

私有仓库更新查询可用`RODEL_GITHUB_TOKEN`，没有可访问release时返回`release_unavailable`，不假报已经是最新版。网络错误附带`networkReason/httpStatus`，TLS握手失败与HTTP错误可区分。

MCP 启动方式为 `mcp serve --stdio`。工具覆盖能力发现、App宿主、实例、设置、内容搜索／计划／安装、材质与光影选择、作业、启动、进程、日志和大结果续查。具名工具将结构化参数转换为CLI token，与`execute`进入同一命令执行入口，再由CLI调用Core，不运行任意shell，也不为每次调用启动子进程。协议输出独占stdout。`tools list` 与 `tools call NAME --arguments JSON` 可直接从CLI检查同一契约。

MCP 结果统一为 `{schemaVersion, operation, status, data, warnings, error}`，文本与 `structuredContent` 相同，业务结果位于 `.data`。业务失败返回 `isError=true`，保留与 CLI 相同的错误码和恢复信息。MCP协议版本由服务端协商；取消一个调用的范围与独立后台任务分开。取消一个工具调用不会默认终止已独立运行的游戏。

本地与在线AI均通过MCP消费启动器能力，统一provider接入由另一worktree负责，见[AI使用](AI.md)。CLI不包含独立模型宿主或Agent循环，也不要求准备推理SDK。

工具所需的能力发现、上下文摘要和结果续查也可直接使用CLI：`capabilities [--tool NAME] [--topic TOPIC]`、`context [--instance ID] [--limit N]`、`instance content-context ID`、`result read ID [--pointer JSON_POINTER] [--offset N] [--limit N] [--characters N] [--property-offset N]`。图片引用使用`result image ID --pointer JSON_POINTER`显式读取，MCP对应`result_image_read`并返回独立图片content block，JSON正文只有类型/字节数。普通`result read`包括旧快照都不内联图片base64。列表的`fullResult`是可选详情，不要求为列出名称而读取全文。`capabilities`不带参数时只列工具/CLI主题，schema与完整帮助分别使用`--tool`和`--topic`。结果ID来自此前的工具结果，结果目录为数据根下的`ai/tool-results`；该目录不提供旧任务执行或格式迁移能力。

## 在线整合包与高级配置

Core/CLI在线整合包支持Modrinth，原生维护可选择实际发布版本和通道，再确认冻结应用。CLI未指定通道时保留按发布日期选择最新所有类型的行为；`--channel release`只选正式版，`beta`包含正式及测试版，`alpha`不收窄类型。`--pack-version`始终是明确的具体版本，不因建议通道变成硬拒绝，超出时在`selectionWarnings`说明。各条自动通道的离线latest缓存独立。原生选择器默认正式通道，并向typed审核传具体版本ID；应用不重新查latest。`--plan`只展示选择，不下载或保存选择缓存。

```text
content search QUERY --type modpack
content versions PROJECT --minecraft 1.21.1 --loader fabric
pack install PROJECT --pack-version VERSION_ID --plan
pack install PROJECT --pack-version VERSION_ID [--name NAME] [--content-only] [--background]
pack install PROJECT --channel release --plan
pack install PROJECT --pack-version VERSION_ID --offline
pack review-online INSTANCE [--project PROJECT] [--pack-version VERSION_ID] [--channel release|beta|alpha]
pack apply OPERATION
pack update-online INSTANCE [--project PROJECT] [--pack-version VERSION_ID] [--channel release|beta|alpha] [--plan] [--background]
```

在线安装/更新同样接受可选文件选择和 `--manual-directory`。省略 project 使用已安装包的线上来源；来源和版本随包更新快照回滚。`--content-only` 跳过运行库准备，之后可用 `pack prepare INSTANCE`。下载损坏可明确重试；安装后的运行库失败会返回保留实例及续接命令。后台包安装取消后先看 job 的终态，不能把可能已创建实例的安装一律当作可安全重放；`pack prepare`/`instance prepare` 可以续跑。

启动设置可持久化在实例，也可本次覆盖。`gameArguments` 是 JSON 字符串数组，`environment` 是名字到值/null 的 JSON 对象；MCP 的 launch/launch_plan 同样支持这些参数。

未指定窗口宽高时使用1280×720；已保存的实例宽高及单次启动覆盖保留，查看启动计划不会改写它们。

```text
config set width 1024 --instance INSTANCE
config set height 576 --instance INSTANCE
config set fullscreen false --instance INSTANCE
launch INSTANCE --width 1280 --height 720 --fullscreen=false
launch INSTANCE --world WORLD_NAME
launch INSTANCE --game-arg=--demo --env EXAMPLE_VAR=value
launch INSTANCE --env EXAMPLE_VAR
```

`--env NAME` 或设置中的 null 移除这个变量。单人快速进入用于版本声明支持的 quickPlaySingleplayer；旧版会提示并普通启动。服务器连接优先于单人快速进入；用户自定义参数仍可覆盖游戏自身行为。

```text
instance rename INSTANCE NEW_NAME
instance relink INSTANCE GAME_DIRECTORY [--minecraft-directory SHARED]
instance inspect-import VERSION_DIRECTORY [--launcher-config PATH] [--profile ID]
instance import NAME --directory VERSION_DIRECTORY --migrate [--copy] [--launcher-config PATH] [--profile ID]
instance prepare INSTANCE [--background]
```

PCL/HMCL 传版本目录；`--launcher-config` 分别可给 PCL 全局 Setup.ini、HMCL 的 hmcl.json。HMCL 优先匹配源共享根，也支持明确 profile。Prism 传 instance.cfg/mmc-pack.json 所在目录。未知配置保留在来源信息，预览会解释未转换部分。`instance prepare` 处理已识别的 Prism MC/加载器组件；`--copy` 冻结有效设置并保留来源。重新关联仅改元数据，关联后的目录不授予删除所有权；要移动文件仍用 move。

全局 `config set network JSON` 控制网络，省略 proxy 使用系统设置，`direct` 绕过代理，或填显式代理 URL。支持字段：`source`、`proxy`、`mirrors`、`concurrency`、`connectTimeoutSeconds`、`requestTimeoutSeconds`、`readIdleSeconds`、`metadataTimeoutSeconds`。`source`默认为`official`，选择`bmclapi`启用[BMCLAPI](https://bmclapidoc.bangbang93.com/)公开镜像；未知值按官方源处理且保留原配置。App在“设置 → 下载与网络”首项提供统一选择并自动保存，适用于下一次新建下载服务调用。覆盖游戏版本/客户端/服务端、资源及依赖库、加载器元数据/安装器/依赖、Mojang Java和authlib-injector公开组件，具体范围见[网络契约](CORE_API.md#网络配置与请求生命周期)。Modrinth、账号认证和软件更新继续使用各自来源。镜像失败或缺失时回退原源，选用BMCLAPI不改变文件校验。

例如`config set network '{"source":"bmclapi"}'`，切回官方用`config set network '{"source":"official"}'`。CLI此命令替换整个network对象；已有代理、并发或自定义规则时，应将它们保留在传入JSON中。App选择器通过Core的对象分区更新，仅修改source。

`mirrors`是原始 HTTP(S) 基址到镜像基址的对象，源基址必须以 `/` 结尾；仍按最长前缀匹配，手工规则覆盖相同前缀的预置规则。期限为秒（1～3600），并发为 1～64；非法数值回默认值或限入这个范围。默认连接 20 秒、正文空闲 120 秒、元数据 30 秒；持续有进度的文件正文没有总时长限制。原版客户端、运行库/native、日志配置与后续assets按阶段使用同一有界并发设置，未配置时为8；元数据依赖仍按解析顺序获取。Java等已有并行流程继续使用该设置，包内容仍逐项处理。配置变化对新服务调用生效，已开始的传输继续使用其客户端；`--offline` 仍按各命令含义使用缓存/本地文件。

```text
config get network
logs bundle SESSION --plan
logs bundle SESSION DESTINATION.zip
logs bundle SESSION DESTINATION.zip --redact=false
content analyze INSTANCE
content detach INSTANCE FILE.jar [--kind KIND] [--world WORLD]
```

资料包包含清单、结构化诊断、会话、保留日志和关联的近期报告；默认移除启动参数/环境并处理已知凭据文本，不能保证任意私有信息都匿名化。原始日志导出入口保持可用。本地内容分析给建议，不阻止启动；版本范围和所有内嵌机制尚未完整求值。来源解绑不移动或删除内容文件。

## Java、内存与日志兼容

```text
java list
launch INSTANCE --plan
config set memoryMode auto --instance INSTANCE
config set autoMemoryMaxMb 4096 --instance INSTANCE
launch INSTANCE --memory-mode auto
launch INSTANCE --memory 4096 --java JAVA_PATH
launch INSTANCE --log-encoding gb18030
launch INSTANCE --logging-compatibility=false
```

计划包含 `javaSelection`、`memorySelection`、`stdoutEncoding`、`stderrEncoding`、`fileEncoding` 与 `loggingCompatibility`；实际会话也保存选择依据。Java 默认按元数据/已知旧版偏好和更新号选择，自动发现会从 `java.home` 定位真实 JDK，显式路径仍按用户选择执行。旧 Forge/JDK 更新问题给建议，不阻止启动。

Java发现按已知安装布局、Windows注册表、环境与托管登记收集候选，不递归扫描整个厂商目录；非标准位置用显式路径或`java import`登记。探测最多四路并发，无整次查询时间预算；完整探测结果跨进程缓存，每次根据可执行文件、release、关键运行库和环境变化重新校验。Java更新、删除和链接重定向在下次查询/启动规划时处理，不持续后台监视。`java list`的`warnings`及选择报告保留属性回退、缓存不可读/不可写等警告；缓存只是可丢弃的观察，不代替真实游戏运行验证。

CLI探测记录仍位于数据根的`cache/java-probes`，每个规范化可执行路径固定一份，同路径更新覆盖，不按查询次数新增文件。现有缓存清理入口按写入年龄识别并清理正常命名的记录（默认30天），保留未知文件，不删除Java本体；不增加自动跟踪或配额。App未指定数据目录时使用包私有缓存，与CLI缓存位置可以不同；显式数据目录的相对布局不变。

内存默认仍是手工模式。自动模式查询 Windows 当前可用物理内存，默认保留 1024 MiB、建议区间 512～16384 MiB，低可用内存有小值回退；可设置 `memoryReserveMb`、`autoMemoryMinMb`、`autoMemoryMaxMb`。`--memory` 优先于自动模式；没有它时，自定义 JVM `-Xmx` 优先。不会关闭本地模型来腾内存。

默认只统一 stdout/stderr 到 UTF-8，不改老模组使用的 `file.encoding`；已有输出编码参数保持有效，`stdoutEncoding` / `stderrEncoding` 可分别设置。控制台按字节保留有界内存窗口，仅在异常退出或显式导出时保存。已知旧 Log4j 的 JndiLookup 移除在独立副本完成，保留共享源文件、JAR 名字和自定义日志配置；失败回到原库并记录警告。它不是所有未知日志库的安全保证。

## 游戏设置与资源包实际选择

```text
game-settings get INSTANCE [--encoding gb18030]
game-settings set INSTANCE '{"lang":"zh_cn","soundCategory_music":"0.25"}' [--plan]
game-settings set INSTANCE '{"customKey":null}'
game-settings apply INSTANCE [--overwrite]
game-settings discard-pending INSTANCE
resourcepack list INSTANCE
resourcepack select INSTANCE second.zip first.zip [--plan]
resourcepack select INSTANCE first.zip --replace --allow-incompatible
shaderpack list INSTANCE [--engine iris]
shaderpack select INSTANCE --file pack.zip [--engine iris] [--plan]
shaderpack select INSTANCE
```

这里编辑游戏的 `options.txt`；`config` 编辑启动器设置。`game-settings set` 使用游戏原始键和值，保留未修改行、未知字段、BOM、编码及换行，`null` 删除该键。JSON 命令示例适合 PowerShell 7；MCP 则直接传 JSON 对象。

资源包列表把 `installed` 与有顺序的 `selected` 分开。选择时按已知旧/新游戏格式处理文件名称，默认保留 builtin 和未托管选择；`--replace` 才替换完整列表，`--allow-incompatible` 才加入明确接受的兼容标记。缺少的显式条目保留并提示，不强制联网或阻止游戏。

操作返回 `state: planned/applied/deferred/conflict`。游戏运行时延期，退出后 `game-settings apply` 应用；游戏后来修改了同一个键则返回冲突，`--overwrite` 明确选择以请求值为准。修改不相关键不会覆盖游戏自己的编辑。`--expected-fingerprint HASH` 是可选的旧文件检测；普通直接写入不要求先预览。

这些操作不热重载正在运行的资源包，也不代替存档数据包激活；按键等原始值不自动跨版本转换。用户可以传本版本实际接受的键/值，未知模组设置保留。

`shaderpack` 从真实模组元数据和配置发现 Iris / Oculus / OptiFine；归属有歧义时用 `--engine iris|oculus|optifine`。`select --file` 选择一个已安装包，省略 `--file` 停用光影，`--plan` 仅预览。使用 `--expected-fingerprint HASH` 防止覆盖读取后的外部编辑；成功替换返回 `recoveryPath`，保留无关 Java Properties 字节与设置。实例运行中拒绝修改；关闭游戏后再选择，下次启动由游戏加载。MCP `shaderpack_list` / `shaderpack_select` 使用相同命令入口。

损坏的 pending 单独返回 `pendingWarning`，正常 options 仍可读。`discard-pending` 把待办移到保留文件并返回 `recoveryPath`，不修改真实游戏设置。资源包禁用/移除会协调已知选择，更新换名保持选择位置和明确接受的兼容标记；未知 builtin 保留。文件已经提交但选择调整失败时返回 `resourcePackSelection.state: pending-recovery`，可用 `content recover INSTANCE` 重试。运行中调整排入待办，退出后仍使用 `game-settings apply`；enable 只恢复文件，不自动替玩家选中它。显式选择禁用文件仍允许，但会提示先 enable。

## 运行版本与加载器的更换／回退

```text
runtime show INSTANCE
runtime operations INSTANCE
runtime operation INSTANCE OPERATION_ID
runtime change INSTANCE --minecraft 1.21.1 --loader fabric --loader-version 0.19.3 --plan
runtime change INSTANCE --minecraft 1.21.1 --loader fabric --loader-version 0.19.3
runtime change INSTANCE --version MY_LOCAL_PROFILE --offline
runtime remove-loader INSTANCE --offline
runtime rollback INSTANCE OPERATION_ID
```

先完成文件准备，再一次提交实例设置。成功返回 `operationId`；回退恢复本次拥有的运行字段，保留后来修改的玩家名、内存等设置。后续又改变了运行组合时，旧操作不能覆盖新选择。共享库、模组、游戏设置和世界不随移除加载器被删除，存档升级也不会被假装自动逆转；需要存档副本时可使用已有 `backup create --scope worlds`。

同一个已完成回退可重复调用；无关设置变化不会被误判为运行冲突。`runtime operations` 可找回丢失的 operationId，`runtime operation` 同时返回记录状态与 `observedState`（当前是 previous-selected/target-selected/selection-changed）。prepared 不证明设置已经提交，先读观察再决定回退。损坏记录逐项显示 unreadable。`pack prepare` 按包声明的依赖重新选组合，`instance prepare` 按迁移组件准备；两者也一次提交设置、返回回退快照，vanilla 明确写 loader=null。

已有内容更新默认按实例当前 MC/loader 查找，返回 `updateContext` 并保留原来源信息。`content update INSTANCE FILE --plan --update-context source` 明确按历史来源查询；`--minecraft/--loader` 可覆盖当前过滤。无法解析运行元数据时解释历史过滤回退；无匹配保留本地文件，不限制启动。

`--offline` 复用已安装的精确 profile；不指定精确加载器版本时无法凭空作离线选择。游戏活动期间仅此次运行组合写操作需等退出，兼容警告不会变成游戏启动门槛。

## 结构化日志

每次启动还会生成独立的轻量诊断：`logs/SESSION.startup.json`。`process show SESSION`、`logs SESSION`、`logs events SESSION`、`logs export`和`logs bundle`的会话信息包含同一份`startup`，正常退出、内存控制台释放后仍可读取，七天后按已有日志规则清理。记录覆盖启动准备、加载器/Mixin、客户端、图形、可见窗口、资源/声音/纹理等实际观察到的标志；不支持或未输出某个标志的版本不会被拒绝，也不会被猜成卡死。

`elapsedMilliseconds`从本次监控准备开始计时，`sincePreviousMilliseconds`表示距离上一个观察标志的间隔，`source`区分启动器动作、stdout/stderr与系统窗口证据。`processCpuMilliseconds`为游戏全部线程累计CPU时间，可能大于墙钟时间；`workingSetBytes`是实际常驻内存。`longestObservedGap`指向最长已完成观察间隔，不能将该间隔全部归因于标志对应的组件。长时间没有后续标志时看快照的总耗时与最后一个标志；观察最多三分钟，超时标记`observation_timeout`，不会终止游戏。窗口、声音和纹理标志齐备也只记`initialization_observed`，不声称菜单可操作或世界/服务器已就绪。

这些日志仅保存固定阶段和计量，不保存原始聊天、令牌或整段游戏控制台。它能定位等待发生在哪一段，不能凭里程碑确定某个模组的因果责任。需要继续定位Java内部热点时，可在隔离副本显式加入本机Java支持的JFR启动参数，结合线程/文件/网络采样调查；普通启动不自动启用录制或要求玩家安装JDK。

```text
logs events SESSION --stdout-offset 0 --stderr-offset 0 --bytes 65536
logs events SESSION --stdout-offset NEXT --stderr-offset NEXT --state RETURNED_PARSER_STATE --level WARN
```

XML 与普通文本解析为事件，含级别、线程、logger、原始时间和正文/异常；XML 时间戳按毫秒解析。文本没有完整时区/日期时保留 `rawTimestamp`。把返回的 `parserState` 和各流 `nextOffset` 一起传入后续调用，也可用 `--state-file FILE`。

未完整的 XML/文本异常保存在有界 parserState 中，终结或出现下一事件时发出；坏 XML、未知格式和超长片段保留原文。事件范围标为 `containing-read-batch`，是包含该事件的读取范围，不冒充逐事件精确字节坐标。两个输出流分别保持原顺序，不推定跨流因果时间线。完整原始依据仍可用 `logs export` 取得。

AI用户问答：模型调用内置`ask_user`后，`ai chat turn show CONVERSATION TURN`的`pendingUserPrompt`提供问题ID和单/多选选项；`ai chat respond --file response.json`读取强类型`AiUserResponse`并向原后台回合提交。示例：

```json
{"conversationId":"CONVERSATION","turnId":"TURN","promptId":"PROMPT","optionIds":["simple"],"text":"保持现有颜色","skip":false}
```

自由输入可省略`optionIds`；明确跳过为`skip:true`、无选项及文字。提问不等于操作审核，回答不授予工具权限。

## 实时游戏数据桥

游戏桥默认开启，全局或实例明确保存的`gameBridgeEnabled=false`仍优先。启动时`--game-bridge true`或`--game-bridge false`可临时覆盖。Sprout全局“游戏启动”提供默认开关，实例设置可沿用全局/关闭/开启。恢复继承用`config patch`的removeKeys删除实例覆盖，不写false/null；普通启动与无桥游戏仍可用，已启动的游戏不能热注入。

```powershell
rodel-mc launch INSTANCE --game-bridge true --offline --world WORLD --player NAME
rodel-mc process bridge show SESSION
rodel-mc process game snapshot SESSION
rodel-mc process game region SESSION --request REQUEST_JSON
rodel-mc process game query SESSION --request REQUEST_JSON
rodel-mc process game ai SESSION --request REQUEST_JSON
rodel-mc process game events SESSION --interval 500 --events
rodel-mc process game edit SESSION --request REQUEST_JSON
rodel-mc process game operation show SESSION OPERATION
rodel-mc process game operation cancel SESSION OPERATION
```

region/query/edit请求使用Core源生成模型；从最新快照取得incarnation、worldEpoch和dimension，操作ID为新的32位小写GUID。同一ID只可重用相同请求；编辑成功封套可能仍为queued，需查收据。断线未知效果不可自动重发。服务器读取仅客户端已同步数据，任何服务器写入始终拒绝，包括同机独立服务端和OP4。

`process game ai` 使用 `GameAiRequest`：`action` 为 `open/send/quick/read/new/cancel/confirm/answer`，`worldKey` 为稳定存档身份，`snapshot` 为最新完整快照。发送/新会话必须带唯一 `requestId`；单次问答后用同一 `quickId` 观察；许可和问答沿用准确 `turnId/invocationId/fingerprint`，只允许本次批准，不扩大授权。`read` 的 `after` 是事件游标，`offset` 从最近回合向前分页。该命令与游戏模组共用 Core 服务，不新增会让 AI 递归调用自己的具名 MCP 工具。

具名MCP工具与命令共用符号：`game_bridge_show/game_snapshot/game_region/game_query/game_edit/game_operation_show/game_operation_cancel`；`launch_start`也接受`gameBridge`。AI回合另需先观察并冻结世界，普通操作许可不授予远程服务器写权。范围、JSON示例和真实验收见[游戏桥](JAVA-GAME-BRIDGE.md)。

查询支持原生菜单、玩家/目标、块/实体SNBT、世界、注册表、配方标识及进度；本地写能力还包括有界原生输入、容器点击/按钮和原玩家权限命令。命令必须原生允许作弊，输入支持tick内延迟交互。applied表示相应原生调用/租约完成，具体游戏结果须新查询确认。

披风为独立作品。`skin create --model Cape --size 64 --name NAME` 创建 64×32 工程，`--size 128` 为 128×64。`--input FILE --model Cape` 显式导入披风，普通 64×32 人物皮肤沿用旧版 Classic 规则。绘制使用 `part=Cape, layer=Base`，复用图层、历史和 PNG 导入导出。

`skin outfit CAPE_ID --revision N --with SKIN_ID` 为指定披风保存人物预览关联，`--clear` 清除关联。未绑定工程的 skins MCP 通过 `skin_outfit` 的 `companionId/clear` 执行同一操作。使用 `skin preview ID --output FILE` 或 `skin_preview` 可渲染已保存搭配，`--solo` 或 `solo` 仅渲染披风本身。App 的披风编辑窗口另用全局预览偏好。`skin export` 输出当前作品纹理，`skin save-as` 保存当前工程副本。

## MCP 按需发现入口

外部客户端默认登记`--tool-profile discovery`，初始仅有`capabilities`、`tool_call`、`execute`、`result_read`、`result_image_read`五个入口。先用capabilities查看相关名称/单个toolName的schema，再以tool_call(name, arguments)调用同一Core契约；CLI专有操作才用execute及对应topic帮助。这个profile不动态依赖客户端刷新tools/list，也不把完整目录schema塞入连接上下文。显式`--tool-profile launcher`仍提供完整具名启动器工具，`skins`提供独立皮肤目录；外部客户端负责自己的授权。初始化说明与技能索引均要求普通问答无需加载操作状态。

```powershell
rodel-mc --data-dir C:\RodelData mcp serve --stdio --tool-profile discovery
```
