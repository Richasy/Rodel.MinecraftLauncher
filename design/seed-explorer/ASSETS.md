# 种子探索器图标资产

当前资产为119张透明PNG：Minecraft Java26.3原版注册表的67群系和52结构条目（含变体）。清单来自实际原版catalog，并与客户端JAR的`data/minecraft/worldgen/biome`及`structure`文件逐项一致。`icons.json`保存精确ID、来源图集、裁切范围和内容摘要；实际业务目录仍读取原版，不以这份设计资产清单替代运行时注册表。

- 群系使用树木、花草、蘑菇、矿物、海水等单个代表物，不使用地块/岛屿场景。
- 结构使用一个建筑或建筑特征；五种村庄、18种废弃营地、七种废弃传送门及其他原版变体各有对应图片。
- 图片为AI生成的示意素材，四张`source-items-*.png`为当前源图集；不复制MinecraftSearch图片或原版纹理。它们不代表某个seed中结构的实际外形。实际结构3D另读取原版生成方块、模型及纹理。
- `assets/biome-*.png`和`assets/structure-*.png`统一128×128透明画布，按主体轮廓裁切并保留完整物件；用24～32 DIP显示。App自包含副本位于`src/Rodel.MinecraftLauncher.App/Assets/SeedExplorer`，运行时不读取design。
- 地图、筛选、图例及详情共用精确ID映射。筛选条件的“任意村庄/任意营地”分别采用平原村庄/森林营地作为集合代表；具体条目仍用各自图片。
- 缩小地图时继续使用有场景含义的彩色线条符号。普通图钉保持轻薄半透明，悬停加深；图例与地图保持同一表现方式，不画白色图片底板。

官方版本背景：[Minecraft Java26.3](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-3)。交互密度参考[MinecraftSearch](https://minecraftsearch.com/tools/seed-map)，不引用其种子索引、图片或准确率声明。
