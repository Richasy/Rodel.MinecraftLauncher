# Artifact Blueprint：字体兼容

的第三方字体例外。Rodel UI 检查 `artifact_blueprint` 已安装后，只为 `compat.ArtifactBlueprintFonts` 中列出的七个确切建筑蓝图页面增强默认/均匀字体。双方无编译依赖，不使用扩展绘图 API，不接管建筑蓝图页面布局、输入和业务。

常规文本映射到 `rodel_game_ui:regular`，原生 bold 样式在 Font 测量和 PreparedTextBuilder 构建两条路径统一解析成 `rodel_game_ui:chinese` 的真实粗体，关闭额外伪粗体位移。显式指定的其它字体不覆盖。系统字体读取、缺字回退、8倍字形采样与灰度图集的 LINEAR sampler 复用 Rodel 既有实现；不附带微软字体文件。

这是建筑蓝图的明确兼容范围，不是全局字体替换或面向所有模组的自动接管机制。页面清单通过字符串判断，不加载建筑蓝图类；未安装它、离开这些页面或进入无关页面时不生效。`UiContexts.modern` 的布局/控件白名单没有扩大，原生 guiScale 和 EditBox 的输入/预编辑区域不作额外变换。

以双方正式 JAR 在独立 26.3/Fabric 客户端核对 GUI 1/2/3/4/自动、七页 glyph 来源、常规与真实粗体的测宽/换行、原生中文预编辑与提交/光标/搜索回调、显式字体保留、离开页面恢复原字体及资源重载；无 Rodel 的建筑蓝图独立实例执行同一回归。实际脚本、截图与报告仅留本地，不进入正常构建。

输入测试通过 Minecraft KeyboardHandler 的 SDL 文本入口驱动，不冒充操作系统候选词操作。Windows输入法候选窗选词与定位尚未验收。非 Windows、其它字体资源包及全部第三方组合未逐一验证。
