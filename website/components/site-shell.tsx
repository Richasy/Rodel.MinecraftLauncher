"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import {
  ArrowUpRight,
  BookOpen,
  ChevronRight,
  Menu,
  Moon,
  Search,
  Sun,
  X,
  Link2,
  Check,
  Languages,
} from "lucide-react";
import { getNavigation, localized, type Locale } from "@/lib/navigation";
type Entry = {
  title: string;
  description: string;
  url: string;
  content: string;
};
export function Header() {
  const path = usePathname(),
    en = path === "/en" || path.startsWith("/en/"),
    locale: Locale = en ? "en" : "zh";
  const t = (zh: string, eng: string) => (en ? eng : zh),
    url = (href: string) => localized(href, locale);
  const [menu, setMenu] = useState(false),
    [dark, setDark] = useState(false),
    [query, setQuery] = useState(""),
    [entries, setEntries] = useState<Entry[]>([]),
    [status, setStatus] = useState("loading");
  const dialog = useRef<HTMLDialogElement>(null),
    input = useRef<HTMLInputElement>(null),
    loaded = useRef(false);
  async function openSearch() {
    if (!dialog.current?.open) dialog.current?.showModal();
    input.current?.focus();
    if (loaded.current) return;
    setStatus("loading");
    try {
      const response = await fetch(url("/search-index.json"));
      if (!response.ok) throw new Error();
      setEntries(await response.json());
      loaded.current = true;
      setStatus("ready");
    } catch {
      setStatus("error");
    }
  }
  useEffect(() => {
    setDark(document.documentElement.dataset.theme === "dark");
    const keydown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        void openSearch();
      }
    };
    document.addEventListener("keydown", keydown);
    return () => document.removeEventListener("keydown", keydown);
  }, [locale]);
  useEffect(() => {
    setMenu(false);
    dialog.current?.close();
    loaded.current = false;
    setQuery("");
  }, [path]);
  function theme() {
    const value = document.documentElement.dataset.theme !== "dark";
    setDark(value);
    document.documentElement.dataset.theme = value ? "dark" : "light";
    try {
      localStorage.setItem("rodel-docs-theme", value ? "dark" : "light");
    } catch {}
  }
  const terms = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const results = entries
    .map((entry) => ({
      ...entry,
      score: terms.reduce(
        (score, term) =>
          score +
          (entry.title.toLocaleLowerCase().includes(term) ? 8 : 0) +
          (entry.description.toLocaleLowerCase().includes(term) ? 3 : 0) +
          (entry.content.toLocaleLowerCase().includes(term) ? 1 : 0),
        0,
      ),
      matches: terms.every((term) =>
        (entry.title + entry.description + entry.content)
          .toLocaleLowerCase()
          .includes(term),
      ),
    }))
    .filter((entry) => !terms.length || entry.matches)
    .sort((a, b) => b.score - a.score)
    .slice(0, terms.length ? entries.length : 9);
  const languageUrl = en
    ? path.slice(3) || "/"
    : "/en" + (path === "/" ? "" : path);
  return (
    <>
      <header className="site-header">
        <div className="header-inner">
          <Link
            href={url("/")}
            className="brand"
            aria-label={t("RMCL 首页", "RMCL home")}
          >
            <img src="/assets/launcher.png" width="38" height="38" alt="" />
            <span>
              RMCL<span className="brand-sub">UI & AI · PROOF OF CONCEPT</span>
            </span>
          </Link>
          <nav
            className="desktop-nav"
            aria-label={t("主导航", "Main navigation")}
          >
            <Link
              className={
                path === url("/") || path === url("/") + "/" ? "active" : ""
              }
              href={url("/")}
            >
              {t("项目首页", "Overview")}
            </Link>
            <Link
              className={
                path.includes("/docs") && !path.includes("/project/")
                  ? "active"
                  : ""
              }
              href={url("/docs")}
            >
              {t("使用手册", "User guide")}
            </Link>
            <Link
              className={path.includes("/project/") ? "active" : ""}
              href={url("/docs/project/about")}
            >
              {t("关于", "About")}
            </Link>
          </nav>
          <div className="header-actions">
            <button
              className="search-trigger"
              onClick={openSearch}
              aria-label={t("搜索使用手册", "Search the guide")}
            >
              <Search size={16} />
              <span>{t("搜索使用手册", "Search the guide")}</span>
              <kbd>Ctrl K</kbd>
            </button>
            <Link
              href={languageUrl}
              className="language-switch"
              aria-label={en ? "切换到中文" : "Switch to English"}
            >
              <Languages size={15} />
              <span>{en ? "中文" : "EN"}</span>
            </Link>
            <button
              className="icon-button"
              onClick={theme}
              aria-label={
                dark
                  ? t("切换浅色主题", "Switch to light theme")
                  : t("切换深色主题", "Switch to dark theme")
              }
            >
              {dark ? <Sun size={18} /> : <Moon size={18} />}
            </button>
            <a
              className="github-link"
              href={url("/docs/help/feedback")}
              target="_blank"
              rel="noreferrer"
            >
              {t("反馈", "Feedback")}
              <ArrowUpRight size={13} />
            </a>
            <button
              className="icon-button mobile-menu"
              aria-label={
                menu
                  ? t("关闭导航", "Close navigation")
                  : t("打开导航", "Open navigation")
              }
              aria-expanded={menu}
              aria-controls="mobile-navigation"
              onClick={() => setMenu(!menu)}
            >
              {menu ? <X size={21} /> : <Menu size={21} />}
            </button>
          </div>
        </div>
        {menu ? (
          <nav
            id="mobile-navigation"
            className="mobile-navigation"
            aria-label={t("移动导航", "Mobile navigation")}
          >
            <Link href={url("/")}>{t("项目首页", "Overview")}</Link>
            {getNavigation(locale).map((group) => (
              <div key={group.title}>
                <strong>{group.title}</strong>
                {group.items.map((item) => (
                  <Link key={item.url} href={item.url}>
                    {item.title}
                  </Link>
                ))}
              </div>
            ))}
          </nav>
        ) : null}
      </header>
      <dialog
        ref={dialog}
        className="search-dialog"
        aria-labelledby="search-title"
        onClick={(event) => {
          if (event.target === dialog.current) dialog.current.close();
        }}
      >
        <div className="search-dialog-header">
          <Search size={22} />
          <label id="search-title" className="sr-only" htmlFor="doc-search">
            {t("搜索使用手册", "Search the guide")}
          </label>
          <input
            ref={input}
            id="doc-search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder={t(
              "试试“AI”、“皮肤”或“服务器”…",
              "Try “AI”, “skin” or “server”…",
            )}
            autoComplete="off"
          />
          <button
            className="icon-button"
            aria-label={t("关闭搜索", "Close search")}
            onClick={() => dialog.current?.close()}
          >
            <X size={20} />
          </button>
        </div>
        <div className="search-results" aria-live="polite">
          <p className="search-caption">
            {status === "loading"
              ? t("正在准备搜索…", "Loading search…")
              : status === "error"
                ? t("搜索暂时不可用", "Search is unavailable")
                : terms.length
                  ? t(
                      `找到 ${results.length} 篇相关指南`,
                      `${results.length} matching guides`,
                    )
                  : t("你可能想找", "Suggested guides")}
          </p>
          {status === "error" ? (
            <button className="button secondary" onClick={openSearch}>
              {t("重试", "Retry")}
            </button>
          ) : null}
          {status === "ready" && !results.length ? (
            <div className="no-results">
              <BookOpen size={28} />
              <p>{t("没有找到相关内容", "No matching guides")}</p>
              <small>
                {t("换个短一点的关键词试试。", "Try a shorter search term.")}
              </small>
            </div>
          ) : null}
          {status === "ready"
            ? results.map((entry) => (
                <Link
                  href={entry.url}
                  key={entry.url}
                  onClick={() => dialog.current?.close()}
                  className="search-result"
                >
                  <BookOpen size={18} />
                  <span>
                    <strong>{entry.title}</strong>
                    <small>{entry.description}</small>
                  </span>
                  <ChevronRight size={16} />
                </Link>
              ))
            : null}
        </div>
        <div className="search-dialog-footer">
          <span>
            {t(
              "标题与正文一起搜索 · 本机完成",
              "Full-text search · on your device",
            )}
          </span>
          <span>
            <kbd>Tab</kbd> {t("选择", "select")} <kbd>Enter</kbd>{" "}
            {t("打开", "open")} <kbd>Esc</kbd> {t("关闭", "close")}
          </span>
        </div>
      </dialog>
    </>
  );
}
export function Footer() {
  const path = usePathname(),
    en = path === "/en" || path.startsWith("/en/"),
    locale: Locale = en ? "en" : "zh",
    url = (href: string) => localized(href, locale);
  return (
    <footer className="site-footer">
      <div className="footer-top">
        <Link href={url("/")} className="footer-brand">
          RMCL
          <span>
            {en ? "A Sprout UI proof of concept." : "一个 Sprout UI 的 PoC。"}
          </span>
        </Link>
        <nav aria-label={en ? "Footer navigation" : "页脚导航"}>
          <Link href={url("/docs/project/about")}>
            {en ? "About & contact" : "关于与联系"}
          </Link>
          <Link href={url("/docs/project/privacy")}>
            {en ? "Privacy" : "隐私说明"}
          </Link>
          <Link href={url("/docs/project/terms")}>
            {en ? "Terms" : "使用约定"}
          </Link>
          <a
            href="https://github.com/Richasy/Rodel.MinecraftLauncher/issues"
            target="_blank"
            rel="noreferrer"
          >
            {en ? "Feedback" : "反馈"}
            <ArrowUpRight size={13} />
          </a>
        </nav>
      </div>
      <div className="footer-bottom">
        <span>Windows 10 19041+ · x64 / ARM64</span>
        <p>
          {en
            ? "Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft."
            : "非 Minecraft 官方产品。未经 Mojang 或 Microsoft 批准或关联。"}
        </p>
        <span>© 2026 Richasy</span>
      </div>
    </footer>
  );
}
export function Contents({
  items,
  locale = "zh",
}: {
  items: { title: React.ReactNode; url: string; depth: number }[];
  locale?: Locale;
}) {
  const [active, setActive] = useState("");
  const en = locale === "en";
  useEffect(() => {
    const observer = new IntersectionObserver(
      (records) => {
        const visible = records.filter((record) => record.isIntersecting);
        if (visible.length) setActive("#" + visible[0].target.id);
      },
      { rootMargin: "-90px 0px -65% 0px" },
    );
    items.forEach((item) => {
      const element = document.getElementById(
        decodeURIComponent(item.url.slice(1)),
      );
      if (element) observer.observe(element);
    });
    return () => observer.disconnect();
  }, [items]);
  return (
    <aside className="toc">
      <p className="eyebrow">{en ? "ON THIS PAGE" : "本页内容"}</p>
      <nav aria-label={en ? "On this page" : "本页目录"}>
        {items.map((item) => (
          <a
            key={item.url}
            href={item.url}
            className={active === item.url ? "active" : ""}
            style={{ paddingLeft: item.depth > 2 ? 12 : 0 }}
          >
            {item.title}
          </a>
        ))}
      </nav>
      <div className="toc-help">
        <span className="pixel-plus" />
        <strong>{en ? "Need a hand?" : "还需要一点帮助？"}</strong>
        <p>
          {en
            ? "Find a common fix or share what happened."
            : "看看常见问题，或者告诉我们遇到了什么。"}
        </p>
        <Link href={localized("/docs/help/faq", locale)}>
          {en ? "Frequently asked questions" : "查看常见问题"}
          <ChevronRight size={14} />
        </Link>
      </div>
    </aside>
  );
}
export function CopyLink({ locale = "zh" }: { locale?: Locale }) {
  const [copied, setCopied] = useState(false);
  const en = locale === "en";
  return (
    <button
      className="copy-link"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(location.href);
          setCopied(true);
          setTimeout(() => setCopied(false), 1800);
        } catch {
          setCopied(false);
        }
      }}
    >
      {copied ? <Check size={14} /> : <Link2 size={14} />}{" "}
      {copied
        ? en
          ? "Link copied"
          : "链接已复制"
        : en
          ? "Copy page link"
          : "复制页面链接"}
    </button>
  );
}
