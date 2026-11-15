const labels = {
  NOTE: ["说明", "Note"],
  TIP: ["小提示", "Tip"],
  IMPORTANT: ["重要", "Important"],
  WARNING: ["注意", "Warning"],
  CAUTION: ["请谨慎操作", "Caution"],
};

// Keep GitHub-style alerts readable in source and render them through the
// site's own Callout component. Ordinary quotations remain quotations.
export function remarkAlerts() {
  return (tree, file) => {
    const english = /[/\\]content[/\\]en[/\\]/.test(file.path ?? "");
    function visit(node) {
      if (!Array.isArray(node.children)) return;
      if (node.type === "blockquote") {
        const paragraph = node.children[0];
        const first = paragraph?.type === "paragraph" && paragraph.children[0];
        const match =
          first?.type === "text" &&
          /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\](?:\r?\n|$)/.exec(
            first.value,
          );
        if (match) {
          first.value = first.value.slice(match[0].length);
          if (!first.value) paragraph.children.shift();
          if (!paragraph.children.length) node.children.shift();
          node.type = "mdxJsxFlowElement";
          node.name = "Callout";
          node.attributes = [
            {
              type: "mdxJsxAttribute",
              name: "type",
              value: match[1].toLowerCase(),
            },
            {
              type: "mdxJsxAttribute",
              name: "title",
              value: labels[match[1]][english ? 1 : 0],
            },
          ];
        }
      }
      node.children.forEach(visit);
    }
    visit(tree);
  };
}
