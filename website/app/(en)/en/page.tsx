import { HomePage } from "@/components/home-page";
import { pageAlternates } from "@/lib/site-metadata";
export const metadata = { alternates: pageAlternates("/", "en") };
export default function Page() {
  return <HomePage locale="en" />;
}
