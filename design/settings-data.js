/* Review snapshot of the explicitly selected CorePreferenceFields; no reflection or runtime Core dependency. */
const preferenceFields = [
  {
    "key": "fullscreen",
    "id": "settings.field.fullscreen",
    "category": "launch",
    "title": "全屏启动",
    "description": "",
    "kind": "Boolean",
    "value": "false",
    "minimum": 1
  },
  {
    "key": "width",
    "id": "settings.number.width",
    "category": "launch",
    "title": "窗口宽度",
    "description": "",
    "kind": "Integer",
    "value": "1280",
    "minimum": 1
  },
  {
    "key": "height",
    "id": "settings.number.height",
    "category": "launch",
    "title": "窗口高度",
    "description": "",
    "kind": "Integer",
    "value": "720",
    "minimum": 1
  },
  {
    "key": "quickPlaySingleplayer",
    "id": "settings.input.quickPlaySingleplayer",
    "category": "launch",
    "title": "直接进入世界",
    "description": "填写存档文件夹名；留空进入主菜单。不支持快速进入的游戏版本仍正常启动。",
    "kind": "Text",
    "value": "",
    "minimum": 1
  },
  {
    "key": "gameBridgeEnabled",
    "id": "settings.field.gameBridgeEnabled",
    "category": "launch",
    "title": "游戏数据桥",
    "description": "开启后可读取游戏信息、操作本地角色；服务器只读，更改在下次启动时生效。",
    "kind": "Boolean",
    "value": "true",
    "minimum": 1
  },
  {
    "key": "javaPath",
    "id": "settings.input.javaPath",
    "category": "java",
    "title": "Java 可执行文件路径（可留空）",
    "description": "留空自动发现 Java。实例可单独指定自己的运行时。",
    "kind": "Text",
    "value": "",
    "minimum": 1
  },
  {
    "key": "memoryMode",
    "id": "settings.field.memoryMode",
    "category": "java",
    "title": "内存分配",
    "description": "",
    "kind": "MemoryMode",
    "value": "auto",
    "minimum": 1
  },
  {
    "key": "minMemoryMb",
    "id": "settings.number.minMemoryMb",
    "category": "java",
    "title": "初始内存（MB）",
    "description": "",
    "kind": "Integer",
    "value": "512",
    "minimum": 1
  },
  {
    "key": "maxMemoryMb",
    "id": "settings.number.maxMemoryMb",
    "category": "java",
    "title": "最大内存（MB）",
    "description": "",
    "kind": "Integer",
    "value": "2048",
    "minimum": 1
  },
  {
    "key": "memoryReserveMb",
    "id": "settings.number.memoryReserveMb",
    "category": "java",
    "title": "自动分配时预留（MB）",
    "description": "",
    "kind": "Integer",
    "value": "1024",
    "minimum": 0
  },
  {
    "key": "autoMemoryMinMb",
    "id": "settings.number.autoMemoryMinMb",
    "category": "java",
    "title": "自动分配下限（MB）",
    "description": "",
    "kind": "Integer",
    "value": "512",
    "minimum": 1
  },
  {
    "key": "autoMemoryMaxMb",
    "id": "settings.number.autoMemoryMaxMb",
    "category": "java",
    "title": "自动分配上限（MB）",
    "description": "",
    "kind": "Integer",
    "value": "16384",
    "minimum": 1
  },
  {
    "key": "network.source",
    "id": "settings.field.network.source",
    "category": "network",
    "title": "下载源",
    "description": "用于 Minecraft 游戏文件、加载器、Mojang Java 和登录组件。BMCLAPI 由 bangbang93 提供；新下载生效，镜像失败时回退原源。",
    "kind": "DownloadSource",
    "value": "official",
    "minimum": 1
  },
  {
    "key": "network.proxy",
    "id": "settings.field.network.proxy",
    "category": "network",
    "title": "网络代理",
    "description": "为启动器的下载与在线请求选择连接方式。",
    "kind": "Proxy",
    "value": "",
    "minimum": 1
  },
  {
    "key": "network.concurrency",
    "id": "settings.number.network.concurrency",
    "category": "network",
    "title": "下载并发",
    "description": "限制同时进行的下载数量。",
    "kind": "Integer",
    "value": "4",
    "minimum": 1
  },
  {
    "key": "network.connectTimeoutSeconds",
    "id": "settings.number.network.connectTimeoutSeconds",
    "category": "network",
    "title": "连接超时（秒）",
    "description": "",
    "kind": "Integer",
    "value": "20",
    "minimum": 1
  },
  {
    "key": "network.requestTimeoutSeconds",
    "id": "settings.number.network.requestTimeoutSeconds",
    "category": "network",
    "title": "请求超时（秒）",
    "description": "",
    "kind": "Integer",
    "value": "30",
    "minimum": 1
  },
  {
    "key": "network.readIdleSeconds",
    "id": "settings.number.network.readIdleSeconds",
    "category": "network",
    "title": "读取空闲超时（秒）",
    "description": "",
    "kind": "Integer",
    "value": "120",
    "minimum": 1
  },
  {
    "key": "network.metadataTimeoutSeconds",
    "id": "settings.number.network.metadataTimeoutSeconds",
    "category": "network",
    "title": "元数据超时（秒）",
    "description": "",
    "kind": "Integer",
    "value": "30",
    "minimum": 1
  },
  {
    "key": "network.mirrors",
    "id": "settings.input.network.mirrors",
    "category": "network",
    "title": "镜像规则",
    "description": "每行一条“原始地址前缀=镜像地址”。原始前缀以 / 结尾；留空使用原始来源。",
    "kind": "Mirrors",
    "value": "",
    "minimum": 1
  },
  {
    "key": "loggingCompatibility",
    "id": "settings.field.loggingCompatibility",
    "category": "logs",
    "title": "旧版游戏日志兼容",
    "description": "为旧版游戏适配日志配置，使日志能正常采集。",
    "kind": "Boolean",
    "value": "true",
    "minimum": 1
  },
  {
    "key": "stdoutEncoding",
    "id": "settings.input.stdoutEncoding",
    "category": "logs",
    "title": "标准输出编码",
    "description": "留空由 Java 与游戏版本自动选择；修改只影响后续启动。",
    "kind": "Text",
    "value": "",
    "minimum": 1
  },
  {
    "key": "stderrEncoding",
    "id": "settings.input.stderrEncoding",
    "category": "logs",
    "title": "错误输出编码",
    "description": "留空由 Java 与游戏版本自动选择；修改只影响后续启动。",
    "kind": "Text",
    "value": "",
    "minimum": 1
  },
  {
    "key": "jvmArguments",
    "id": "settings.input.jvmArguments",
    "category": "advanced",
    "title": "额外 JVM 参数",
    "description": "每行一个完整参数，参数内的空格会保留。",
    "kind": "Arguments",
    "value": "",
    "minimum": 1
  },
  {
    "key": "gameArguments",
    "id": "settings.input.gameArguments",
    "category": "advanced",
    "title": "额外游戏参数",
    "description": "每行一个完整参数，参数内的空格会保留。",
    "kind": "Arguments",
    "value": "",
    "minimum": 1
  },
  {
    "key": "environment",
    "id": "settings.input.environment",
    "category": "advanced",
    "title": "环境变量",
    "description": "每行一条“名称=值”，只应用到游戏进程。",
    "kind": "Environment",
    "value": "",
    "minimum": 1
  }
];
const settingExtras = {
  "settings.language": {
    "title": "界面语言",
    "description": "选择启动器显示的语言。",
    "keywords": "界面语言"
  },
  "settings.theme": {
    "title": "界面主题",
    "description": "跟随系统外观，或固定使用浅色、深色主题。",
    "keywords": "跟随系统 浅色 深色"
  },
  "wallpaper.light.choose": {
    "title": "壁纸",
    "description": "浅色壁纸 深色壁纸 背景"
  },
  "settings.after-launch": {
    "title": "游戏启动后",
    "description": "选择游戏开始运行后启动器窗口的行为。",
    "keywords": "最小化到系统托盘 最小化到任务栏 保持显示"
  },
  "settings.close-behavior": {
    "title": "关闭主窗口时",
    "description": "游戏或任务仍由启动器管理时，关闭窗口会转入托盘。可在托盘菜单恢复窗口；没有进行中的操作时，可选择“退出启动器”。"
  },
  "settings.data.choose": {
    "title": "应用数据目录",
    "description": "默认使用用户目录下的 .rmcl，导入的实例复制到这里。更改后重启生效，已有数据保留在原位置。"
  },
  "settings.java.manager": {
    "title": "管理 Java",
    "description": "查看本机运行时，添加、下载或设置默认 Java。",
    "keywords": "扫描 下载 添加 安装 Java 运行库 默认"
  },
  "settings.cleanup.open": {
    "title": "下载与内容缓存",
    "description": "清理过期下载及可安全回收的依赖缓存，保留游戏内容和仍被引用的文件。"
  },
  "settings.cache.clear": {
    "title": "图片缓存",
    "description": "社区图片保存在本应用的缓存目录，可清理后按需重新下载。不影响实例、模组、存档或自定义壁纸。"
  },
  "settings.diagnostics.open": {
    "title": "启动器日志",
    "description": "汇总最近7天的软件运行记录，并隐藏常见凭据。"
  },
  "settings.game-logs.open": {
    "title": "游戏日志",
    "description": "按实例选择某一次游戏运行，导出游戏日志或诊断包。"
  }
};
