export const navigation = [
  {
    title: "启程之前",
    number: "01",
    items: [
      { title: "欢迎来到 RMCL", url: "/docs" },
      { title: "安装与准备", url: "/docs/start/install" },
      { title: "第一次启动游戏", url: "/docs/start/first-launch" },
      { title: "添加与切换账户", url: "/docs/start/accounts" },
    ],
  },
  {
    title: "安顿你的世界",
    number: "02",
    items: [
      { title: "认识与管理实例", url: "/docs/play/instances" },
      { title: "导入已有游戏", url: "/docs/play/import" },
      { title: "导入整合包", url: "/docs/play/modpacks" },
      { title: "添加模组与资源包", url: "/docs/play/content" },
      { title: "管理与备份世界", url: "/docs/play/worlds" },
      { title: "浏览与管理截图", url: "/docs/play/screenshots" },
      { title: "加入服务器", url: "/docs/play/servers" },
      { title: "和朋友联机", url: "/docs/play/multiplayer" },
    ],
  },
  {
    title: "玩出自己的样子",
    number: "03",
    items: [
      { title: "绘制自己的皮肤", url: "/docs/create/skins" },
      { title: "使用 AI 助手", url: "/docs/create/assistant" },
      { title: "种子探索与世界地图", url: "/docs/create/seed-explorer" },
      { title: "调整外观与设置", url: "/docs/create/settings" },
    ],
  },
  {
    title: "内置模组",
    number: "04",
    items: [
      { title: "安装与版本选择", url: "/docs/play/built-in-mods" },
      { title: "Rodel AI：游戏内助手", url: "/docs/create/game-ai" },
      { title: "World Reroll：世界重开", url: "/docs/mods/world-reroll" },
      { title: "Better Tools：工具魔方", url: "/docs/mods/bettertools" },
    ],
  },
  {
    title: "遇到问题时",
    number: "04",
    items: [
      { title: "常见问题", url: "/docs/help/faq" },
      { title: "游戏无法启动", url: "/docs/help/launch" },
      { title: "收集日志与反馈", url: "/docs/help/feedback" },
    ],
  },
  {
    title: "关于这个项目",
    number: "05",
    items: [
      { title: "关于 RMCL 与联系", url: "/docs/project/about" },
      { title: "微软登录与应用状态", url: "/docs/project/microsoft" },
      { title: "隐私说明", url: "/docs/project/privacy" },
      { title: "使用约定", url: "/docs/project/terms" },
    ],
  },
];

export type Locale = "zh" | "en";
export function localized(url: string, locale: Locale) {
  return locale === "en" && url.startsWith("/")
    ? "/en" + (url === "/" ? "" : url)
    : url;
}
const english: Record<string, string> = {
  启程之前: "Getting started",
  安顿你的世界: "Instances & servers",
  玩出自己的样子: "Explore & create",
  遇到问题时: "Help & troubleshooting",
  关于这个项目: "About the project",
  内置模组: "Built-in mods",
  "欢迎来到 RMCL": "Welcome to RMCL",
  安装与准备: "Installation & requirements",
  第一次启动游戏: "Your first launch",
  添加与切换账户: "Accounts",
  认识与管理实例: "Manage instances",
  导入已有游戏: "Import an existing game",
  导入整合包: "Import a modpack",
  添加模组与资源包: "Mods, resource packs & shaders",
  安装与版本选择: "Installation & versions",
  "Rodel AI：游戏内助手": "Rodel AI",
  "World Reroll：世界重开": "World Reroll",
  "Better Tools：工具魔方": "Better Tools",
  管理与备份世界: "Worlds & backups",
  浏览与管理截图: "Screenshots",
  加入服务器: "Manage servers",
  和朋友联机: "Play with friends",
  绘制自己的皮肤: "The skin designer",
  "使用 AI 助手": "The AI assistant",
  种子探索与世界地图: "Seed explorer & world maps",
  调整外观与设置: "Preferences & settings",
  常见问题: "Frequently asked questions",
  游戏无法启动: "When a game will not start",
  收集日志与反馈: "Logs & feedback",
  "关于 RMCL 与联系": "About RMCL & contact",
  微软登录与应用状态: "Microsoft sign-in & app status",
  隐私说明: "Privacy",
  使用约定: "Terms of use",
};
export function getNavigation(locale: Locale) {
  const groups = [
    navigation[0],
    navigation[2],
    navigation[1],
    navigation[3],
    navigation[4],
    navigation[5],
  ];
  return groups.map((group, index) => ({
    ...group,
    number: String(index + 1).padStart(2, "0"),
    title:
      locale === "en"
        ? english[group.title]
        : group.title === "玩出自己的样子"
          ? "探索与创作"
          : group.title === "安顿你的世界"
            ? "实例与服务器"
            : group.title,
    items: group.items.map((item) => ({
      ...item,
      title: locale === "en" ? english[item.title] : item.title,
      url: localized(item.url, locale),
    })),
  }));
}
export function getAllPages(locale: Locale) {
  return getNavigation(locale).flatMap((group) =>
    group.items.map((item) => ({ ...item, group: group.title })),
  );
}
