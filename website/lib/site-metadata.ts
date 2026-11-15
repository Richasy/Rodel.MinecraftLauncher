import type { Metadata } from "next";
import { localized, type Locale } from "@/lib/navigation";

export const siteOrigin = "https://rmcl.richasy.net";

export function pageAlternates(
  pathname: string,
  locale: Locale,
): Metadata["alternates"] {
  const base = pathname.replace(/^\/en(?=\/|$)/, "") || "/";
  const zh = base.replace(/\/$/, "") + "/";
  const en = localized(zh, "en");
  return {
    canonical: locale === "en" ? en : zh,
    languages: { "zh-CN": zh, en, "x-default": zh },
  };
}
