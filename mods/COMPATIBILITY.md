# 内置模组兼容范围

当前内置模组为 Rodel AI、World Reroll 和 BetterTools，统一版本 `0.0.2`；无大改时递增末位补丁号。Rodel UI 已撤下，源码保留为独立实验模块；原有 UI 单独支持的组合不再构成启动器分发目标。其它模组没有编译、运行时或反射 UI 依赖，主题选择归各自配置。

| Minecraft | 加载器 | Java | 登记的随包模块 |
| --- | --- | --- | --- |
| 26.3 | Fabric | 25 | AI、World Reroll、BetterTools、Fabric API |
| 26.2、26.1.2 | Fabric | 25 | AI、World Reroll、BetterTools、匹配 Fabric API |
| 26.3 | Quilt 0.31.0-beta.4 | 25 | AI、World Reroll、BetterTools、Fabric API 兼容包 |
| 26.3 | Forge 66.1.0 | 25 | AI、World Reroll、BetterTools 原生包 |
| 26.3 | NeoForge 26.3.0.99 | 25 | AI、World Reroll、BetterTools 原生包 |
| 1.21.11 | Fabric 0.19.5 | 21 | BetterTools、匹配 Fabric API |
| 1.21.1 | Fabric 0.19.5 | 21 | AI、World Reroll、BetterTools、Fabric API 0.116.17+1.21.1 |
| 1.20.1 | Forge 47.3.33、47.4.10 | 17 | AI 原生包 |

除下述新验证的 1.21.1 外，其余登记继承既有业务兼容范围，不表示 `0.0.2` 的新主题设置和无 UI 组合均已实机复验。未列出的组合不推断为支持，不缩窄第三方模组或手工实例能力。

1.21.1 的三个 `0.0.2` 正常 JAR 已在独立 Java 21 客户端验证：原生多行编辑、AI 命令后保留聊天及 Esc；真实装备组件／耐久、耗尽回退、副手守恒、原物品栏浮层／滚轮／Tab／右键及原生合成配方；村庄准备／原生出生点，以及新世界就绪后删除精确来源、保留原生规则与额外数据包文件。测试入口独立于产品 JAR，见 [World Reroll 验证](world-reroll/docs/VALIDATION.md)。

同一组合另通过含 165 个原有顶层 JAR 的整合包隔离副本验证，进入新世界后实际玩家皮肤、窗口模式、装备／原物品栏及 AI 输入正常；不将这个整合包的通过推定为所有第三方组合兼容。

BetterTools 的十格、真实装备／组件／耐久、原生攻击与冷却、目标切换及保存重进保留既有业务验收；四个 Fabric 目标的耗尽回退与副手守恒已有实测。Quilt／Forge／NeoForge 的旧材质截图不作为本次独立主题验收。World Reroll 保留新世界就绪后精确删除来源、失败保留及单人／LAN 边界。Forge 1.20.1 AI 使用原生单行编辑器和滚动记录，复用 Core 连续会话及权限，不将其称为现代多行侧栏。

## 构建与选包

唯一分发输入为 `bundled-targets.props`。正常 Core 构建从源码生成 `Assets/BuiltInMods/<Minecraft>/<loader>` 与 `catalog.json`，随 CLI／App 发布。Fabric／Quilt 携带匹配 API；Forge／NeoForge 的 `loader-build` 仅打包登记模块、原生生命周期、TOML、Mixin 和访问转换声明。

Forge 共用源码使用上游 Mixin 0.8.7、Java 21 字节码，26.3 游戏仍运行 Java 25。Forge 1.20.1 使用 ForgeGradle 6／Gradle 8 编译 Java 17 字节码，开发机需 JDK 21～24。玩家安装不依赖这些构建工具。

目录按版本和加载器缓存元数据，审核和提交重新读取 JAR、依赖、哈希与实例运行环境。已有兼容且启用的共享依赖保留，同 ID 改名／禁用包替换和恢复沿用 Core 内容事务。撤下 UI 不触碰玩家已有文件或存档。

## 验证边界

构建、Core／CLI 安装、客户端真实运行、NativeAOT 和原生 App 验收分别登记，不能互相替代。测试只用隔离目录与新建世界。业务验证另见 [World Reroll 验证](world-reroll/docs/VALIDATION.md)。
