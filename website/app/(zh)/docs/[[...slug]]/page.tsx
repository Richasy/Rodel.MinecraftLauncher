import { source } from "@/lib/source";
import { DocumentationPage } from "@/components/documentation-page";
import { notFound } from "next/navigation";
import { pageAlternates } from "@/lib/site-metadata";
export default async function Page({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  return <DocumentationPage slug={slug} locale="zh" />;
}
export function generateStaticParams() {
  return source.generateParams();
}
export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  const page = source.getPage(slug);
  if (!page) notFound();
  return {
    title: page.data.title,
    description: page.data.description,
    alternates: pageAlternates(page.url, "zh"),
  };
}
