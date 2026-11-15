/* Built-in wallpaper catalog: six scenery and six player themes per appearance. */
const wallpaperCatalog = [
  {
    "id": "light-desert-temple",
    "name": "沙漠神殿",
    "theme": "light",
    "image": "assets/wallpapers/light-desert-temple-centered.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "light-cherry-meadow",
    "name": "樱花草甸",
    "theme": "light",
    "image": "assets/wallpapers/light-cherry-meadow-inhabited.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "light-dappled-autumn",
    "name": "斑驳秋林",
    "theme": "light",
    "image": "assets/wallpapers/light-dappled-autumn-inhabited.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "light-snow-peaks",
    "name": "雪峰冰湖",
    "theme": "light",
    "image": "assets/wallpapers/light-snow-peaks-chalet.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "light-tropical-bay",
    "name": "热带海湾",
    "theme": "light",
    "image": "assets/wallpapers/light-tropical-bay.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "light-redrock-savanna",
    "name": "赤岩高原",
    "theme": "light",
    "image": "assets/wallpapers/light-redrock-savanna.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "dark-river-valley",
    "name": "河谷",
    "theme": "dark",
    "image": "assets/river-valley.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "dark-lava-canyon",
    "name": "熔火峡谷",
    "theme": "dark",
    "image": "assets/wallpapers/dark-lava-canyon.png",
    "position": "center center",
    "dimension": "下界"
  },
  {
    "id": "dark-luminous-cavern",
    "name": "幽光洞庭",
    "theme": "dark",
    "image": "assets/wallpapers/dark-luminous-cavern.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "dark-mangrove-water",
    "name": "红树夜泊",
    "theme": "dark",
    "image": "assets/wallpapers/dark-mangrove-water.png",
    "position": "center center",
    "dimension": "主世界"
  },
  {
    "id": "dark-warped-valley",
    "name": "诡异幽谷",
    "theme": "dark",
    "image": "assets/wallpapers/dark-warped-valley.png",
    "position": "center center",
    "dimension": "下界"
  },
  {
    "id": "dark-end-islands",
    "name": "末地浮岛",
    "theme": "dark",
    "image": "assets/wallpapers/dark-end-islands-dragon.png",
    "position": "center top",
    "dimension": "末地"
  },
  {
    "id": "light-technical-world-eater",
    "name": "生电 · 世吞开机",
    "theme": "light",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/light-technical-world-eater.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "dark-technical-iron-farm",
    "name": "生电 · 夜班刷铁",
    "theme": "dark",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/dark-technical-iron-farm.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "light-parkour-ruins",
    "name": "跑酷 · 再远一格",
    "theme": "light",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/light-parkour-ruins.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "dark-parkour-brittle-ice",
    "name": "跑酷 · 别停下",
    "theme": "dark",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/dark-parkour-brittle-ice.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "light-farming-harvest",
    "name": "种田 · 丰收时刻",
    "theme": "light",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/light-farming-harvest.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "dark-farming-night",
    "name": "种田 · 午夜访客",
    "theme": "dark",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/dark-farming-night.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "light-pvp-bedwars",
    "name": "竞技 · 窄桥攻防",
    "theme": "light",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/light-pvp-bedwars.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "dark-pvp-duel",
    "name": "竞技 · 读懂下一招",
    "theme": "dark",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/dark-pvp-duel.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "light-industry-aeronautics",
    "name": "航空学 · 首次升空",
    "theme": "light",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/light-industry-aeronautics.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "dark-industry-create",
    "name": "机械动力 · 夜班开工",
    "theme": "dark",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/dark-industry-create.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "light-adventure-discovery",
    "name": "冒险 · 共同发现",
    "theme": "light",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/light-adventure-discovery.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  },
  {
    "id": "dark-adventure-cavern",
    "name": "冒险 · 我来照路",
    "theme": "dark",
    "image": "../src/Rodel.MinecraftLauncher.App/Assets/Backgrounds/dark-adventure-cavern.jpg",
    "position": "center center",
    "dimension": "玩家主题"
  }
];
