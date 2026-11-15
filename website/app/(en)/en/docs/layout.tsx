import { DocsSidebar } from "@/components/docs-sidebar";
export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div className="docs-layout">
      <DocsSidebar locale="en" />
      {children}
    </div>
  );
}
