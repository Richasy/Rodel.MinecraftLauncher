import type { MDXComponents } from "mdx/types";
import Link from "next/link";
import type { ComponentProps, ReactNode } from "react";
import { versionedAssetUrl } from "@/lib/image-assets.mjs";
import { StoreBadge } from "@/components/store-badge";
import {
  CircleAlert,
  Info,
  Lightbulb,
  ShieldAlert,
  TriangleAlert,
} from "lucide-react";
function Screenshot({ src, ...props }: ComponentProps<"img">) {
  const imageUrl = typeof src === "string" ? versionedAssetUrl(src) : src;
  if (typeof imageUrl !== "string") return <img {...props} src={imageUrl} />;
  return (
    <a
      className="screenshot-link"
      href={imageUrl}
      target="_blank"
      rel="noreferrer"
    >
      <img {...props} src={imageUrl} />
    </a>
  );
}
function Callout({
  children,
  title = "小提示",
  type = "tip",
}: {
  children: ReactNode;
  title?: string;
  type?: "tip" | "note" | "important" | "warning" | "caution";
}) {
  const Icon = {
    tip: Lightbulb,
    note: Info,
    important: CircleAlert,
    warning: TriangleAlert,
    caution: ShieldAlert,
  }[type];
  return (
    <aside className={`callout ${type}`}>
      <span className="callout-mark" aria-hidden="true">
        <Icon size={20} strokeWidth={1.75} />
      </span>
      <div>
        <strong>{title}</strong>
        <div>{children}</div>
      </div>
    </aside>
  );
}
function GuideCard({
  href,
  title,
  children,
}: {
  href: string;
  title: string;
  children: ReactNode;
}) {
  return (
    <Link className="guide-card" href={href}>
      <strong>
        {title}
        <span>↗</span>
      </strong>
      <div className="guide-card-description">{children}</div>
    </Link>
  );
}
export function getMDXComponents(components?: MDXComponents) {
  return {
    Callout,
    GuideCard,
    Screenshot,
    StoreBadge,
    img: Screenshot,
    ...components,
  } satisfies MDXComponents;
}
export const useMDXComponents = getMDXComponents;
declare global {
  type MDXProvidedComponents = ReturnType<typeof getMDXComponents>;
}
