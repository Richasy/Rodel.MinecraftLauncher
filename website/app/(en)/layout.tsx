import type { Metadata } from "next";
import { siteOrigin } from "@/lib/site-metadata";
import { Header, Footer } from "@/components/site-shell";
import "@/app/global.css";
export const metadata: Metadata = {
  metadataBase: new URL(siteOrigin),
  title: {
    default: "RMCL · A UI & AI proof of concept",
    template: "%s · RMCL User Guide",
  },
  description:
    "An experimental Minecraft Java launcher for exploring the Sprout native UI framework and AI assistance for new players. Windows 10 build 19041+, x64 / ARM64.",
  robots: { index: true, follow: true },
  icons: { icon: "/assets/launcher.png" },
};
export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning data-scroll-behavior="smooth">
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
          Skip to content
        </a>
        <Header />
        {children}
        <Footer />
      </body>
    </html>
  );
}
