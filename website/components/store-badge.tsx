import { versionedAssetUrl } from "@/lib/image-assets.mjs";
import type { Locale } from "@/lib/navigation";

export function StoreBadge({ locale = "zh" }: { locale?: Locale }) {
  return (
    <a
      className="store-badge"
      href="https://apps.microsoft.com/detail/9MW9ZMS2K47C"
      aria-label={
        locale === "en"
          ? "Get RMCL from Microsoft Store"
          : "从 Microsoft Store 获取 RMCL"
      }
    >
      <img
        src={versionedAssetUrl(`/assets/microsoft-store-${locale}.svg`)}
        alt={
          locale === "en"
            ? "Get it from Microsoft Store"
            : "从 Microsoft Store 获取"
        }
        width="161"
        height="44"
      />
    </a>
  );
}
