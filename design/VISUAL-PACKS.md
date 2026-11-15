# 材质包与光影的实际使用

## 玩家操作

安装只使内容可用。内容页的材质包和光影开关读取游戏真实配置，显示“使用中 / 未使用”；不再把文件存在或没有 `.disabled` 当成正在使用。选择失败时显示实际错误，保留列表，不猜测成功。

材质包可以同时使用多个。打开开关保留已有包及其顺序，并将新选择放到最高优先级；重复选择不改变顺序。已使用条目的菜单提供提高 / 降低优先级。关闭开关仅移出选择，不删除或禁用文件。保留游戏内置包和未知来源的选择，继续复用 Core 的 options.txt 指纹、兼容性检查和待应用语义。

光影一次选择一个，关闭当前项即停用光影。Core 从真实模组元数据及已存在配置发现 Iris、Oculus 或 OptiFine；只有归属有歧义时才显示引擎选择。没有可识别引擎时说明原因，不安装猜测的依赖。CLI 可明确指定引擎以支持手工配置。配置读失败时不伪装为未使用。

运行中的实例不能改这些配置，避免游戏退出保存时覆盖启动器选择；不承诺运行中热加载。选择停用文件时恢复该文件，再写入选择；预览不产生这些效果。写入前核对指纹，临时写入并可恢复替换，旧配置留在实例的 `.rodel/shader-settings`。界面不堆成功提示或确认步骤。

## 来源与取舍

- HMCL 固定参考提交 `8e6ac947ed83a38879238c4cf3efcdfb013d06a0` 的 [ResourcepackListPage.java](https://github.com/HMCL-dev/HMCL/blob/8e6ac947ed83a38879238c4cf3efcdfb013d06a0/HMCL/src/main/java/org/jackhuang/hmcl/ui/versions/ResourcepackListPage.java) 与 `HMCLCore/.../resourcepack/ResourcepackFile.java`：按实例管理实际 ZIP / 目录与元数据，提供添加、目录和删除操作。源码没有证明“安装就自动选用”；本实现借鉴其库存边界，实际选择由明确开关完成。
- [IrisConfig.java](https://github.com/IrisShaders/Iris/blob/26.1/common/src/main/java/net/irisshaders/iris/config/IrisConfig.java) 使用 `shaderPack` / `enableShaders` 和 Java Properties；读取流采用 Latin-1 / Unicode 转义。Core 保留无关字节、注释、重复键及换行，只修改归属键，不以 UTF-8 重写整份配置。
- Iris / Oculus 分别使用 `config/iris.properties`、`config/oculus.properties`；OptiFine 使用 `optionsshaders.txt`，`shaderPack=OFF` 表示停用。合成配置回归覆盖三种路径；真实游戏证据单独记录，不从文件写入推定渲染效果。

## 接线

Sprout 内容页 → Core `GetResourcePacksAsync` / `SetResourcePackUsageAsync` / `MoveResourcePackAsync` 与 `GetShaderPacksAsync` / `SelectShaderPackAsync`。CLI / MCP 复用同一 Core，新增规范 `shaderpack list/select` 与 `shaderpack_list/select`。HTML 使用模拟选择与优先级，不能作为真实游戏验证。
