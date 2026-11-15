package io.github.richasy.rodel.ui;

import net.minecraft.client.Minecraft;

public interface MenuContext {
    float scale();
    boolean compact();
    MenuTheme theme();

    default MenuDensity density() {
        var window = Minecraft.getInstance().getWindow();
        return compact(window.getGuiScaledWidth(), window.getGuiScaledHeight()) ? MenuDensity.COMPACT : MenuDensity.NORMAL;
    }

    default boolean focusVisible() {
        return Minecraft.getInstance().getLastInputType().isKeyboard();
    }

    static boolean compact(int width, int height) {
        return width / designScale() < 1100 || height / designScale() < 640 || (float) width / height < 1.5f;
    }

    static float designScale() {
        // Two design units equal one Minecraft GUI unit; the game's GUI scale supplies the pixel multiplier.
        return .5f;
    }

    record Viewport(int width, int height, MenuContext source) implements MenuContext {
        @Override
        public float scale() {
            return MenuContext.designScale();
        }

        @Override
        public boolean compact() {
            return MenuContext.compact(width, height);
        }

        @Override
        public MenuTheme theme() {
            return source.theme();
        }
    }
}
