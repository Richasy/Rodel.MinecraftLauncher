# 皮肤编辑器 · 社区设计依据

本页列出皮肤编辑器的外部技术参考；产品交互以[设计规范](../DESIGN.md)和[编辑器规格](DESIGN.md)为准。

## 参考项目与取舍

| 项目 | 已核实的能力与证据 | Rodel 设计建议 |
| --- | --- | --- |
| Blockbench / Skin mode | **源码**：专用皮肤格式、Steve/Alex、外层显示、模型展开、PNG 导出；绘图工具包括画笔、橡皮、填充、吸色、形状、选区、镜像、透明度锁定和绘图图层。**官方文档**：Paint mode 的 UV Editor 可以绘画。见 B1–B4。 | 3D、UV 和并排视图共用同一像素文档；当前工具只显示相关属性。部位隔离及固定朝向用于背面、腋下等区域的精修。 |
| NeedCoolerShoes | **源码**：皮肤底层/外层分别显示，身体各部位可隔离；图层复制、合并、重排与历史记录独立存在；填充有五种范围；PNG 与保留图层的工程分别导出。**官方页面**：图层优先、参考图取色、Slim/Classic。见 N1–N6。 | 明确区分“皮肤表面：底层/外层”和“绘图图层”。填充范围有可读名称；作品工程与最终 PNG 不混为同一保存动作。 |
| PMCSkin3D | **官方页面与内置帮助**：3D/2D、部位显示、参考图、调色板；表面 X/Y 镜像与跨肢体镜像分开；笔画可跨面或锁定起笔面；支持 PNG 下载。未核实其开源实现。见 P1。 | 镜像范围和跨面行为持续可见，避免隐式扩大修改范围。色板与参考图服务手动绘制；姿势展示为次级能力。 |
| MCSkin3D | **历史源码**：Windows/.NET 桌面编辑器，提供 3D、2D、混合视图，画笔、橡皮、吸色、明暗、填充、噪点、印章，可配置快捷键、撤销历史及 PNG 保存。见 M1–M3。 | 借鉴独立文档窗口和稳定工具位置；二维精修、三维检查均能独立占满工作区。不照搬旧式密集菜单。 |

## 固定源码与官方来源

### Blockbench

