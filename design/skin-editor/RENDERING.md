# 原生皮肤 3D 渲染路径

本页维护Sprout皮肤编辑器的原生渲染路径与后续验证边界。已经实现D3D11视口与直接3D笔划；HTML原型与CPU PNG仍不是原生GPU像素或性能验收证据。

## 当前路径

采用App持有的D3D11渲染器，通过同版本`Sprout.SwapChain`的`SwapChainView`嵌入中央视口。Sprout管理布局、DPI、裁切与UI覆盖，App持有独立设备、composition swap chain、纹理、shader和呈现生命周期。GPU依据同源Quads推导12个凸盒并解析射线求交，每盒只取最近前向表面，最多排序12个命中，按近到远合成并在不透明后停止；人物投影包围区域以外不运行求交。中央UV小窗、按钮、提示仍由普通Sprout控件覆盖；不引入游戏引擎、WebView或独立HWND，也不修改Sprout现有D2D后端来塞入业务模型。

这个模型规模很小：6个身体部位、底层与外层共12个长方体，即72个面、144个三角形。纹理只有64×64或128×128；相机旋转不需要重建几何或重新上传纹理。因此主要工程量在输入与可见像素的一致性、透明外层、设备恢复和资源释放，而非模型复杂度。是否流畅、省电须实际测量，不能由面数推定。

| 路径 | 适用性与取舍 |
| --- | --- |
| D3D11 + SwapChainView | 首选；已有原生承载点，GPU负责纹理采样、深度与投影，独立呈现，UI覆盖沿用Sprout |
| 现有SkinPreview CPU → PNG → Image | 保留CLI、静态缩略图和离线验证用途；当前逐输出像素遍历模型面并编码PNG，64～512方图，不作为拖动时持续编码/解码的交互通路 |
| CPU投影后在D2D拼贴各面 | 会把透视校正、遮挡、半透明及纹素边界重新搬到App；没有比现成D3D11路径更明确的收益 |
| D3D12或完整3D引擎 | 当前工作量没有要求其同步、资源管理或场景系统；先不引入 |

## 已由源码证实的基础

App当前引用Sprout `0.1.0-rodel.93.2.scrollfix`，图形包对应commit `eee6b8c1ce709b0cae9d52edd9b2020724333842`，没有用本机较新HEAD的新增能力作为依赖前提。

Sprout源码入口（相对于Sprout仓库）：

- `packages/Sprout.SwapChain/SwapChainView.cs`：接收借用的swap chain COM指针，普通UI合成在其上；`OnSizeChanged`回调直接提供视口物理像素尺寸，移动与DPI几何同步由框架完成。它自身没有输入节点，需要由App组合输入宿主。
- `packages/Sprout.SwapChain/README.md`：明确App创建自己的设备与composition swap chain，Sprout仅拥有hosting visual。当前承载点与`WindowSpec.UseRenderThread=true`不兼容；编辑器当前未启用该选项，默认false。
- `src/Sprout.Interop/GraphicsDevice.cs`：已有D3D11设备封装、hardware/WARP选择及COM释放约定，可复用创建能力；不要借用窗口内部immediate context并跨线程调用。
- `samples/Sprout.Cookbook/SwapChain/AnimatedSwapChain.cs`：独立设备、BGRA8双缓冲composition chain、resize/present/dispose示例；只清屏，不是现成3D renderer，不能把它当作shader与半透明已实现。
- `SwapChainView.OnDeviceRecovered`只恢复Sprout hosting并重新挂接已有内容。App自己的设备若被移除，仍须检查Present/ResizeBuffers返回值，重建自己的资源。

产品已引用并还原同版本`Sprout.SwapChain`，不升级整组Sprout包。HLSL在构建时使用Windows SDK的fxc编译到obj并嵌入应用，NativeAOT运行不需要shader编译器；shader源码与构建脚本纳入Git，生成的cso不纳入Git。

走路Popup使用此版本已有的单输入CustomImageEffect，直接在Sprout的D2D设备绘制，共享SkinShaderScene几何和SkinViewport.hlsl解析光线。2048方形画布只创建一次；实际映射表面的RGBA8按面紧凑打包进常量，Classic128最多13056个纹素，连同几何、投影范围和姿态共57968字节，低于D2D的64 KiB限制。保持原始颜色与透明度，不降色或编码图片，每帧提交独立常量，不让排队绘制读取后续姿态。不再通过外部atlas图像及大采样半径的多输入pipeline，也不逐帧D3D读回/创建ImageSource。四光线覆盖AA保留，关闭停止调度并撤下效果引用，最小化暂停。静态导入/新建缩略预览仍按需读回，不承担连续动画。

