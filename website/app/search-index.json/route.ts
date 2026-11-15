import { source } from "@/lib/source";
export const dynamic = "force-static";
export async function GET() {
  const pages = await Promise.all(
    source.getPages().map(async (page) => ({
      title: page.data.title,
      description: page.data.description || "",
      url: page.url,
      content: (await page.data.getText("processed")).replace(
        /<[^>]*>|[#*_]/g,
        " ",
      ),
    })),
  );
  return Response.json(pages);
}