固定源码：[`ec0b2827104d0799c153bfb5f867579c0c44652f`](https://github.com/JannisX11/blockbench/commit/ec0b2827104d0799c153bfb5f867579c0c44652f)。

- **B1**：[Skin 格式与编辑入口](https://github.com/JannisX11/blockbench/blob/ec0b2827104d0799c153bfb5f867579c0c44652f/js/formats/minecraft/skin.ts#L291)；同文件包含[外层、Steve/Alex 转换、导出与模型展开](https://github.com/JannisX11/blockbench/blob/ec0b2827104d0799c153bfb5f867579c0c44652f/js/formats/minecraft/skin.ts#L594)。
- **B2**：[画笔等工具注册](https://github.com/JannisX11/blockbench/blob/ec0b2827104d0799c153bfb5f867579c0c44652f/js/texturing/painter.js#L2871)，以及[矩形、椭圆、套索、魔棒和按颜色选区](https://github.com/JannisX11/blockbench/blob/ec0b2827104d0799c153bfb5f867579c0c44652f/js/texturing/painter.js#L3287)。同文件可核对 B/E/M 等工具快捷键和镜像参数。
- **B3**：[绘图图层、混合模式、图层层级和撤销](https://github.com/JannisX11/blockbench/blob/ec0b2827104d0799c153bfb5f867579c0c44652f/js/texturing/layers.ts#L18)。
- **B4**：[官方 Overview & Tips](https://blockbench.net/wiki/guides/blockbench-overview-tips/)。

转换边界：B1 的 `adjust_texture` 分支调整 Steve/Alex 纹理时，会关闭并清空纹理编辑图层，再记录纹理撤销。由此得出的 **Rodel 建议** 是：模型转换必须说明纹理影响并支持撤销，不能假设它只是预览开关；不据此宣称所有社区编辑器都存在同样行为。

### NeedCoolerShoes

固定源码：[`e603440257e94dc7838bcdf7265716a06914d9a0`](https://github.com/NeedCoolerShoes/editor/commit/e603440257e94dc7838bcdf7265716a06914d9a0)。

- **N1**：[底层/外层显示及各自网格](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/tools/editor_toggles.js#L65)，[部位显示与隔离](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/tools/part_toggles.js#L134)。
- **N2**：[填充范围配置](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/config/tabs/tools/configs/bucket_tool_config.js#L24)：整个部位连通、当前面连通、整个部位、当前面、全图同色。这里的“部位”对应项目文案中的 cube。
- **N3**：[图层与工具快捷键](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/keybinds.js#L6)：B 画笔、E 橡皮、G 填充、I 吸色、Ctrl+Z/Y、0 重置视角，以及添加、复制、合并图层。
- **N4**：[PNG 和 `.ncrs` 工程的不同导出路径](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/config/tabs/export/buttons.js#L44)。
- **N5**：[前后/左右翻转、底层与外层交换和合并](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/config/tabs/layers/buttons.js#L56)；操作通过历史项更新选中绘图图层。
- **N6**：[官方网站的产品说明](https://needcoolershoes.com/)，[Sculpt 官方说明](https://wiki.needcoolershoes.com/skin_editor/tools/sculpt/)：将底层颜色复制到外层，以增加外观层次。

### PMCSkin3D

- **P1**：[官方编辑器与内置帮助](https://www.planetminecraft.com/skin-editor/)。读取范围包含 Mirror、Brush Stroke、Visible Parts、文件菜单和保存说明。镜像、跨面与锁面结论来自官方文字，不以社区评论推定实现。

### MCSkin3D

固定源码：[`e82294ede91257e0064c36993c26773f7f8e2f8d`](https://github.com/Paril/mcskin3d/commit/e82294ede91257e0064c36993c26773f7f8e2f8d)；仅作为历史桌面工作流参考，不推定其当前游戏兼容性。

- **M1**：[3D、2D 与混合视图绘制](https://github.com/Paril/mcskin3d/blob/e82294ede91257e0064c36993c26773f7f8e2f8d/Forms/Editor.cs#L1325)。
- **M2**：[工具及默认快捷键](https://github.com/Paril/mcskin3d/blob/e82294ede91257e0064c36993c26773f7f8e2f8d/Forms/Editor.cs#L4407)，[快捷键注册与保存](https://github.com/Paril/mcskin3d/blob/e82294ede91257e0064c36993c26773f7f8e2f8d/Forms/Editor.cs#L3195)。
- **M3**：[PNG 另存为](https://github.com/Paril/mcskin3d/blob/e82294ede91257e0064c36993c26773f7f8e2f8d/Forms/Editor.cs#L3812)，[撤销列表](https://github.com/Paril/mcskin3d/blob/e82294ede91257e0064c36993c26773f7f8e2f8d/Forms/Editor.cs#L3533)。

## 模型、分辨率与用途边界

| 事实 | 一手依据与适用范围 |
| --- | --- |
| Classic / Steve 为4像素手臂，Slim / Alex 为3像素手臂 | Minecraft 官方 [What is a Minecraft Skin?](https://www.minecraft.net/en-us/article/what-is-minecraft-skin) 说明两种模型及导入时选择模型的流程。此处的像素宽度采用标准皮肤基准；HD纹理不会改变人物的几何比例。 |
| 当前官方 Java 账号编辑页的上传尺寸校验要求宽64、高32或64 | 只读核查[账号皮肤编辑页](https://www.minecraft.net/en-us/msaprofile/mygames/editskin)公开加载的[固定前端构建脚本](https://www.minecraft.net/msaprofilejs/2578bbecd5a3728a2f74_13102026_1219/5031.chunk.9dbc00f386adbd412bee.js)：图片尺寸不符合 `64 !== width || (32 !== height && 64 !== height)` 即抛出尺寸错误。该证据是页面当前实现，不是本次真实上传验证，也不是对所有皮肤用途的统一限制。 |
| 128纹理存在独立于Java账号上传的用途 | 微软 [Bedrock Creator 皮肤包验证规则 CSPJ107](https://learn.microsoft.com/en-us/minecraft/creator/reference/content/mctoolsvalreference/cspj?view=minecraft-bedrock-stable#cspj107)列出64×64或128×128。它适用于Bedrock皮肤包，不能作为Java账号接受128的证据。Java社区的[CustomSkinLoader README](https://github.com/xfl03/MCCustomSkinLoader/blob/5830503dfce07eb272a58754f3d748ba77415107/README.md#hd-skins-and-capes-support)明确支持HD皮肤与披风；读取commit `5830503dfce07eb272a58754f3d748ba77415107`，只核实项目说明，不推定所有服务器或客户端都支持。 |

**Rodel设计决定**：新建将模板、手臂模型、纹理尺寸分开。原创“Alex 风格”建议Slim，所有模板仍可选Classic / Slim与64 / 128；玩家手动选择模型后不再被模板建议覆盖。128使用实际HD像素编辑与同尺寸导出，在尺寸选择和导出附近说明“128用于HD纹理，Java版账号皮肤需使用64×64”，不禁用128或静默降采样。

## 动态预览边界

动态预览仅提供MC标准走路循环，采用相反手脚交替的余弦摆动；在现有D3D11皮肤视口中变换肩/髋关节，不安装或导入第三方动画模组、动画资产或Java代码。预览为手动关闭的大视图，没有模式切换。

## 参考图色板与取色

参考图色板采用Windows既有WIC能力，不引入模型或供应商SDK。[WICBitmapPaletteType](https://learn.microsoft.com/en-us/windows/win32/api/wincodec/ne-wincodec-wicbitmappalettetype)将`MedianCut`定义为从图像生成的优化中位切分色板；[IWICPalette.InitializeFromBitmap](https://learn.microsoft.com/en-us/windows/win32/api/wincodec/nf-wincodec-iwicpalette-initializefrombitmap)为实际调用入口。

真实WIC回归观察到复杂图片的量化结果可能是图中不存在的分区中心，因此将中心匹配到实际源样本并去重，再作为最多16色的可编辑起点；不宣称人物分割或自动识别头发／服装语义。解码后的完整原像素另行保存，显示缩放、透明背景合成及量化均不改写精确取色输入。放大镜、逐像素方向键和以鼠标为中心的缩放是本产品的交互选择，不声称复制某一商业软件的实现。

近似色合并与排序使用[Oklab](https://bottosson.github.io/posts/oklab/)后处理：sRGB先线性化，使用公开转换矩阵得到感知明度／对立色坐标，欧氏距离判断近似，色度与色相用于分组。按原图样本归属估计占比，优先保留主要真实代表色，近灰与有色分开，色系内按感知明度排序。具体阈值和60°分组跨度是产品选择，不是“视觉等价”标准或排序美感的普遍保证；正式回归保护近灰合并、肤色保留及源扫描顺序变化后的稳定顺序。

## 交互边界

以下是 Rodel 的设计判断，不是上述项目已实现的 AI 能力：

- 默认打开手动编辑。工具、画布、颜色、部位、图层和历史优先占据工作区；AI 面板由玩家主动打开，收起后继续保留文档和操作上下文。
- 选择状态、可见性、编辑目标和锁定分别表达。底层/外层、选中部位或面、绘图图层及镜像范围必须能在操作前辨认。
- 手动与 AI 共用文档、像素语义和历史。AI 修改携带文档版本及作用范围，先提供前后预览，接受后形成可撤销操作；旧版本提案不得静默覆盖玩家继续绘制的结果。
- “仅修改当前范围”“保护已有内容”“应用/放弃”使用玩家语言；MCP 协议、工具参数和调用细节放在次级详情中。原型中的 AI 提案只是本地交互示例。
- 保存工程、导出 PNG、应用到身份是不同目的。图层工程、模型转换、跨面绘制等后续功能以实际 Core/皮肤库契约和原生验收为准，不能因为参考项目提供就记为本产品已交付。

## 独立披风映射核对

固定源码：[bs-community/skinview3d，84906e983a2cf325f33f515b9a71c871f799054b，src/model.ts](https://github.com/bs-community/skinview3d/blob/84906e983a2cf325f33f515b9a71c871f799054b/src/model.ts)。setCapeUVs按64×32处理，CapeObject为10×16×1；源码区分内外侧，PlayerObject将披风转向人物背后。本项目只核对模型与UV，没有复制该项目源码；自己的坐标系将Cape.Front定义为朝人物背后的外侧，映射(1,1,10,16)，Back为(12,1,10,16)，HD同比扩展UV，几何不放大。摆动仅为编辑器预览，不作为游戏物理一致性的证据。
