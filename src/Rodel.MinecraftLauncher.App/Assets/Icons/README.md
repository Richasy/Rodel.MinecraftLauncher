# 应用图标

`launcher.png` 是1024×1024透明母版：草方块、浅色启动符号、左上光照，无背景板。可见主体四边保留4px；运行资源位于 `../AppIcons`，EXE图标为项目根的 `app.ico`。

使用 Sprout DevTools 的 `generate assets --full` 生成完整的 scale、targetsize 与 unplated/lightunplated 资源矩阵，输出到 `Assets/AppIcons`。ICO包含16、24、32、48、64和256px六帧及透明通道。`ApplicationIcon` 指向 `app.ico`，包清单指向逻辑Logo文件名，由MakePri索引实际资源。

## 高对比度图标

`launcher-hc-black.svg` / `launcher-hc-white.svg` 为深底／浅底的单色版本：实心草方块、透明镂空棱线及启动三角，无纹理、渐变、阴影或底板。`black` / `white` 指目标背景，主体分别为白色／黑色；透明母版为1024×1024。

运行 `scripts/assets/generate-hc-icons.ps1` 重生成SVG、PNG、ICO及带 `contrast-black` / `contrast-white` 限定符的资源。预览和中间输出归忽略的 `artifacts`，不作为运行依赖。主体alpha边界贴画布，四角透明来自方块轮廓。

任务栏和Alt+Tab图标通过 `Theming/ApplicationIconTheme.cs` 与Sprout `Window.Icon` 同步。高对比度模式按窗口系统背景亮度选择对比更高的版本，退出后恢复EXE彩色图标。

参考：[Windows资源限定规则](https://learn.microsoft.com/en-us/windows/uwp/app-resources/tailor-resources-lang-scale-contrast)、[应用图标高对比度设计](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-design)。
