/* Read-only design reference. Package version is a sample, not an update check. */
const aboutRepository = 'https://github.com/Richasy/Rodel.MinecraftLauncher';
const aboutMit = 'https://licenses.nuget.org/MIT';
const aboutApache = 'https://licenses.nuget.org/Apache-2.0';
const aboutQuery = new URLSearchParams(location.search);
if (aboutQuery.get('page') === 'settings' && aboutQuery.get('topic') === 'about') settingsSection = 'about';
const aboutProjects = [
  ['Sprout', 'https://github.com/Richasy/Sprout', 'MIT', aboutMit],
  ['.NET', 'https://github.com/dotnet/runtime', 'MIT', aboutMit],
  ['Windows App SDK', 'https://github.com/microsoft/WindowsAppSDK', 'Microsoft 条款', '../src/Rodel.MinecraftLauncher.App/Assets/Licenses/WindowsAppSDK.txt'],
  ['MSAL.NET', 'https://github.com/AzureAD/microsoft-authentication-library-for-dotnet', 'MIT', aboutMit],
  ['MSAL Native Interop', 'https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop', 'Microsoft 条款', '../src/Rodel.MinecraftLauncher.App/Assets/Licenses/MsalNativeInterop.txt'],
  ['TerraFX.Interop.Windows', 'https://github.com/terrafx/terrafx.interop.windows', 'MIT', aboutMit],
  ['Microsoft.Extensions.AI', 'https://github.com/dotnet/extensions', 'MIT', aboutMit],
  ['MCP C# SDK', 'https://github.com/modelcontextprotocol/csharp-sdk', 'Apache-2.0', aboutApache],
  ['Microsoft.ML.Tokenizers', 'https://github.com/dotnet/machinelearning', 'MIT', aboutMit],
  ['Google.Protobuf', 'https://github.com/protocolbuffers/protobuf', 'BSD-3-Clause', 'https://licenses.nuget.org/BSD-3-Clause'],
  ['models.dev', 'https://models.dev/', 'MIT', 'https://github.com/anomalyco/models.dev/blob/dev/LICENSE'],
  ['StbImageSharp', 'https://github.com/StbSharp/StbImageSharp', 'Public Domain / MIT', 'https://github.com/StbSharp/StbImageSharp#license'],
  ['StbImageWriteSharp', 'https://github.com/StbSharp/StbImageWriteSharp', 'Public Domain', 'https://github.com/StbSharp/StbImageWriteSharp#license'],
  ['Humanizer', 'https://github.com/Humanizr/Humanizer', 'MIT', aboutMit],
  ['YamlDotNet', 'https://github.com/aaubry/YamlDotNet', 'MIT', aboutMit],
  ['Terracotta', 'https://github.com/burningtnt/Terracotta/tree/v0.4.2', 'AGPL-3.0-or-later', 'https://github.com/burningtnt/Terracotta/blob/v0.4.2/LICENSE'],
  ['rust-mcp-filesystem', 'https://github.com/rust-mcp-stack/rust-mcp-filesystem', 'MIT', 'https://github.com/rust-mcp-stack/rust-mcp-filesystem/blob/ef4797360ea03eec5375e03a6f10d092dfc6a5e0/LICENSE'],
  ['mcp-retrieval', 'https://github.com/Role1776/mcp-retrieval', 'MIT', 'https://github.com/Role1776/mcp-retrieval/blob/98eadccee4f374d7c178e9bcab82b3a906b84efe/LICENSE'],
  ['doh-mcp', 'https://github.com/99apps-id/doh-mcp', 'Apache-2.0', 'https://github.com/99apps-id/doh-mcp/blob/cb8e483dc06435bf7f5cddb2caac68f4cbae3d80/LICENSE'],
  ['Fabric API', 'https://github.com/FabricMC/fabric-api', 'Apache-2.0', 'https://github.com/FabricMC/fabric-api/blob/26.3/LICENSE'],
  ['CsWin32', 'https://github.com/microsoft/CsWin32', 'MIT', aboutMit],
  ['C#/WinRT', 'https://github.com/microsoft/CsWinRT', 'MIT', aboutMit],
  ['Roslyn', 'https://github.com/dotnet/roslyn', 'MIT', aboutMit],
  ['Roslynator', 'https://github.com/dotnet/roslynator', 'Apache-2.0', aboutApache],
];
function aboutSettingsMarkup() {
  const link = (url, label, attributes = '') => `<a href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" ${attributes}>${escapeHtml(label)}</a>`;
  return `<section class="settings-about" aria-label="关于此应用">
    <header class="about-app-header">
      <img class="about-app-icon" src="../src/Rodel.MinecraftLauncher.App/Assets/Icons/launcher.png" alt="">
      <div class="about-app-identity"><h2>RMCL</h2><p>面向 Windows 的 Minecraft Java 版启动器</p><div class="about-app-meta"><bdi>0.2611.1.0</bdi><button class="about-copy" data-about-copy data-setting-id="settings.about.copy-version" aria-label="复制版本信息" title="复制版本信息">${vectorIcon('copy')}</button>${link('https://github.com/Richasy', 'Richasy')}<span>免费使用</span></div></div>
      <div class="about-publisher">${link('ms-windows-store://publisher/?name=' + encodeURIComponent('云之幻'), '开发者的其他应用 ↗', 'class="about-store" data-setting-id="settings.about.developer-apps"')}<small>云之幻 · Microsoft Store</small></div>
    </header>
    <nav class="about-quick-links" aria-label="项目与帮助">
      <a href="${aboutRepository}" target="_blank" rel="noopener noreferrer" data-setting-id="settings.about.repository"><strong>GitHub 仓库 ↗</strong><small>查看项目与更新</small></a>
      <a href="https://rmcl.richasy.net/docs/start/install/" target="_blank" rel="noopener noreferrer"><strong>使用指南 ↗</strong><small>从第一次启动开始</small></a>
      <a href="${aboutRepository}/issues" target="_blank" rel="noopener noreferrer" data-setting-id="settings.about.feedback"><strong>问题与建议 ↗</strong><small>在 GitHub 上反馈与交流</small></a>
    </nav>
    <section class="about-acknowledgments" data-setting-id="settings.about.credits">
      <h3>技术与致谢</h3><p>感谢这些项目及其贡献者。</p>
      <ul>${aboutProjects.map(([name, project, license, terms]) => `<li><span>${link(project, name + ' ↗')}</span>${link(terms, license, `class="about-license" aria-label="${escapeHtml(name)}的许可证：${escapeHtml(license)}"`)}</li>`).join('')}</ul>
      <p class="about-attribution">Terracotta © Burning_TNT and contributors · ${link('https://github.com/burningtnt/Terracotta/tree/v0.4.2#许可', '上游集成例外')}</p>
    </section>
    <footer class="about-legal"><div><span>© Richasy · RMCL</span>${link('https://rmcl.richasy.net/docs/project/privacy/', '隐私说明', 'data-setting-id="settings.about.privacy"')}${link('https://rmcl.richasy.net/docs/project/terms/', '使用说明与条款', 'data-setting-id="settings.about.terms"')}</div><p>RMCL 是独立项目，与 Mojang 或 Microsoft 无隶属关系。Minecraft 及相关标识属于其各自权利人。</p></footer>
  </section>`;
}
document.addEventListener('click', async event => {
  if (!event.target.closest('[data-about-copy]')) return;
  try {
    await navigator.clipboard.writeText(`RMCL 0.2611.1.0\n${aboutRepository}`);
    showToast('版本信息已复制。');
  } catch {
    showToast('无法访问剪贴板，请稍后重试。');
  }
});
