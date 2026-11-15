import Link from "next/link";
import { ChevronRight, ArrowLeft, ArrowRight, Clock3 } from "lucide-react";
import { notFound } from "next/navigation";
import { source } from "@/lib/source";
import { sourceEn } from "@/lib/source-en";
import { getAllPages, localized, type Locale } from "@/lib/navigation";
import { Contents, CopyLink } from "@/components/site-shell";
import { getMDXComponents } from "@/components/mdx";
export async function DocumentationPage({
  slug,
  locale,
}: {
  slug?: string[];
  locale: Locale;
}) {
  const en = locale === "en",
    pages = getAllPages(locale);
  const page = (en ? sourceEn : source).getPage(slug);
  if (!page) notFound();
  const MDX = page.data.body,
    index = pages.findIndex((item) => item.url === page.url);
  const entry = pages[index],
    previous = pages[index - 1],
    next = pages[index + 1];
  const text = await page.data.getText("processed");
  const minutes = Math.max(
    2,
    Math.ceil(en ? text.split(/\s+/).length / 210 : text.length / 400),
  );
  return (
    <>
      <main id="main-content" className="doc-main">
        <div className="breadcrumb">
          <Link href={localized("/docs", locale)}>
            {en ? "User guide" : "使用手册"}
          </Link>
          <ChevronRight size={12} />
          <span>{entry?.group}</span>
        </div>
        <div className="article-meta">
          <span className="eyebrow">RMCL / DOCUMENTATION</span>
          <span>
            <Clock3 size={13} />
            {en ? `${minutes} min read` : `约 ${minutes} 分钟`}
          </span>
        </div>
        <h1>{page.data.title}</h1>
        <p className="doc-description">{page.data.description}</p>
        <div className="article-tools">
          <span>
            {en
              ? "Windows 10 (19041+) · x64 / ARM64 · PoC"
              : "Windows 10（19041+）· x64 / ARM64 · PoC"}
          </span>
          <CopyLink locale={locale} />
        </div>
        <article className="prose">
          <MDX components={getMDXComponents()} />
        </article>
        <div className="article-end">
          <span>{en ? "Continue reading" : "继续阅读"}</span>
          <span className="tiny-blocks">▪ ▪ ▪</span>
        </div>
        <nav
          className="page-navigation"
          aria-label={en ? "Adjacent guides" : "相邻文档"}
        >
          {previous ? (
            <Link href={previous.url}>
              <small>
                <ArrowLeft size={13} />
                {en ? "Previous" : "上一篇"}
              </small>
              <strong>{previous.title}</strong>
            </Link>
          ) : (
            <div />
          )}
          {next ? (
            <Link href={next.url}>
              <small>
                {en ? "Next" : "下一篇"}
                <ArrowRight size={13} />
              </small>
              <strong>{next.title}</strong>
            </Link>
          ) : (
            <Link href={localized("/docs", locale)}>
              <small>
                {en ? "Back to the guide" : "回到手册"}
                <ArrowRight size={13} />
              </small>
              <strong>{en ? "Welcome to RMCL" : "欢迎来到 RMCL"}</strong>
            </Link>
          )}
        </nav>
        <p className="doc-feedback">
          {en ? "Something unclear? " : "还有不清楚的地方？ "}
          <Link href={localized("/docs/help/feedback", locale)}>
            {en ? "Send feedback ↗" : "告诉我们 ↗"}
          </Link>
        </p>
      </main>
      <Contents items={page.data.toc} locale={locale} />
    </>
  );
}
