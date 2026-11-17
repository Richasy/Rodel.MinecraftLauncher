import { Download } from "lucide-react";
import type { Locale } from "@/lib/navigation";

export function ReleaseDownload({ locale = "zh" }: { locale?: Locale }) {
  return (
    <a
      className="button primary"
      href="https://github.com/Richasy/Rodel.MinecraftLauncher/releases/latest"
      aria-label={
        locale === "en"
          ? "Download RMCL from GitHub Releases"
          : "从 GitHub Releases 下载 RMCL"
      }
    >
      <Download size={18} aria-hidden="true" />
      {locale === "en" ? "Download for Windows" : "下载 Windows 版"}
    </a>
  );
}
