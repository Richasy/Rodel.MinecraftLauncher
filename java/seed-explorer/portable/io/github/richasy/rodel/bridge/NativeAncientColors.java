package io.github.richasy.rodel.bridge;

import java.awt.image.BufferedImage;
import java.io.*;
import java.lang.reflect.*;
import java.util.*;

import javax.imageio.ImageIO;

/** Displays native top-face atlas colours when the original game has no biome system. */
final class NativeAncientColors {
    private final Object blocks;
    private final Method texture;
    private final int[] colors;

    private NativeAncientColors(Object blocks, Method texture, int[] colors) {
        this.blocks = blocks;
        this.texture = texture;
        this.colors = colors;
    }

    /** Optional rendering data never substitutes or changes native terrain generation. */
    static NativeAncientColors open(NativeAncientSymbols symbols) throws Exception {
        byte[] bytes = symbols.terrainTexture();
        if (bytes == null) return null;
        BufferedImage image = ImageIO.read(new ByteArrayInputStream(bytes));
        if (image == null
                || image.getWidth() != image.getHeight()
                || image.getWidth() % 16 != 0
                || image.getWidth() > 4096) return null;
        List<Method> queries = new ArrayList<>();
        for (Method method : symbols.blockType.getDeclaredMethods())
            if (Modifier.isPublic(method.getModifiers())
                    && !Modifier.isStatic(method.getModifiers())
                    && method.getReturnType() == int.class
                    && Arrays.equals(
                            method.getParameterTypes(), new Class<?>[] {int.class, int.class}))
                queries.add(method);
        if (queries.size() != 1) return null;
        List<Object> arrays = new ArrayList<>();
        for (Field field : symbols.blockType.getDeclaredFields())
            if (Modifier.isStatic(field.getModifiers())
                    && field.getType() == Array.newInstance(symbols.blockType, 0).getClass()) {
                field.setAccessible(true);
                if (field.get(null) != null) arrays.add(field.get(null));
            }
        if (arrays.size() != 1) return null;
        int[] colors = new int[256];
        int tile = image.getWidth() / 16;
        for (int index = 0; index < colors.length; index++) {
            long red = 0, green = 0, blue = 0, alpha = 0;
            for (int z = 0; z < tile; z++)
                for (int x = 0; x < tile; x++) {
                    int pixel = image.getRGB((index % 16) * tile + x, (index / 16) * tile + z);
                    int weight = pixel >>> 24;
                    alpha += weight;
                    red += ((pixel >> 16) & 255) * weight;
                    green += ((pixel >> 8) & 255) * weight;
                    blue += (pixel & 255) * weight;
                }
            colors[index] =
                    alpha == 0
                            ? 0x929b9c
                            : (int) (red / alpha) << 16
                                    | (int) (green / alpha) << 8
                                    | (int) (blue / alpha);
        }
        return new NativeAncientColors(arrays.get(0), queries.get(0), colors);
    }

    /**
     * Uses the original block's side/metadata selection for the upper face of the sampled voxel.
     */
    int color(int id, int metadata) throws Exception {
        if (id <= 0 || id >= Array.getLength(blocks)) return 0x929b9c;
        Object block = Array.get(blocks, id);
        if (block == null) return 0x929b9c;
        int index = ((Number) texture.invoke(block, 1, metadata)).intValue();
        return index < 0 || index >= colors.length ? 0x929b9c : colors[index];
    }
}
