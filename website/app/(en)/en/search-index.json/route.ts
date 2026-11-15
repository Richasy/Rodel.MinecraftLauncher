import { sourceEn } from "@/lib/source-en";
export const dynamic = "force-static";
export async function GET() {
  return Response.json(
    await Promise.all(
      sourceEn.getPages().map(async (page) => ({
        title: page.data.title,
        description: page.data.description || "",
        url: page.url,
        content: await page.data.getText("processed"),
      })),
    ),
  );
}
