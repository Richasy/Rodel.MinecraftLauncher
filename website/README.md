# RMCL website

这个目录是 RMCL 的文档站，定位为验证 Sprout 原生 UI 框架、探索 AI 辅助新手的 PoC。界面和文案以低调的项目说明、操作指导为主。RMCL 免费使用，启动器自有代码按 AGPL-3.0-only 开源，独立组件保留原许可。网站位于 <https://rmcl.richasy.net/>；正式发行版内置微软登录公开Client ID，不要求玩家注册应用，也不把配置或商店发布等同于第三方服务审批。

## 本地运行

产品通过 [GitHub Release EXE](https://github.com/Richasy/Rodel.MinecraftLauncher/releases/latest) 分发x64／ARM64安装包，不再通过Microsoft Store更新。App启动后静默检查，可从标题栏更新图标查看本地化说明、下载或跳过版本。

本地开发使用 Node.js 与 npm；部署版本以工作流为准。依赖锁定在 `package-lock.json`。

```powershell
cd website
npm ci
npm run dev -- --hostname 127.0.0.1 --port 4317
```

- 中文首页：<http://127.0.0.1:4317/>
- 中文手册：<http://127.0.0.1:4317/docs/>
- English: <http://127.0.0.1:4317/en/>
- English guide: <http://127.0.0.1:4317/en/docs/>

右上角语言切换保留当前文章；主题选择由浏览器保留。搜索在本机读取静态全文索引，支持中文关键词、英文词组、Ctrl+K、Tab、Enter 和 Esc。无后端账户、分析器、外部字体或 AI 网络调用。

## 构建与检查

```powershell
npm run types:check
npm run format:check
npm run build
npm run check:static
npm run package:static
npm start -- --listen tcp://127.0.0.1:4317 --no-clipboard
```

生产发布：在 GitHub Actions 手动运行 **Deploy documentation**（`main`），由自托管 Windows runner 构建并部署到现有服务器。配置、核验与回滚见 [部署说明](deploy/README.md)。

构建输出为 `out/`，可交给支持目录索引的普通静态托管。开发缓存使用 `.next-dev/`，构建缓存使用 `.next/`；两者隔离，均不入 Git。发布产物也不入 Git。

`postbuild` 自动运行 `scripts/normalize-export-segments.mjs`，修正 Next.js 16.4.2 在 Windows 导出时生成的嵌套 segment 文件名，使其与浏览器请求的点分文件名一致。脚本仅处理 `out/` 内的生成文件；其他平台已正确导出的路径保持原样。

正式域名为 `https://rmcl.richasy.net`，公开页面允许索引，提供每页 canonical、中英语言对应、`robots.txt` 和 56 个页面的 `sitemap.xml`。没有虚构安装包下载地址或官方授权状态。

`package:static` 仅从 `out/` 复制浏览器需要的静态文件到独立的 `build/publish-*` 目录，排除 `.map`，拒绝源码映射引用、符号链接与意外文件类型，并为文本资源生成 gzip。逐文件 SHA256 清单在发布目录之外，不对外提供。服务器直接由 Nginx 读取文件，不运行 Node.js；域名、证书、缓存、续期与回滚说明见 [部署维护](deploy/README.md)。

## 内容与界面

- Fumadocs Core + MDX 负责内容加载、目录和正文编译，Next.js 生成静态页面。
- 首页、文档三栏、移动目录、全文搜索、主题和阅读样式全部为本站实现；未加载 Fumadocs 默认 UI 或主题。
- `content/docs/`：27 篇中文文章；`content/en/docs/`：27 篇对应英文文章。
- `lib/navigation.ts`：统一目录与同页语言映射。
- `components/home-page.tsx`：PoC 首页、四个重点入口与功能截图图库。
- `components/documentation-page.tsx`：阅读页；`components/site-shell.tsx`：导航、搜索、主题和本页目录。
- `app/global.css`：沿用暖白 / 深绿、方角和薄浮雕的完整样式。
- [内容覆盖与检查记录](CONTENT-PLAN.md)、[图片来源与压缩记录](ASSETS.md)。

内容涵盖 AI 与游戏内对话、皮肤/披风、种子探索、实例、截图管理、服务器、联机房间、模组与整合包。安装、账户、Java/内存/网络、世界、备份、故障和日志各有对应入口。内置模组仅列Rodel AI、World Reroll和BetterTools，Rodel UI已撤下；当前发行版暂不提供本地推理入口。平台说明统一为 Windows 10 build 19041 及以上、x64/ARM64；截图来自 x64 客户端。

## 展示图像

截图来自实际安装的原生App，使用隔离实例、内容、皮肤工程、模型响应与联机展示状态；不使用私人存档或对话。通过PID/HWND、Windows UIA pattern与Sprout DevTools采集，不使用坐标点击。客户区按真实窗口几何裁切，常规全窗图缩放为1600×1000 WebP；软件UI不经绘制或合成。

正文配图来自演示环境，点击可查看原始分辨率；主题插画为装饰素材。图像来源见[ASSETS](ASSETS.md)，维护约定见[CONTENT-PLAN](CONTENT-PLAN.md)。采集脚本、模拟数据与客户端包不作为网站运行依赖。

首页与正文的本地图像在构建渲染时自动附带内容 SHA256 查询参数。更新截图会生成新的 URL，避开浏览器中仍有效的旧图片缓存；源文件中的路径保持可读。`lib/image-assets.mjs`、MDX 图片组件与首页共用这一处理。

## 申请目的与当前边界

网站用于说明项目用途、可用功能、身份认证与数据去向，为后续 Minecraft 第三方应用服务访问申请准备公开资料。具体提交内容以当时官方表单和真实申请状态为准，不能从文档内容推定审核会通过。

参考：[Fumadocs 自定义 UI](https://www.fumadocs.dev/docs/guides/customize-ui)、[Minecraft Usage Guidelines](https://www.minecraft.net/en-us/usage-guidelines)、[Minecraft Java Edition Support](https://help.minecraft.net/hc/en-us/sections/44460521913485)。服务申请条件与状态以官方实际表单为准。

文档支持 GitHub 风格的 `> [!NOTE]` / `TIP` / `IMPORTANT` / `WARNING` / `CAUTION` 提示，统一由 `lib/remark-alerts.mjs` 编译为本站的提示块。反馈优先使用[仓库Issues](https://github.com/Richasy/Rodel.MinecraftLauncher/issues)。反馈入口不会自动发送消息。网站经明确授权后通过Deploy documentation发布，实际部署状态以对应工作流结果为准。
