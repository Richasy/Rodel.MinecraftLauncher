import Link from "next/link";
import { ArrowRight, ArrowUpRight, BookOpen, Monitor } from "lucide-react";
import { localized, type Locale } from "@/lib/navigation";
import { versionedAssetUrl } from "@/lib/image-assets.mjs";
import { StoreBadge } from "@/components/store-badge";
const topics = [
  {
    icon: "ai",
    slug: "create/assistant",
    zh: "AI 辅助",
    en: "AI assistance",
    description:
      "先问清楚，再动手。读懂错误、寻找模组、了解当前实例，也探索对游戏内操作的帮助。",
    english:
      "Ask questions, understand errors and find suitable content. Explore assistance that works with your game context.",
  },
  {
    icon: "skin",
    slug: "create/skins",
    zh: "皮肤设计器",
    en: "The skin designer",
    description:
      "从模板或现有皮肤开始，在 3D 和平面视图里绘制，用图层和历史慢慢调整，也可以请 AI 帮忙。",
    english:
      "Start from a template or an existing skin. Paint in 3D or 2D, work with layers and history, and ask AI for help.",
  },
  {
    icon: "instances",
    slug: "play/instances",
    zh: "实例与联机",
    en: "Instances & multiplayer",
    description:
      "把不同版本和玩法分开放。迁入已有游戏、收藏服务器，或创建房间邀请朋友一起玩。",
    english:
      "Keep versions and play styles separate. Import games, save servers or host a room for friends.",
  },
  {
    icon: "mods",
    slug: "play/content",
    zh: "模组与整合包",
    en: "Mods & modpacks",
    description:
      "搜索 Modrinth，添加本地文件或导入整合包。核对版本、依赖和安装清单后再应用。",
    english:
      "Search Modrinth, add local files or import a modpack. Review versions and dependencies before applying changes.",
  },
];
const screens = [
  {
    file: "multiplayer-room",
    zh: "和朋友联机",
    en: "Play with friends",
    slug: "play/multiplayer",
  },
  {
    file: "seed-map",
    zh: "种子探索",
    en: "Seed explorer",
    slug: "create/seed-explorer",
  },
  {
    file: "assistant",
    zh: "AI 助手",
    en: "AI assistant",
    slug: "create/assistant",
  },
  {
    file: "skins",
    zh: "皮肤设计器",
    en: "Skin designer",
    slug: "create/skins",
  },
  {
    file: "instances",
    zh: "实例管理",
    en: "Instance management",
    slug: "play/instances",
  },
  {
    file: "servers",
    zh: "服务器管理",
    en: "Server management",
    slug: "play/servers",
  },
  {
    file: "content",
    zh: "模组与内容",
    en: "Mods & content",
    slug: "play/content",
  },
  {
    file: "settings",
    zh: "外观与设置",
    en: "Preferences",
    slug: "create/settings",
  },
];
export function HomePage({ locale }: { locale: Locale }) {
  const en = locale === "en",
    t = (zh: string, eng: string) => (en ? eng : zh),
    url = (href: string) => localized(href, locale);
  return (
    <main id="main-content">
      <section className="home-hero poc-hero">
        <div className="hero-grid" aria-hidden="true" />
        <div className="hero-topline">
          <span>
            <span className="status-dot" /> PROOF OF CONCEPT
          </span>
          <Link href={url("/docs/project/about")}>
            {t("实验项目 · 持续开发", "Experimental · in development")}
            <ArrowUpRight size={13} />
          </Link>
        </div>
        <div className="hero-layout">
          <div className="hero-copy">
            <div className="eyebrow">
              <span className="pixel-plus" /> RMCL / MINECRAFT JAVA EDITION
            </div>
            <h1>
              RMCL
              <br />
              Minecraft Launcher<span className="poc-label">PoC</span>
            </h1>
            <p>
              {t(
                "用 Minecraft 启动器验证 Sprout 原生 UI 框架。",
                "A Minecraft launcher for exploring the Sprout native UI framework.",
              )}
              <br />
              {t(
                "也试着用 AI，帮刚接触游戏的玩家少卡几步。",
                "Also an experiment in making AI useful for new players.",
              )}
              <br />
              {t(
                "感兴趣的话，可以从这份手册慢慢了解。",
                "If you are curious, the guide is a good place to start.",
              )}
            </p>
            <div className="hero-buttons">
              <StoreBadge locale={locale} />
              <Link className="button" href={url("/docs")}>
                <BookOpen size={17} />
                {t("阅读使用手册", "Read the guide")}
                <ArrowRight size={17} />
              </Link>
            </div>
            <div className="hero-notes">
              <span>
                <Monitor size={14} />
                Windows 10 19041+
              </span>
              <span>x64 / ARM64</span>
            </div>
          </div>
          <div className="hero-product">
            <div className="product-caption">
              <span className="small-square" /> NATIVE WINDOWS APP
              <span>SPROUT UI</span>
            </div>
            <Link
              href={url("/docs/start/first-launch")}
              className="product-frame"
              aria-label={t("查看首次启动指南", "Read the first launch guide")}
            >
              <img
                src={versionedAssetUrl(`/assets/${locale}-home.webp`)}
                width="1600"
                height="1000"
                alt={t(
                  "RMCL 启动器首页，包含不同图标的游戏实例",
                  "RMCL home with several game instances and distinct icons",
                )}
                fetchPriority="high"
              />
            </Link>
            <div className="product-bottom">
              <span>{t("首页 · 游戏与实例", "Home · games & instances")}</span>
              <span>
                {t("原生客户端画面", "Native application")}
                <span className="inline-pixels">▪ ▪ ▪</span>
              </span>
            </div>
          </div>
        </div>
        <div className="hero-foot">
          <span>
            {t(
              "免费使用，仅通过 Microsoft Store 获取；源码计划后续公开。",
              "Free, exclusively on Microsoft Store. Source release is planned.",
            )}
          </span>
          <a href="#topics" className="scroll-hint">
            {t("查看功能与指南", "Explore the guide")}
            <span>↓</span>
          </a>
        </div>
      </section>
      <section className="content-width intro-section">
        <div className="start-note">
          <BookOpen size={22} />
          <div>
            <h2>{t("第一次接触 Minecraft？", "New to Minecraft?")}</h2>
            <p>
              {t(
                "先了解账户、实例和游戏版本，再完成第一次启动。无需先学命令行或调整复杂参数。",
                "Start with accounts, instances and game versions, then launch your first game. No command line or advanced tuning needed.",
              )}
            </p>
          </div>
          <Link href={url("/docs/start/first-launch")}>
            {t("从第一步开始", "Start here")}
            <ArrowRight size={16} />
          </Link>
        </div>
      </section>
      <section className="path-section content-width poc-topics" id="topics">
        <div className="section-heading">
          <div>
            <span className="eyebrow">FEATURES & GUIDES</span>
            <h2>{t("现在能做什么", "What you can explore")}</h2>
          </div>
          <Link className="text-link" href={url("/docs")}>
            {t("全部使用文档", "All guides")}
            <ArrowRight size={15} />
          </Link>
        </div>
        <div className="topic-grid">
          {topics.map((topic) => (
            <Link
              className="topic-card"
              key={topic.slug}
              href={url("/docs/" + topic.slug)}
            >
              <img
                src={versionedAssetUrl(`/assets/topics/${topic.icon}.webp`)}
                alt=""
                width="64"
                height="64"
              />
              <div>
                <h3>
                  {en ? topic.en : topic.zh}
                  <ArrowUpRight size={17} />
                </h3>
                <p>{en ? topic.english : topic.description}</p>
              </div>
            </Link>
          ))}
        </div>
      </section>
      <section className="screenshot-section" id="screenshots">
        <div className="content-width">
          <div className="section-heading">
            <div>
              <span className="eyebrow">A LOOK INSIDE</span>
              <h2>{t("在客户端里看看", "Inside the application")}</h2>
            </div>
            <p>
              {t(
                "从页面进入对应指南。",
                "Open a screenshot to read its guide.",
              )}
            </p>
          </div>
          <div className="screenshot-grid">
            {screens.map((screen) => (
              <Link
                href={url("/docs/" + screen.slug)}
                key={screen.file}
                className="screenshot-card"
              >
                <div className="screenshot-image">
                  <img
                    src={versionedAssetUrl(
                      `/assets/${locale}-${screen.file}.webp`,
                    )}
                    alt={en ? screen.en : screen.zh}
                    width="1600"
                    height="1000"
                    loading="lazy"
                  />
                </div>
                <div>
                  <span>{en ? screen.en : screen.zh}</span>
                  <ArrowUpRight size={16} />
                </div>
              </Link>
            ))}
          </div>
        </div>
      </section>
      <section className="content-width direction-section">
        <div>
          <span className="eyebrow">WHERE THIS IS GOING</span>
          <h2>{t("接下来继续试什么", "What we are working toward")}</h2>
        </div>
        <div>
          <p>
            {t(
              "这个项目首先是一个 UI 框架的验证场，也会继续探索 AI 对新手玩家的实际帮助：解释问题、协助配置、辅助皮肤创作，以及在明确权限下与游戏交互。",
              "This project is a testing ground for a UI framework. A continuing direction is practical AI help for new players: explaining problems, assisting with setup and skin creation, and interacting with the game within explicit permissions.",
            )}
          </p>
          <p>
            {t(
              "从一个具体任务开始：一起玩朋友的世界、找一个喜欢的出生点，或为自己的角色设计一件披风。手册里有对应的入口、步骤和画面。",
              "Start with a task: join a friend's world, find a spawn you like, or design a cape for your character. The guide shows where to go and what to do.",
            )}
          </p>
          <Link className="text-link" href={url("/docs/project/about")}>
            {t("项目说明与当前边界", "Project notes & current scope")}
            <ArrowRight size={15} />
          </Link>
        </div>
      </section>
      <section className="quick-section content-width">
        <div>
          <span className="eyebrow">WHEN YOU NEED IT</span>
          <h2>{t("遇到问题时", "When you need help")}</h2>
        </div>
        <div className="quick-links">
          <Link href={url("/docs/help/launch")}>
            <span>{t("游戏无法启动", "The game will not start")}</span>
            <ArrowUpRight size={18} />
          </Link>
          <Link href={url("/docs/play/worlds")}>
            <span>{t("管理与备份世界", "Worlds & backups")}</span>
            <ArrowUpRight size={18} />
          </Link>
          <Link href={url("/docs/project/privacy")}>
            <span>{t("数据与隐私", "Data & privacy")}</span>
            <ArrowUpRight size={18} />
          </Link>
          <Link href={url("/docs/project/microsoft")}>
            <span>{t("微软登录与应用状态", "Microsoft sign-in status")}</span>
            <ArrowUpRight size={18} />
          </Link>
        </div>
      </section>
    </main>
  );
}
