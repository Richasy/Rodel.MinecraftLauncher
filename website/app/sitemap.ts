import type { MetadataRoute } from "next";
import { navigation, localized } from "@/lib/navigation";
import { siteOrigin } from "@/lib/site-metadata";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  const paths = [
    "/",
    ...navigation.flatMap((group) => group.items.map((item) => item.url)),
  ];
  return paths.flatMap((pathname) => {
    const zh = siteOrigin + pathname.replace(/\/$/, "") + "/";
    const en = siteOrigin + localized(pathname, "en").replace(/\/$/, "") + "/";
    const alternates = { languages: { "zh-CN": zh, en } };
    return [
      { url: zh, alternates },
      { url: en, alternates },
    ];
  });
}
