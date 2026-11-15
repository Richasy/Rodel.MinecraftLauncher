"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { ArrowLeft, ArrowUpRight } from "lucide-react";
import { getNavigation, localized, type Locale } from "@/lib/navigation";
export function DocsSidebar({ locale = "zh" }: { locale?: Locale }) {
  const path = usePathname().replace(/\/$/, "");
  const en = locale === "en";
  return (
    <aside className="docs-sidebar">
      <Link href={localized("/", locale)} className="back-home">
        <ArrowLeft size={14} />
        {en ? "Back to home" : "返回首页"}
      </Link>
      <div className="sidebar-heading">
        <span className="eyebrow">RMCL / DOCUMENTATION</span>
        <strong>{en ? "User guide" : "使用手册"}</strong>
      </div>
      <nav aria-label={en ? "Documentation" : "文档目录"}>
        {getNavigation(locale).map((group) => (
          <div className="sidebar-group" key={group.title}>
            <h2>
              <span>{group.number}</span>
              {group.title}
            </h2>
            {group.items.map((item) => (
              <Link
                href={item.url}
                key={item.url}
                aria-current={path === item.url ? "page" : undefined}
              >
                {item.title}
              </Link>
            ))}
          </div>
        ))}
      </nav>
      <a
        className="sidebar-feedback"
        href={localized("/docs/help/feedback", locale)}
        target="_blank"
        rel="noreferrer"
      >
        {en ? "Feedback & logs" : "反馈与日志"}
        <ArrowUpRight size={14} />
      </a>
    </aside>
  );
}
