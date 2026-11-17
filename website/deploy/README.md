# RMCL 静态站部署

正式地址：<https://rmcl.richasy.net/>。服务器使用现有 Ubuntu Nginx 与 Certbot，没有新增 Node.js 服务或数据库。

## 文件与配置

- `/home/www/rmcl/releases/<release-id>/`：每次发布的纯静态文件和 gzip 副本。
- `/home/www/rmcl/current`：指向当前 release 的符号链接，Nginx 的公开根目录。
- `/home/www/rmcl/ops/`：配置备份，不在公开根目录。
- `/home/www/rmcl/verification/microsoft-identity-association.json`：按需托管的微软发行者域验证文件，独立于静态 release，不把验证文件加入网站源码或客户端安装包；正式客户端内置公开Client ID是另一项发布配置。
- `/etc/nginx/sites-available/rmcl.conf`：本站配置，对应本目录的 `nginx.conf`。
- `/etc/nginx/sites-enabled/rmcl.conf`：启用配置的符号链接。
- `/etc/letsencrypt/live/rmcl.richasy.net/`：Certbot 管理的证书，私钥只留在服务器。
- `/etc/letsencrypt/renewal-hooks/deploy/rmcl-nginx-reload`：对应本目录同名脚本，只在本站证书更新时检查并重载 Nginx。

## 发布内容

仓库不再提供网站部署 workflow；当前 Actions 仅提供 GitHub Release EXE 发布。网站源码、静态构建检查和部署脚本保留，线上站点及 `website-production` 环境配置继续保留。

独立部署环境使用 Node.js 24，依次执行锁定依赖安装、类型与格式检查、静态构建和 `check:static`，再运行 `node scripts/deploy.mjs`。该脚本要求真实的 `GITHUB_REF=refs/heads/main`、`GITHUB_SHA`、`GITHUB_RUN_ID` 和 `GITHUB_RUN_ATTEMPT`，不能直接在缺少执行元数据的本地终端运行。检查覆盖中英文文章配对、内部页面和锚点、图片原文件及内容指纹。随后打包纯静态产物，通过固定主机密钥的 SSH 上传，服务器核对归档 SHA256 后原子切换。每个版本包含公开的 `deployment.json`，记录提交、run、attempt 和发布时间。上线检查遍历 sitemap 的全部页面，并逐字节核对正文与首页引用的所有图片，同时确认不存在的页面、环境文件及 source map 返回 404；检查失败会切回上一版。

以下为部署脚本的环境契约，由仓库管理员维护；当前两个打包 workflow 不读取这些配置：

| 类型     | 名称                                    | 用途                                |
| -------- | --------------------------------------- | ----------------------------------- |
| Secret   | `WEBSITE_SSH_KEY`                       | 专用 `rmcl-deploy` 账号的 SSH 私钥  |
| Secret   | `WEBSITE_SSH_KNOWN_HOSTS`               | 从服务器核实的 OpenSSH 主机公钥记录 |
| Variable | `WEBSITE_SSH_HOST` / `WEBSITE_SSH_PORT` | SSH 目标地址与端口                  |
| Variable | `WEBSITE_SSH_USER`                      | 专用部署账号                        |
| Variable | `WEBSITE_DEPLOY_ROOT`                   | `/home/www/rmcl`                    |
| Variable | `WEBSITE_URL`                           | `https://rmcl.richasy.net`          |

账号没有 sudo 权限；发布不改 Nginx、证书、微软域验证文件或其他网站。临时上传位于 `incoming/`，通过线上检查后清理；旧 release 保留。发布日志记录当前及上一个 release；执行环境提供 `GITHUB_STEP_SUMMARY` 时也写入摘要。SSH 私钥只存入受控部署环境，运行时写入临时目录并在结束时删除；不要将账号私钥或服务器证书加入 Git。

部署后的 HTTPS 检查使用 Windows 自带的 `curl.exe` 访问正式域名并保留完整证书校验，runner 同时需要 OpenSSH 的 `ssh`／`scp` 和 `tar` 命令。

需要回滚时，将 `current` 原子切回发布日志记录的上一版。独立部署环境须串行执行发布，不能同时修改 `current`；仓库当前没有可重新触发的网站部署工作流。

