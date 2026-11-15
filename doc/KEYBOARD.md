# 键盘导航与焦点

本文规定 RMCL 原生 App 的键盘交互，不把 UIA 属性存在等同于键盘或 Narrator 验收通过。游戏内模组沿用各自的原生输入约定。

## 操作约定

| 场景 | 行为 |
| --- | --- |
| 普通表单 | Tab / Shift+Tab 按阅读顺序前进／后退，跳过隐藏、禁用和纯装饰内容；输入框、滑块、数值框保留自身编辑键和 IME。 |
| 导航、页签、工具组 | 一次 Tab 进入，方向键按实际布局移动，Enter / Space 执行动作，再次 Tab 离开整组；重新进入优先恢复组内最后焦点。只移动焦点不提前执行切页、下载或删除。 |
| 列表与网格 | 方向键移动真实条目焦点并将其带入视口，Home / End 到首尾；虚拟列表的 PageUp / PageDown 使用实际视口。二维网格遵循屏幕方向，RTL 不倒转业务数据。 |
| 多选图库 | 普通方向键选择目标，Ctrl+方向键仅移动焦点，Shift+方向键延伸范围；选择状态和键盘焦点分别呈现。 |
| 滚动区域 | 有可操作内容时，Tab 直接进入内容，不先停在滚动外框；纯阅读的可滚动区域保留自己的键盘入口。 |
| Dialog / Popup / Flyout | 打开后焦点进入当前弹层，Tab 不逃到背后。Esc 按既有取消／关闭策略只处理当前层；嵌套关闭逐层归位。触发控件重建时恢复同一稳定身份，已删除时选择原区域仍可用的目标，不聚焦孤立旧对象。 |
| 异步回读与系统选择器 | 不从仍打开的子弹层抢焦点；程序归位沿用实际输入方式。初始目标已经聚焦也算完成，不在后续布局反复跳回。 |
| 地图、结构预览与图片裁切 | 画布可用 Tab 到达，只在自身焦点范围处理方向键；地图平移、结构旋转、裁切移动不再抢整个窗口的方向键。Tab 仍可离开。 |
| 皮肤视图 | 方向键旋转模型或平移平面，Home 复位；剪切、复制、粘贴及删除像素限制在画布上下文，不从图层／历史／其它表单误触发。自由绘画的完整无鼠标替代不由这些导航能力推定完成。 |

鼠标／触摸／笔按下隐藏键盘焦点环，单纯 hover 不撤销键盘提示。键盘导航或激活即显示焦点环，不要求先按一次 Tab；程序聚焦和弹层归位不强制开启边框。文字输入的光标、选区和原生编辑状态不属于待隐藏的键盘焦点环。

App 的焦点双线在控件边界内绘制，避免填满滚动视口的导航、列表或网格条目只剩被裁切的横线；不能通过取消真实滚动裁切来显示屏幕外的焦点。

复杂列表中含多个编辑字段时保留逐字段 Tab，不能为减少停靠点把输入框藏进无法退出的复合控件。页面导航和文件操作继续复用已有业务回调；焦点变化不是操作授权。

## 实现与验证

Sprout 拥有输入方式、焦点遍历／范围、虚拟化实现及滚动跟随；App 只声明哪些真实控件形成一组、哪些键归画布，以及失效业务目标的恢复位置。静态命令组用 `LauncherControls.KeyboardGroup`，交互集合用 `ItemsView` 或 `ItemsRepeater.KeyboardNavigation`；表单型集合保留 `singleTabStop: false`。不在页面复制 Tab 状态机，不靠任意 TabIndex 或额外按钮修补重复停靠。

正式 App 回归见 `test\Rodel.MinecraftLauncher.App.Tests\NativeKeyboardNavigationTests.cs`。它使用真实控件投影和回调，覆盖设置分类、皮肤页签、裁切及滚动，不代替正常 packaged App 的真实键鼠检查。原生输入、窗口焦点、弹层、截图及系统辅助显示条件通过 Sprout.DevTools 在隔离 VM 验证；不得向开发者桌面注入键鼠。检查正反 Tab、键鼠切换、关闭／取消、嵌套弹层、滚动首尾、隐藏／禁用目标及 RTL，并分别报告实际覆盖。

## 依据

- [Microsoft：Keyboard interactions](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions)：Tab 与方向键分工、逻辑顺序、初始焦点及安全操作。
- [Microsoft：Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility)：可达性、焦点层级与自定义控件。
- [Microsoft：Focus navigation](https://learn.microsoft.com/en-us/windows/apps/develop/input/focus-navigation)：方向区域和实际布局。
- [Microsoft：Dialog controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)：模态边界及安全关闭。
- [W3C APG：Keyboard interface](https://www.w3.org/WAI/ARIA/apg/practices/keyboard-interface/)：单一复合停靠点、焦点持久性、焦点与选择分离。
- [W3C APG：Modal dialog](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/)、[Listbox](https://www.w3.org/WAI/ARIA/apg/patterns/listbox/)、[Grid](https://www.w3.org/WAI/ARIA/apg/patterns/grid/)：初始／恢复焦点及集合内部导航。

APG 的行为原则用于对照，不把 DOM、ARIA 属性或浏览器实现搬进原生 App，也不据此宣称完整 WCAG 合规认证。
