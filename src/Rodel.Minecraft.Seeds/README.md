# Rodel.Minecraft.Seeds

种子探索器的独立 .NET 11 / NativeAOT 类库，与 `Rodel.Minecraft.Skins` 同级，不引用启动器 Core、CLI 或 Sprout。

## 源码组织

源码按职责分目录，每个 C# 文件仅定义一个类型；所有类型仍使用 `Rodel.Minecraft.Seeds` 命名空间，不因目录调整改变调用方式或 JSON 契约。

- `Runtime`：已安装版本、Java 选择及版本支持信息。
- `World`：生成设置、正向见证坐标及仅供复制的传送指令。
- `Catalog`：原版维度、群系与结构注册目录。
- `Maps`：地图请求、瓦片、冻结视野及栅格/PNG 导出。
- `Search`：正向条件、依赖匹配及可恢复搜索请求/结果。
- `Regions`：有界方块体积、状态、材料、容器战利品及生成进度。
- `Structures`：有界结构查询与组装见证结果。
- `Scenes`：可见面网格、相机和共享投影；`Resources` 子目录负责原版模型与纹理读取。
- 根目录保留跨领域的 `SeedJsonContext` 源生成入口及 `SeedValidation` 校验入口。

## 能力与边界

- 提供原版地图、群系/结构目录、正向条件搜索和生成配置的强类型契约、JSON 源生成元数据及请求/结果校验。
- 提供共享地图配色、地形栅格、带生成上下文的 PNG 编码和仅供复制的维度传送指令格式化。
- `SeedMapView`冻结矩形视野，PNG导出优先使用实际细瓦片，拒绝缺失像素及混合运行时/世界/种子；Core负责最终原子文件提交。
- `SeedRegionQuery/Result`定义有界FULL方块、原版容器战利品和材料计数；`GenerationPolicy`与范围共同标识新生成预览，不能重建任意已有世界。
- `SeedModelResources`读取指定原版客户端JAR的blockstate/父模型/variants/multipart与PNG，`SeedBlockScene.CreateMesh`构建可重复裁层的可见面；`SeedSceneCamera`共享GPU/图钉投影，右拖使模型近侧向右移动。不包含GPU或窗口依赖。
- 模型预览支持JSON元素、楼梯/台阶等形状和闭合宝箱；缺失的特殊模型显式列出并跳过。空气和流体不绘制，光照/透明/动画/染色简化，装饰性加权模型取首项；不声称复用Minecraft完整渲染器。
- 正常构建从 `java/seed-explorer` 生成并嵌入自有 Java 适配器。它调用玩家本地精确版本的原版游戏代码；不分发 Minecraft JAR，不引入快速库，也不将世界生成算法移植到 C#。

第一项已验证的适配版本为 Minecraft Java **26.3 / Java 25**。版本不得近似映射。地图表示原版基础地形列/水体和指定高度的群系；不代表已完成装饰或生成了玩家存档。结构使用原版完整选择与拼装流程，坐标为拼装范围中心。群系条件只承诺采样点的正向见证，不用网格未命中证明不存在。

启动器的 `Core/Worlds/Seeds` 负责安装版本与 Java 准备、受控 JVM 生命周期、精确文件指纹、瓦片缓存、持久搜索、收藏及导出文件事务。App 与 CLI 统一通过 Core 调用。独立库不会读启动器配置、玩家身份或存档。

技术边界、真实验证和独立窗口设计见 [`doc/SEED-EXPLORER.md`](../../doc/SEED-EXPLORER.md)。
