import { sourceEn } from "@/lib/source-en";
import { DocumentationPage } from "@/components/documentation-page";
import { notFound } from "next/navigation";
import { pageAlternates } from "@/lib/site-metadata";
export default async function Page({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  return <DocumentationPage slug={slug} locale="en" />;
}
export function generateStaticParams() {
  return sourceEn.generateParams();
}
export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  const page = sourceEn.getPage(slug);
  if (!page) notFound();
  return {
    title: page.data.title,
    description: page.data.description,
    alternates: pageAlternates(page.url, "en"),
  };
}
