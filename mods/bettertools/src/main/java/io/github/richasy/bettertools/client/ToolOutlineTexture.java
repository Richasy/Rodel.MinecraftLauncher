package io.github.richasy.bettertools.client;

import com.mojang.blaze3d.platform.NativeImage;
import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.ToolKind;
import java.io.IOException;
import net.minecraft.client.renderer.texture.ReloadableTexture;
import net.minecraft.client.renderer.texture.TextureContents;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.ResourceManager;

/** Traces the current vanilla/resource-pack tool alpha; native texture reload owns loading, upload and disposal. */
public final class ToolOutlineTexture extends ReloadableTexture {
    private static final int SIZE = 64;
    private final Identifier original;

    /** Registers a derived texture without copying Minecraft art into the mod. */
    public ToolOutlineTexture(ToolKind kind) {
        super(id(kind));
        original = Identifier.withDefaultNamespace("textures/item/iron_" + kind.name().toLowerCase(java.util.Locale.ROOT) + ".png");
    }

    /** Identifies the reload-managed outline associated with one vanilla tool silhouette. */
    public static Identifier id(ToolKind kind) { return BetterTools.id("outline/" + kind.name().toLowerCase(java.util.Locale.ROOT)); }

    /** Produces a hollow, half-GUI-pixel contour along the source's exact alpha boundary. */
    @Override public TextureContents loadContents(ResourceManager resources) throws IOException {
        try (var stream = resources.open(original); var image = NativeImage.read(stream)) {
            var result = new NativeImage(SIZE, SIZE, true);
            for (int y = 0; y < SIZE; y++) for (int x = 0; x < SIZE; x++) {
                boolean edge = opaque(image, x, y) && (!opaque(image, x - 2, y) || !opaque(image, x + 2, y)
                        || !opaque(image, x, y - 2) || !opaque(image, x, y + 2));
                result.setPixel(x, y, edge ? 0xFFFFFFFF : 0);
            }
            return new TextureContents(result, null);
        }
    }

    private static boolean opaque(NativeImage image, int x, int y) {
        if (x < 0 || y < 0 || x >= SIZE || y >= SIZE) return false;
        int sourceX = x * image.getWidth() / SIZE;
        int sourceY = y * Math.min(image.getWidth(), image.getHeight()) / SIZE;
        return (image.getPixel(sourceX, sourceY) >>> 24) >= 128;
    }
}
