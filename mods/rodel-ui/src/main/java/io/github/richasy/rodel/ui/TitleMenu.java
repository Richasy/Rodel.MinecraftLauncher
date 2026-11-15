package io.github.richasy.rodel.ui;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.List;
import io.github.richasy.rodel.ui.platform.LoaderPlatform;
import net.minecraft.SharedConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.PlainTextButton;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.components.toasts.SystemToast;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;
import io.github.richasy.rodel.ui.mixin.ButtonActionAccessor;
import io.github.richasy.rodel.ui.mixin.WidgetTooltipAccessor;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class TitleMenu implements MenuContext {
    private static final Logger LOGGER = LoggerFactory.getLogger("Rodel UI");
    private static Boolean sessionDark;
    private final Minecraft minecraft;
    private final List<Button> buttons = new ArrayList<>();
    private final List<OriginalButton> originals = new ArrayList<>();
    private final List<OriginalButton> applied = new ArrayList<>();
    private final Path themePath = LoaderPlatform.configDirectory().resolve("rodel-ui-theme.txt");
    private final int width;
    private final int height;
    private final float scale;
    private final boolean compact;
    private final boolean dense;
    private final int outer;
    private final int heroTop;
    private final int panelX;
    private final int panelWidth;
    private final int panelPadding;
    private int panelY;
    private int panelHeight;
    private boolean dark;
    private Button themeButton;

    public TitleMenu(Minecraft minecraft, int width, int height) {
        this.minecraft = minecraft;
        compact = (float) width / height < 1.5f;
        dense = !compact && MenuContext.compact(width, height);
        scale = MenuContext.designScale();
        this.width = Math.round(width / scale);
        this.height = Math.round(height / scale);
        outer = dense ? 48 : compact ? 32 : 72;
        heroTop = this.height - outer - (dense ? 148 : compact ? this.height < 560 ? 181 : 205 : 263);
        panelPadding = dense ? 14 : compact ? 20 : 24;
        panelWidth = dense ? 200 : compact ? this.width - 2 * outer : 320;
        panelX = this.width - panelWidth - outer;
        prepareTheme(minecraft);
        dark = sessionDark;
    }

    /** Resolves the session preference once, including direct world entry that bypasses the title screen. */
    public static void prepareTheme(Minecraft minecraft) {
        if (sessionDark != null) return;
        Path themePath = LoaderPlatform.configDirectory().resolve("rodel-ui-theme.txt");
        boolean dark = false;
        try {
            if (Files.exists(themePath)) {
                String preference = Files.readString(themePath).strip();
                if (!preference.equals("light") && !preference.equals("dark")) {
                    throw new IOException("Invalid Rodel UI theme: " + preference);
                }
                dark = preference.equals("dark");
            }
        } catch (IOException exception) {
            LOGGER.error("Could not read theme preference from {}", themePath, exception);
            SystemToast.add(minecraft.gui.toastManager(), SystemToast.SystemToastId.PACK_LOAD_FAILURE,
                Component.translatable("rodel.menu.theme.error"), Component.literal(exception.toString()));
        }
        String requested = System.getProperty("rodel.ui.theme");
        if ("light".equals(requested) || "dark".equals(requested)) {
            dark = requested.equals("dark");
        } else if (requested != null) {
            LOGGER.warn("Unknown launcher UI theme {}; retaining the game preference", requested);
        }
        sessionDark = dark;
    }

    /** Returns null without changing controls when the native page cannot fit this layout. */
    public List<Button> layout(List<? extends GuiEventListener> children, Component copyright) {
        List<Button> hero = new ArrayList<>();
        List<Button> side = new ArrayList<>();
        List<Button> footer = new ArrayList<>();
        for (GuiEventListener child : children) {
            if (!(child instanceof Button button)) {
                return null;
            }
            if (button instanceof PlainTextButton) {
                if (!button.getMessage().equals(copyright)) return null;
                continue;
            }
            String key = key(button);
            if (key.equals("menu.singleplayer") || key.equals("menu.playdemo") || key.equals("menu.multiplayer")) {
                hero.add(button);
            } else if (key.equals("options.language") || key.equals("options.accessibility")) {
                footer.add(button);
            } else {
                side.add(button);
            }
        }
        int actionWidth = dense ? 288 : compact ? width - 2 * outer - 204 : 510;
        int actionGap = dense ? 8 : 12;
        int primaryWidth = compact ? (actionWidth - actionGap) / 2 : Math.round((actionWidth - actionGap) * .6f);
        int actionHeight = dense ? 30 : compact ? 52 : 62;
        int columns = compact ? 2 : 1;
        int rows = (side.size() + columns - 1) / columns;
        int rowHeight = dense ? 28 : compact ? 42 : 52;
        int rowGap = dense ? 8 : 14;
        int contentTop = panelPadding + (dense ? 18 : compact ? 16 + 14 : 20 + 20);
        panelHeight = contentTop + rows * rowHeight + Math.max(0, rows - 1) * rowGap + panelPadding;
        panelY = compact ? outer : (height - panelHeight) / 2;
        if (hero.size() > 2 || actionWidth <= 0 || panelY < 0
            || compact && panelY + panelHeight > heroTop || panelY + panelHeight > height) return null;
        for (GuiEventListener child : children) originals.add(new OriginalButton((Button) child));
        int actionX = outer;
        for (int i = 0; i < hero.size(); i++) {
            int buttonWidth = i == 0 ? primaryWidth : actionWidth - primaryWidth - actionGap;
            place(hero.get(i), actionX, height - outer - actionHeight, buttonWidth, actionHeight,
                i == 0 ? MenuIcon.PLAY : MenuIcon.SERVER, i == 0, false, false);
            actionX += buttonWidth + actionGap;
        }
        int cellWidth = (panelWidth - 2 * panelPadding - (compact ? 10 : 0)) / columns;
        for (int i = 0; i < side.size(); i++) {
            Button button = side.get(i);
            place(button, panelX + panelPadding + i % columns * (cellWidth + 10),
                panelY + contentTop + i / columns * (rowHeight + rowGap), cellWidth, rowHeight,
                icon(key(button)), false, false, true);
        }
        themeButton = Button.builder(themeLabel(), button -> toggleTheme()).build();
        Button settingsButton = side.stream().filter(button -> key(button).equals("menu.options")).findFirst().orElse(null);
        if (settingsButton != null) {
            for (Button shortcut : footer) {
                ((ButtonActionAccessor) shortcut).rodel$action(ignored -> {
                    ((ButtonActionAccessor) settingsButton).rodel$action().onPress(settingsButton);
                    NativeSettingsPage page = NativeSettingsPage.get(minecraft.gui.screen());
                    if (page == null) throw new IllegalStateException("Native settings root did not attach");
                    page.openCategory(key(shortcut));
                });
            }
        } else {
            LOGGER.warn("Title settings button is unavailable; keeping native language and accessibility shortcuts");
        }
        footer.add(themeButton);
        int footerSize = dense ? 28 : 52;
        int footerGap = dense ? 8 : 12;
        int footerX = width - outer - footer.size() * footerSize - (footer.size() - 1) * footerGap;
        int footerY = height - outer - actionHeight + (actionHeight - footerSize) / 2;
        for (Button button : footer) {
            button.setTooltip(Tooltip.create(button.getMessage()));
            place(button, footerX, footerY, footerSize, footerSize,
                button == themeButton ? MenuIcon.THEME : icon(key(button)), false, true, false);
            footerX += footerSize + footerGap;
        }
        LOGGER.info("Rodel title menu initialized: canvas={}x{}, scale={}, compact={}, dense={}, controls={}",
            width, height, scale, compact, dense, buttons.size());
        for (OriginalButton original : originals) applied.add(new OriginalButton(original.button()));
        return List.copyOf(buttons);
    }

    public Button themeButton() { return themeButton; }

    /** Restore the original objects; a late extension keeps its own controls and callbacks. */
    public void restore() {
        for (int i = 0; i < originals.size(); i++) originals.get(i).restore(applied.get(i));
    }

    private record OriginalButton(Button button, int x, int y, int width, int height,
                                  Tooltip tooltip, Button.OnPress action) {
        OriginalButton(Button button) {
            this(button, button.getX(), button.getY(), button.getWidth(), button.getHeight(),
                ((WidgetTooltipAccessor) button).rodel$tooltip().get(),
                ((ButtonActionAccessor) button).rodel$action());
        }

        void restore(OriginalButton styled) {
            // Do not overwrite changes another extension made after our layout.
            if (button.getX() == styled.x && button.getY() == styled.y
                && button.getWidth() == styled.width && button.getHeight() == styled.height)
                button.setRectangle(width, height, x, y);
            if (((WidgetTooltipAccessor) button).rodel$tooltip().get() == styled.tooltip) button.setTooltip(tooltip);
            if (((ButtonActionAccessor) button).rodel$action() == styled.action)
                ((ButtonActionAccessor) button).rodel$action(action);
            ((ThemedButton) button).rodel$setStyle(null, null, false, false, false);
        }
    }

    private static String key(Button button) {
        return button.getMessage().getContents() instanceof TranslatableContents contents ? contents.getKey() : "";
    }

    private static MenuIcon icon(String key) {
        return switch (key) {
            case "menu.online" -> MenuIcon.WORLD;
            case "menu.options" -> MenuIcon.SETTINGS;
            case "menu.quit" -> MenuIcon.EXIT;
            case "options.language" -> MenuIcon.WORLD;
            case "options.accessibility" -> MenuIcon.ACCESSIBILITY;
            case "modmenu.title" -> MenuIcon.PACK;
            default -> MenuIcon.PEOPLE;
        };
    }

    private void place(Button button, int x, int y, int width, int height,
                       MenuIcon icon, boolean primary, boolean iconOnly, boolean side) {
        button.setRectangle(pixel(x + width) - pixel(x), pixel(y + height) - pixel(y), pixel(x), pixel(y));
        ((ThemedButton) button).rodel$setStyle(this, icon, primary, iconOnly, side);
        buttons.add(button);
    }

    private int pixel(int value) {
        return Math.round(value * scale);
    }

    public float scale() {
        return scale;
    }

    public boolean compact() {
        return compact;
    }

    public boolean dense() { return dense; }

    public MenuTheme theme() {
        return highContrast() ? MenuTheme.HIGH_CONTRAST : dark ? MenuTheme.DARK : MenuTheme.LIGHT;
    }

    /** Shares the current game-session preference with in-game surfaces without reading files during drawing. */
    public static MenuTheme currentTheme() {
        return Minecraft.getInstance().options.highContrast().get() ? MenuTheme.HIGH_CONTRAST
            : Boolean.TRUE.equals(sessionDark) ? MenuTheme.DARK : MenuTheme.LIGHT;
    }

    public boolean highContrast() {
        return minecraft.options.highContrast().get();
    }

    private Component themeLabel() {
        return Component.translatable(dark ? "rodel.menu.theme.light" : "rodel.menu.theme.dark");
    }

    private void toggleTheme() {
        boolean next = !dark;
        Path temporary = themePath.resolveSibling(themePath.getFileName() + ".tmp");
        try {
            Files.createDirectories(themePath.getParent());
            Files.writeString(temporary, next ? "dark\n" : "light\n");
            Files.move(temporary, themePath, StandardCopyOption.REPLACE_EXISTING);
            dark = next;
            sessionDark = next;
            themeButton.setMessage(themeLabel());
            themeButton.setTooltip(Tooltip.create(themeLabel()));
        } catch (IOException exception) {
            LOGGER.error("Could not save theme preference to {}", themePath, exception);
            reportThemeError(exception);
        }
    }

    private void reportThemeError(IOException exception) {
        SystemToast.add(minecraft.gui.toastManager(), SystemToast.SystemToastId.PACK_LOAD_FAILURE,
            Component.translatable("rodel.menu.theme.error"), Component.literal(exception.toString()));
    }

    public void extract(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        MenuTheme theme = theme();
        boolean tight = compact && height < 560;
        graphics.pose().pushMatrix();
        graphics.pose().scale(scale);
        if (highContrast()) {
            graphics.fill(0, 0, width, height, theme.surface());
        } else {
            graphics.fillGradient(0, 0, width, height, dark ? 0x0509161B : 0x08FFF8E4,
                dark ? 0xB0061216 : 0xABFFF7E1);
        }
        text(graphics, Component.literal("JAVA EDITION"), outer, heroTop, dense ? 7 : compact ? 11 : 12, theme.muted());
        text(graphics, Component.literal("Minecraft"), outer, heroTop + (dense ? 18 : compact ? 23 : 30),
            dense ? 40 : compact ? tight ? 42 : 50 : 68, theme.ink());
        text(graphics, Component.translatable("rodel.menu.tagline"), outer,
            heroTop + (dense ? 68 : compact ? tight ? 78 : 88 : 123), dense ? 10 : compact ? 15 : 17, theme.ink());
        text(graphics, Component.literal(SharedConstants.getCurrentVersion().name() + "  /  Fabric"),
            outer, heroTop + (dense ? 92 : compact ? tight ? 108 : 122 : 162), dense ? 7 : compact ? 11 : 12, theme.muted());
        theme.panel(graphics, panelX, panelY, panelWidth, panelHeight);
        text(graphics, Component.translatable("rodel.menu.actions"), panelX + panelPadding,
            panelY + panelPadding, dense ? 8 : compact ? 12 : 14, theme.muted());
        graphics.pose().popMatrix();
        for (Button button : buttons) {
            button.extractRenderState(graphics, mouseX, mouseY, partialTick);
        }
    }

    private void text(GuiGraphicsExtractor graphics, Component message, int x, int y, float size, int color) {
        MenuText.extract(graphics, minecraft.font, MenuText.styled(message), x, y, size, color);
    }
}
