import { loader } from "fumadocs-core/source";
import { defineDocs } from "fumadocs-mdx/macro";
import { metaSchema, pageSchema } from "fumadocs-core/source/schema";
const docs = defineDocs({
  dir: "content/en/docs",
  docs: { schema: pageSchema, postprocess: { includeProcessedMarkdown: true } },
  meta: { schema: metaSchema },
});
export const sourceEn = loader({
  baseUrl: "/en/docs",
  source: docs.toFumadocsSource(),
});
