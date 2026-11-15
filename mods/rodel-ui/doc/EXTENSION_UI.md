# Rodel UI 复用边界

Rodel UI 保留为独立实验模块，不在启动器内置目录或正常打包中。其它第一方模组不得通过编译依赖、元数据、反射、字体或主题接口与其耦合。

Rodel AI、World Reroll 和 BetterTools 使用 Minecraft 原生字体、控件与自己的面板／主题设置，明暗选择由各自模组配置持久化。Rodel UI 源码中供自身实现使用的绘制类型不是跨模组集成承诺，不再推进基于它的 Artifact 业务页迁移或第一方 UI 复用设计。

第三方配置仍原样打开，既有确切页面兼容边界见模块 README 与 [Artifact 字体范围](ARTIFACT_BLUEPRINT_FONTS.md)。