Popup按当前姿态投影同源身体盒的96个角点，范围外直接填背景，不执行四组求交。包围范围保留两像素AA余量；相机平面穿过盒体时保守绘制全幅，不能用错误裁切换取低占用。GPU利用率取同窗口、同尺寸的进程3D引擎计数器，区分播放、关闭与最小化，不把整张显卡的合计占用归给预览。

启动器已有入口：

- [SkinGeometry](../../src/Rodel.Minecraft.Skins/Geometry/SkinGeometry.cs)的`Quads`提供实际面原点、完整Right/Down轴、UV矩形，`Pick`提供同源射线命中；支持Classic/Slim及64/128。外层头膨胀0.5、身体0.25模型单位，HD只改变纹素密度，不改变几何尺寸。
- [SkinFrame](../../src/Rodel.Minecraft.Skins/Geometry/SkinFrame.cs)定义边界坐标与纹素中心：局部中心为`(x+0.5)/width`。GPU应使用同样角点/UV方向，不能重新维护一张模型坐标表。
- [SkinLayers.Pixels](../../src/Rodel.Minecraft.Skins/Editing/SkinLayers.cs)提供合成纹理快照。Core [SkinService](../../src/Rodel.MinecraftLauncher.Core/Skins/SkinService.cs)的OpenEditor返回具体SkinEditingSession，持有内存内容；Render缓存独立SkinRenderSnapshot，同一个快照提供像素、Quads及无读盘/历史校验的Pick。
- [SkinPreview](../../src/Rodel.Minecraft.Skins/Imaging/SkinPreview.cs)是正交CPU射线预览，逐像素按距离合成可见面，具有固定背景；它不是HTML的透视相机。不能直接把两种投影截图当逐像素对照。

## 边界与数据流

保持App → Core → Skins。Core对同一有效文档/临时预演快照提供模型、尺寸、修订、合成RGBA及同源几何，不引入通用渲染服务。App负责GPU资源、相机、布局与指针坐标。底层库继续不引用Sprout或DirectX。

快照拥有独立合成数组，不能把文档内部可变数组交给渲染线程。相机变化只提交观察状态，模型/尺寸对应几何及角点缓存；文档或笔划预演变化才产生新纹理快照。64/128全图上传初期已足够小，先不造脏矩形系统。输入按顺序增量进入同一SkinDrawing，保留中心去重、最大coverage及原始活动层像素；只有coverage增加的纹素重算，不能在已预演颜色上累计混合。离开模型/进入UV覆盖区后的下一点BreakBefore，不补画穿过空白区域。4096点上限保留并提交整笔，提示重新按下，不丢弃或静默拆历史。

UI每帧最多生成一次当前纹理快照，producer只有一个待处理帧槽；不能丢弃笔划输入点，也不为每个pointer move堆积Task或PNG。抬笔在后台走真实expectedRevision、资源租约及原子保存，失败保留草稿，可重试或明确放弃后重新读取工程。保存期间仅允许一笔下一手势缓冲，冻结配置并按顺序存命中点，不重投影；前笔成功才提升，失败保留两笔，重试不绕过expectedRevision，放弃同时清除两者并重读。队列满明确反馈，每笔4096点上限封口保留而非丢弃。保存/草稿/待处理笔划未处理时阻止关闭、新建和打开；取消活动手势只丢弃未提交预演。

相机初始采用与认可原型一致的透视轨道观察，当前围绕整体中心旋转，已提供旋转、复位和缩放按钮；选中部位中心、标准正背视角及完整键盘替代仍待接入。App将控件内DIP转换到视口NDC，用同一view/projection逆变换生成模型空间射线，再由Core调用已有Pick。DPI只在明确边界转换一次；不得将物理像素尺寸用于DIP指针坐标而不换算。

拾取必须使用本次呈现的相机、视口与文档快照版本；尺寸/相机/纹理状态改变时取消过期手势。观察命中和绘制命中分开：绘制外层传`Layer=Outer, SkipTransparent=false`，否则空白帽子/衣物无法起笔。显示外层与活动绘制表面仍是两个状态，不在不可见表面静默落笔。导航不产生文档事务；后续绘图继续一笔一次提交。

