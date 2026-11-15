# World Reroll 验证

本模块迁入自 [Richasy/WorldReroll](https://github.com/Richasy/WorldReroll)，固定基线提交为 `4d2fcb1b764ae2dfca65e24a0cb19836140265a6`（`0.2.0+26.3`）。保持 `worldreroll` 模组ID、Java包名、行为、资源及MIT许可证；原仓库的Git历史、缓存、构建产物和游戏运行目录不迁入。

## 构建与单元测试

使用JDK 25；从启动器仓库根目录运行：

```powershell
cd .\mods\world-reroll
.\gradlew.bat build compileGametestJava processGametestResources --no-daemon
```

`build`编译主源码、客户端源码并执行JUnit测试；后两个任务另行编译、处理客户端游戏测试源码及资源，不启动Minecraft。依赖已缓存时可加`--offline`。产物为`build/libs/worldreroll-0.0.1.jar`，不包含测试入口；单元测试报告位于`build/reports/tests/test/index.html`。

现有10项单元测试覆盖：

| 源码 | 保护的行为 |
| --- | --- |
| `RerollSessionTest`（3项） | 新世界就绪前禁止删除、取消及失败不授权删除、拒绝重复准备 |
| `WorldIdentityTest`（2项） | 精确来源及活动世界排除、来源目录被替换后保留 |
| `WorldFolderTest`（2项） | 可读命名及冲突不覆盖、Unicode与Windows非法/保留名称处理 |
| `WorldDataPacksTest`（3项） | 可选缺省及完整嵌套复制、目标冲突不覆盖、符号链接拒绝与来源字节保留 |

测试源码位于`src/test/java`，使用临时目录。不把单元测试或编译通过称为游戏内兼容验收。

## 真实客户端游戏测试

在本模块目录中另行运行：

```powershell
.\gradlew.bat runClientGameTest --no-daemon
```

该任务使用Fabric Client GameTest及`src/gametest`中的五个场景，实际同载 Rodel AI、World Reroll 与 BetterTools，不加载 Rodel UI：

- `CreationGameTest`：真实世界分类开关、默认值与结构生成限制；指定种子和世界参数保留；村庄出生、奖励箱及村庄定位失败时保留新世界和既有存档。
- `RerollGameTest`：真实暂停入口与面板、取消、设置和数据包保留、延迟删除精确来源、其他目录保留、LAN限制、村庄出生和失败保留；创建前失败修复后重试，以及活动替代世界中的连续失败→重试成功→精确删除最初来源／保留失败世界与返回。
- `VillageSpawnGameTest`：实际失败种子与初始玩家位置，先核对定位到同一雪地村庄，再以该村庄为异步搜索锚点，避免新的距离排序改选其他群系掩盖雪层回归；验证安全出生、世界出生点和可用奖励箱，并检查受控的土径／薄雪／厚雪／水列。该场景不使用玩家存档。
- `RerollPresentationGameTest`：实际原生最小320×240 GUI预算中的新设置及确认页；正文裁剪、末端选项、固定footer、取消后保持原父页及同一世界。使用原生guiScale，不能以小于原生允许尺寸的测试代替小窗口验收。
- `FirstPartyMenusGameTest`：暂停页打开AI对话、关闭回实际父页及保留同一世界，原生Options与该版本实际存在的世界设置页。`AiTranscriptChecks`另以受控Core响应和真实客户端字体检查未变化帧复用、增量/最终回答、旧历史、换行、主题/密度、审批详情、选项几何及原生Language替换后的刷新；恢复被替换的Language，不声称完成真实资源包重载或模型回答。`AiConsoleChecks`使用真实Enter/Esc验证AI提交后保留聊天和草稿、240tick后的定点消息更新时间，以及裸`/rai`打开的面板不被提交收尾关闭。此场景不连接模型或启动器AI桥。`FirstPartyAppearanceChecks` 通过真实原生按钮、独立配置文件及原父页回调验证三个模组的主题保存、取消和相互隔离。

村庄重开的正向场景使用仅限测试模组的一次性随机种子控制：来源种子42、替代种子8611，并核对实际新世界种子。这样避免随机生成雪地或不安全地形后，已正确保留来源的可恢复失败被成功场景误判。实际生成、村庄定位、安全落脚、奖励箱与延迟删除仍执行产品代码；独立的缺村庄/坏数据包失败场景继续核对保留。出生区域由真实结构起点包围盒判断，不用另一次locate的不同搜索结果冒充当前位置错误。测试Mixin、种子控制及入口均不进入正常JAR。

默认目标仅使用`build/run/clientGameTest`中的一次性世界；其他目标通过`'-Pminecraft_target=26.2'`等参数选择，构建和运行目录位于`build/targets/<版本>`。手动开发运行目录为`run/client`。删除测试不得指向个人实例或存档，生成的截图、日志及测试世界不提交。

当前界面使用本模组自己的主题与 Minecraft 原生字体，主题在自身配置页保存；村庄准备和重试沿用本模组外观，原生世界加载／保存回调保持。取消、确认及新世界就绪后精确删除来源继续使用原有业务边界。
兼容范围见[兼容矩阵](../../COMPATIBILITY.md)。单元测试、编译和真实客户端运行分别验证，不将其外推到未测试的版本、加载器或第三方模组组合。
