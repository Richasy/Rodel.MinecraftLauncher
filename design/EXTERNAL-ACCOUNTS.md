# 外置账户交互设计

，HTML已认可，Core / CLI / Sprout已接入；用户补充要求设备码不可用时直接使用账号密码，错误页标题保留返回。[直接预览](index.html?accounts=1&external=1&theme=light)。HTML模拟不作为真实认证证据。

## 账户与登录

添加类型保留Microsoft与本地离线，增加外置认证。LittleSkin及自建兼容站点是同一类型的不同来源。列表和头像菜单按Microsoft、外置、本地排序；外置条目显示玩家名及“来源 · 实际状态”两行，尾部使用地球类型图标。外置头像为演示角色图片，没有本地编辑入口；新增/重登均不自动切换当前启动身份。

来源选择中LittleSkin使用[官网图标](https://littleskin.cn/favicon.png)，保留原始颜色与透明背景，按28 DIP等比显示；其他认证服务使用通用地球图标。原图分别随原生应用`Assets/Icons/littleskin.png`与HTML的`assets/littleskin.png`打包，显示时不请求远端图标。

核对[ALI服务元数据规范](https://yushijinhun.github.io/authlib-injector/en/yggdrasil-server-technical-specification.html#metadata-in-meta)及[HMCL服务模型](https://github.com/HMCL-dev/HMCL/blob/a4db0532b9e461c2c6511af7b805d3c3a1922121/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/authlibinjector/AuthlibInjectorServer.java)：已约定站点名称、实现信息与主页/注册链接，没有统一服务图标字段。没有找到可用的统一官方Yggdrasil标志；“其他认证服务”尚未指定站点，保留通用图标，不将某个实现的Logo当作协议标志。

- LittleSkin设备授权可用时沿用设备码页：授权码各字符均匀铺满代码区，整块区域可点击/键盘复制；生成或重取代码时自动复制完整原码。成功显示勾及“已复制”，复制失败时如实提示并允许重试；过期/拒绝后禁用复制并清除反馈。主操作打开服务返回的授权页，网页确认后自动继续。HTML只复制演示代码及模拟打开网页。
- 设备流应用未配置或被拒绝时，原生直接切换为LittleSkin邮箱/角色名与密码表单，自动使用`https://littleskin.cn/api/yggdrasil/`，不让玩家填写认证地址，也不留下应用权限警告、确认按钮或无效重试。密码仍由用户主动输入和提交，不自动发送、不保存；完成、返回或关闭时清理。标题为“登录 LittleSkin”，左侧返回来源选择；现有账户重登返回账户管理并保持原服务/UUID约束。HTML默认LittleSkin入口模拟当前设备流未开通的密码路径，设备码各状态仍可由评审下拉预览。[官方密码登录说明](https://manual.littleskin.cn/yggdrasil/client#hmcl-3)。
- 其他服务在同一表单填写认证地址、邮箱或用户名、密码，一次“登录”自动读取服务元数据并验证账户，支持Enter提交；不设单独读取服务、确认来源的步骤。地址可粘贴网站或API入口，已有账户重登直接显示绑定来源，只填写登录信息。失败返回同一表单并保留草稿，完整原文通过提示条标题尾部的信息图标在Flyout中查看。HTTP按用户填写的协议处理，不增加特别提示、不自动改写为HTTPS。

来源选择中的“其他认证服务”每次打开均为空白地址、账号与密码，不继承LittleSkin的预置地址或前一个服务的草稿；返回来源选择时清理当前来源草稿。同一表单内失败后仍保留输入以便修正；已有账户重登继续使用其绑定服务，不重置来源。
- 单角色成功直接登记；多角色显式选择，未选时提交禁用，支持方向键/Home/End。无角色提供站点管理及重新读取，不伪造离线角色。重新认证绑定原服务及原角色，角色删除不悄悄换成其他角色。
- 取消结束当前请求、清理临时密码并保留已添加账户及当前选择。HTML不发送认证请求、不读玩家数据、不执行JAR；地址、模拟角色和输入只在内存中。实际取消/迟到响应、令牌保护、跨命令续期及原生接线仍须Core实施验证。

## 异常与恢复

| 情况 | 界面行为 |
| --- | --- |
| 明确账号/密码错误 | 表单内显示拒绝原因，保留输入供修正；不能把所有403或未知错误推断成密码错误 |
| 连接失败/超时 | 原位重试，保留原账户及凭据状态，不标“需要重新登录” |
| TLS/域名证书失败 | 显示安全连接原因，保留地址及原文，没有忽略证书按钮 |
| 非JSON/无效响应 | 说明认证接口内容异常，允许修正地址或重试 |
| 未识别的远端错误 | 保留实际errorMessage；标题尾部信息图标打开原文Flyout，可选择及复制，不堆猜测原因 |
| 请求过多 | 显示服务限流，不归为密码错误，不发明倒计时或无限自动重试 |
| 明确令牌失效 | 原账户标记需要重新登录，由原来源恢复，不删除身份 |
| OAuth拒绝/过期 | 分别表达拒绝与过期；重启授权生成新代码，不沿用已结束请求 |
| 设备流应用未获授权 | 自动显示LittleSkin密码表单，无需填写服务地址，返回可回来源选择 |
| 无角色/原角色已删除 | 管理/重读或显式另加角色，不静默更换UUID |
| 启动前刷新网络失败 | 主操作重试验证；仅有实际可用角色、Agent与服务元数据缓存时提供次要“使用缓存尝试”。仅沿用原UUID/会话，不转为本地离线、不改默认账户、不取消原服务器目标；不保证联机/皮肤成功。无缓存时没有该操作 |
| 登录组件下载失败 | 与账号错误分开；重试准备或选择本地JAR，之后仍须校验。HTML只读文件名，不读取或执行JAR |

LittleSkin的authorization_pending及slow_down保持等待，不进入失败页；实际轮询间隔、取消、过期与刷新按服务响应处理。页面不伪造剩余时间。HTML没有真实轮询，不用定时器冒充认证证据。

## 形制与评审

沿用现有440 DIP账户Popup、正文四边16 DIP、自然高度、固定头/footer与正文滚动。设备码页只保留一条操作说明与一条实际等待状态。错误使用主题语义浅底、图标和直接原因，每组最多一个accent。提示条四边内距12 DIP，图标与文字组间隔12 DIP，标题与正文间隔4 DIP，到后续说明或表单间隔16 DIP。标题尾部24 DIP信息图标打开轻量原文Flyout，原文可选择及复制，长文内部滚动；不保留正文展开按钮。外部点击或Esc只关闭Flyout、保留页面与草稿，Esc恢复信息图标焦点。普通表单及错误页均保留标题返回/关闭，只有真正等待时隐藏它们并提供独立取消。

登录/会话验证/角色读取/组件准备使用共用紧凑等待行：20 DIP不定加载环、14 DIP单条动作、12 DIP服务/角色或文件上下文。正文四边16 DIP为唯一外层内距，组件不另加上下留白，次级上下文紧邻动作。独立“取消”位于状态行右侧，与加载环及两行文字整体垂直居中；不复用关闭按钮，进行中隐藏顶部关闭/返回且不设置底部取消栏，初始焦点落在取消，Esc同样取消。LittleSkin设备码页打开网页/重试连接在左、取消在右，等宽并排，状态行只显示状态，标题左侧返回登录来源选择，没有重复关闭。所有并排操作均主操作在左、取消或次要操作在右，DOM与键盘顺序一致。结束后的页面恢复关闭。底部只有一个重取代码、登录、添加角色或重试按钮时，采用无分割线的通栏操作，紧接正文。

弹窗底部的“预览流程状态”按入口、LittleSkin、其他认证服务及启动/组件分组。选中后立即构造该状态并保留选中项，移除“返回结果”，从异常页也可连续切换；预览不依赖前序登录阶段。填写表单等候选仅使用虚构草稿，真实流程的按钮仍各自执行其职责。主题按钮仅用于原型评审。全部角色、授权码、端点结果均为虚构，不要输入真实凭据。

## 固定来源与设计取舍

核对HMCL提交[`0d20910ea302a907d6a01fec9244ab10d2993d5e`](https://github.com/HMCL-dev/HMCL/tree/0d20910ea302a907d6a01fec9244ab10d2993d5e)。源码参考不构成HMCL或本项目的运行验证。

- [`AddAuthlibInjectorServerPane.java` 181–229行](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCL/src/main/java/org/jackhuang/hmcl/ui/account/AddAuthlibInjectorServerPane.java#L181)：TLS/证书/连接分类，先读取服务元数据再展示名称及地址；HTTP有实际警告。Rodel不增加HTTP提示，将发现与登录合为一次玩家操作；后台仍须先核对元数据及目标，再向解析后的服务认证，不照搬HMCL的分步界面。
- [`Accounts.java` 507行起](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCL/src/main/java/org/jackhuang/hmcl/setting/Accounts.java#L507)：无角色、连接、响应、明确密码/令牌错误、角色删除和组件故障分开；未识别的远端消息保留。设计参考其责任划分，没有将字符串匹配复制成Rodel已实施逻辑。
- [`CreateAccountPane.java` 225–245及589行起](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCL/src/main/java/org/jackhuang/hmcl/ui/account/CreateAccountPane.java#L225)：创建失败恢复表单，取消选择独立处理，候选来自CharacterSelector。
- [`LauncherHelper.java` 777–815行](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCL/src/main/java/org/jackhuang/hmcl/game/LauncherHelper.java#L777)：凭据过期重登；其他认证失败提供重试、跳过刷新和取消。
- [`AuthlibInjectorAccount.java` 72–119行](https://github.com/HMCL-dev/HMCL/blob/0d20910ea302a907d6a01fec9244ab10d2993d5e/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/authlibinjector/AuthlibInjectorAccount.java#L72)：缓存继续要求已有Agent和服务元数据，组件下载与元数据读取故障分别传播。Rodel的缓存说明是基于这些边界的设计建议，HTML不证明真实缓存可启动。
- LittleSkin官方[设备代码流](https://manual.littleskin.cn/advanced/oauth2/device-authorization-grant)、[授权代码流及PKCE限制](https://manual.littleskin.cn/advanced/oauth2/authorization-code-grant)、[OAuth访问Yggdrasil](https://manual.littleskin.cn/feature/oauth-for-yggdrasil)：网页/设备码来自同一流程，应用白名单和测试模式为真实接入前提。

实际接入仍按CLI / Sprout → Core；Core拥有身份、凭据、登录组件准备及typed API。本轮不新增产品服务、独立EXE或真实OAuth应用。
