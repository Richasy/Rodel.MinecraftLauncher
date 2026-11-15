import type { Metadata } from "next";
import { siteOrigin } from "@/lib/site-metadata";
import { Header, Footer } from "@/components/site-shell";
import "@/app/global.css";
export const metadata: Metadata = {
  metadataBase: new URL(siteOrigin),
  title: {
    default: "RMCL · 一个 UI 与 AI 的 PoC",
    template: "%s · RMCL 使用手册",
  },
  description:
    "用于验证 Sprout 原生 UI 框架的实验性 Minecraft Java 启动器。支持 Windows 10 19041 及以上，x64 / ARM64，探索 AI 对新手玩家的辅助。",
  robots: { index: true, follow: true },
  icons: { icon: "/assets/launcher.png" },
};
export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="zh-CN" suppressHydrationWarning data-scroll-behavior="smooth">
      <head>
        <script
          dangerouslySetInnerHTML={{
            __html:
              "try{document.documentElement.dataset.theme=localStorage.getItem('rodel-docs-theme')||'light'}catch{}",
          }}
        />
      </head>
      <body>
        <a className="skip-link" href="#main-content">
          跳到正文
        </a>
        <Header />
        {children}
        <Footer />
      </body>
    </html>
  );
}
