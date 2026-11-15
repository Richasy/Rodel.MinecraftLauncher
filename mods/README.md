# Rodel Minecraft 模组

与启动器同仓库管理的独立 Java 模组，不加入 .NET 解决方案。Rodel AI、World Reroll 和 BetterTools 由启动器内部持有，随正常 CLI／App 构建打包；玩家通过“内容 → 模组 → 内置模组”安装，无需 JDK、Gradle、源码目录或社区下载。所需 Fabric API 一同提供。

**Rodel UI 已撤出内置列表与正常打包。** 源码保留为独立实验模块，不自动卸载或禁用玩家已有安装。其它模组不得依赖它的类、元数据、反射接口、字体或主题；启动器不再传 UI 主题参数。

| 项目 | 职责 | 主题设置 |
| --- | --- | --- |
| [Rodel AI](rodel-ai/README.md) | `/rai` 和暂停菜单共用本次游戏进程内的连续会话，复用启动器 Core AI | 模组配置；侧栏设置按钮也可进入 |
| [World Reroll · 世界重开](world-reroll/README.zh-CN.md) | 保留设置换种子重开，新世界就绪后才删除精确来源 | 模组配置；重开选项页也可进入 |
| [BetterTools · 工具魔方](bettertools/README.md) | 存储并切换真实装备，保留组件、附魔与独立耐久 | 模组配置；`/bettertools config` 也可进入 |
| [Rodel UI](rodel-ui/README.md) | 菜单与工作站视觉实验 | 独立开发，不随启动器分发 |

三个内置模组使用自己的 `config/*-client.properties` 保存明暗主题，默认浅色，完成时保存，取消不改动。Fabric／Quilt 的模组配置入口由可选 Mod Menu 提供，Forge／NeoForge 使用原生模组列表。

版本统一由 `mods/gradle.properties` 维护，当前为 `0.0.2`。正式开源后每次模组发布变更递增版本，无大改时优先递增末位补丁号。精确分发组合唯一登记于 `bundled-targets.props`，实际验收见[兼容性矩阵](COMPATIBILITY.md)。不由一个模组通过推定其他模组或加载器兼容。

构建需要 JDK 25，Forge 1.20.1 另需 JDK 21～24；分别通过 `BundledModsJavaHome`／`BundledModsLegacyJavaHome` 指定。Core 调用 `scripts/build-bundled-mods.ps1` 从源码生成 `Assets/BuiltInMods/<Minecraft>/<loader>` 和 `catalog.json`。原生加载器打包只编译登记的模组，不隐式构建 UI。安装、更新同 ID 与恢复沿用 Core 内容审核及事务。

游戏实例、缓存、构建产物和机器路径不提交。各模组运行不得依赖设计目录或开发机器配置。