## 绘制正确性

当前使用解析射线而非面中心排序或三角形深度近似。纹理以整数texel Load读取单mip，边界UV及纹素中心与Pick相同；不加D3D9式半像素偏移。同深度时较早几何序号在前，CPU静态预览的远到近混合需将同深度序号反序，保持与Pick/GPU一致。网格由同源UV生成；投影笔刷轮廓复用BrushFootprint生成按面边界mask，独立于RGBA上传；在透明跳过之前采样轮廓，再参加同一深度排序与合成。不能为每个纹素生成控件。

半透明外层保留实际alpha，不做二值镂空。肩部等交叠使用逐像素距离与固定tie规则合成；原型的面中心排序不是交付算法。实际GPU target读回与无光照CPU参考比对；不能仅凭截图视觉推定正确。

颜色源为`0xRRGGBBAA`的straight alpha；在小端机器不能直接把uint内存当RGBA8上传。需要明确字节转换及shader/混合约定，防止红蓝互换与二次乘alpha。首版视口可绘制不透明主题背景并以IGNORE alpha呈现；若使用透明composition chain，则输出必须符合premultiplied约定。纹理颜色、灯光显示和导出像素分开，灯光不写回文档；纯色/无光照状态作为颜色见证，不能从有光照截图反推取色值。

## 生命周期与首个纵切

App渲染工作使用单一资源所有者串行处理draw、resize和dispose；UI只发布有界状态并在UI线程挂接/摘下surface。D3D11 immediate context不能被多个线程同时使用。静止时按需呈现，相机拖动或后续动画才连续更新；最小化、隐藏、切平面视图停止无意义呈现，不能仅凭underlay被裁空就认为producer已暂停。

关闭时UI摘下surface并停止发布；producer仍持有自己的COM引用，串行退出后释放，UI不Wait/Join可能正在Present的线程。跨线程attach暂借额外COM引用并在UI操作结束释放，过期窗口回调不重新挂接。ResizeBuffers前释放/解绑backbuffer的RTV等引用；零尺寸不创建缓冲。App设备丢失与Sprout设备恢复分别处理，恢复失败保留可解释状态及现有文档，不影响PNG导出/工程。

硬件路径按实际物理像素绘制。WARP软件路径的解析射线限制在512K离屏像素，再以nearest resolve输出到完整物理尺寸swap chain；UI、相机/NDC与64/128 atlas精度不变，软件回退的轮廓空间采样会较粗。它不改变拾取或导出，也不能把软件分辨率策略记作全物理分辨率性能。不能用提高纹理mip、二值透明或错误排序换取速度。

当前纵切包含真实工程新建/打开、3D画笔/橡皮/填充/取色、镜像/大小、部位及外层显示、相机旋转/缩放/复位、Core保存和撤销重做。主平面/UV绘制已接入同一会话，平面隐藏UV小窗及入口，3D恢复原偏好；图层已有真实缩略图和选中绘制目标。选区、完整图层管理、PNG导入导出按钮与AI仍未接入；新建表单还不是完整模板缩略图工作流。后续完善投影足迹、键盘流程、跨DPI/设备恢复和以上业务，不能将此切片记作编辑器全功能完成。新增正式测试仍按test/GUIDELINES逐项审查价值，不测试D3D getter或复刻坐标表。

验收必须区分：源码存在、编译通过、GPU实际画面、交互正确、性能测量。应覆盖透明交叠、正背及上下各面、人物自身左右、Slim袖口、128相邻微像素、空外层命中、无命中不绘制、视口大小/DPI变化、UI popup覆盖、设备恢复和资源释放；记录硬件/WARP、视口实际像素、NativeAOT配置与帧时，不能只报一个无条件FPS。

## 官方资料

- [CreateSwapChainForComposition](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgifactory2-createswapchainforcomposition)：composition chain创建与flip/scaling约束。
- [D3D11设备与context线程规则](https://learn.microsoft.com/en-us/windows/win32/direct3d11/overviews-direct3d-11-devices-intro)：资源创建与immediate context串行访问边界。
- [D3D11 point采样](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_filter)：纹理过滤选项。
- [Output-merger](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-output-merger-stage)：深度、混合与像素shader alpha处理。
- [设备移除恢复](https://learn.microsoft.com/en-us/windows/uwp/gaming/handling-device-lost-scenarios)：Present及ResizeBuffers的设备移除检查；采用设备恢复原则，不引入其UWP宿主。