在 `website/` 执行：

```powershell
npm ci
npm run build
npm run package:static
```

打包命令输出唯一的 `build/publish-*` 目录、文件数、体积和独立 SHA256 清单。只上传发布目录内的文件；清单、源码、`node_modules`、`.next`、环境文件、采集素材及本地连接凭据不上传到公开目录。生产浏览器 source map 显式关闭，打包时再次排除 `.map`，Nginx 同时拒绝对应 URL。

先上传到新的 release 目录，核对清单及文件权限（目录 755、文件 644），再在同一文件系统内以临时符号链接加 `mv -T` 原子切换 `current`。保留旧 release；回滚时将 `current` 原子切回。不要原地清空正在提供服务的目录。

配置变更前备份本站配置，执行 `sudo nginx -t`，成功后才执行 `sudo systemctl reload nginx`。只管理本站文件，保留服务器其他虚拟主机和全局配置。静态更新不需要重启服务。

## 传输与缓存

- Nginx `gzip_static on` 优先发送预压缩文本，避免每次请求重新压缩；不支持 gzip 的客户端仍可取得原文件。
- 带内容哈希的 `/_next/static/` 文件缓存一年并标记 immutable；截图和图标缓存七天。首页和正文图片的 URL 自动附带内容指纹，截图更新后会使用新 URL。
- 页面、搜索索引和静态导航数据使用 `no-cache`，每次验证缓存，避免更新后继续引用旧页面。
- HTTP 跳转 HTTPS；ACME HTTP-01 验证目录单独保留，不被跳转或隐藏文件规则拦截。
- 缺失页面返回真实 404；没有将全部请求回退为首页。

文件数量和体积以每次打包输出为准。压缩副本与未压缩文件并存会增加磁盘占用；这里的体积不代表一次访问会下载整站，也不是端到端加载时间测量。

## 微软发行者域验证

HTTPS仅为`/.well-known/microsoft-identity-association.json`提供精确路径例外，映射到上述verification目录，返回UTF-8的`application/json`；其他隐藏路径继续404。验证文件的`associatedApplications`数组按微软格式列出获批应用的公开`applicationId`，由本机配置在部署时生成，不在Git保存实际值。更新时保留已有应用条目，临时写入后原子替换；Nginx配置先备份、`nginx -t`通过后才reload。

外网检查HTTP 200、JSON类型与目标应用ID后，在Entra应用注册的“品牌和属性 → 发布者域”点击“验证并保存域”；微软目前不提供REST/PowerShell设置publisherDomain的接口。完成后读回publisherDomain确认生效。微软允许验证完成后删除临时验证JSON；保留或移除不改变已验证的域属性，后续重新验证时按需恢复。

发行者域验证与“已验证发布者”认证不同。后者还要求通过业务验证的Microsoft AI Cloud Partner Program全局合作伙伴账户（PGA）Partner One ID、相应租户关联/域与角色、MFA等条件；Microsoft Store Publisher ID或地区账户（PLA）ID不能替代。参见[发行者域配置](https://learn.microsoft.com/en-us/entra/identity-platform/howto-configure-publisher-domain)与[发布者验证要求](https://learn.microsoft.com/en-us/entra/identity-platform/publisher-verification-overview)。

## 证书续期

使用已有的 Certbot 账户和 webroot 插件，验证根目录为 `/var/lib/letsencrypt`。证书路径和续期配置由 Certbot 管理，不把证书或账户资料复制到仓库。

服务器已有且已启用的 `certbot.timer` 定期执行续期；不要再叠加另一份 cron。成功更新本站证书后，部署 hook 检查配置并重载 Nginx，使新证书生效。手工检查：

```bash
sudo nginx -t
systemctl status certbot.timer
systemctl list-timers certbot.timer
sudo certbot renew --cert-name rmcl.richasy.net --dry-run --no-random-sleep-on-renew
```

证书有效期与续期结果以实时证书和 Certbot 状态为准。

参考：[Nginx 预压缩文件](https://nginx.org/en/docs/http/ngx_http_gzip_static_module.html)、[Certbot 自动续期与部署 hook](https://eff-certbot.readthedocs.io/en/stable/using.html#automated-renewals)。
