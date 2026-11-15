package io.github.richasy.rodel.ui.worlds;

import java.util.List;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import org.slf4j.LoggerFactory;

public final class WorldEditPage implements WorldSurface {
    private final Screen screen;
    private final MenuContext appearance;
    private final List<AbstractWidget> original;
    private final SettingsChoiceList choices = new SettingsChoiceList(this);
    private final EditBox name;
    private final Button save;
    private final Button cancel;
    private WorldFormList body;
    private int actualWidth;
    private int actualHeight;
    private float canvasScale = 1;
    private int left;
    private int top;
    private int width;
    private int bottom;
    private int panelHeight;
    private Component trimmedTooltip;

    public WorldEditPage(Screen screen, MenuContext appearance) {
        this.screen = screen;
        this.appearance = appearance;
        original = List.copyOf(NativeWidgets.get(screen));
        name = original.stream().filter(EditBox.class::isInstance).map(EditBox.class::cast).findFirst().orElseThrow();
        save = original.stream().filter(Button.class::isInstance).map(Button.class::cast)
            .filter(button -> SettingsText.hasKey(button.getMessage(), "selectWorld.edit.save")).findFirst().orElseThrow();
        cancel = original.stream().filter(Button.class::isInstance).map(Button.class::cast)
            .filter(button -> SettingsText.hasKey(button.getMessage(), "gui.cancel")).findFirst().orElseThrow();
    }
    public static WorldEditPage get(Screen screen) {
        return WorldSurface.get(screen) instanceof WorldEditPage page ? page : null;
    }
    @Override public void prepare() {
        var window = Minecraft.getInstance().getWindow();
        actualWidth = window.getGuiScaledWidth();
        actualHeight = window.getGuiScaledHeight();
        canvasScale = MenuContext.designScale() * 2;
        screen.width = Math.round(actualWidth / canvasScale);
        screen.height = Math.round(actualHeight / canvasScale);
    }
    @Override public void beginInit() { prepare(); }
    @Override public void initialized() {
        if (body == null) body = new WorldFormList(this, original.stream()
            .filter(widget -> widget == name || widget instanceof Button && widget != save && widget != cancel).toList());
        ((ThemedButton) save).rodel$setStyle(this, null, true, false, false);
        ((ThemedButton) cancel).rodel$setStyle(this, MenuIcon.BACK, false, true, false);
        cancel.setTooltip(Tooltip.create(Component.translatable("gui.cancel")));
        var widgets = NativeWidgets.get(screen);
        widgets.clear();
        widgets.add(cancel);
        widgets.add(body);
        widgets.add(save);
        widgets.add(choices);
        layout();
        LoggerFactory.getLogger("Rodel UI").info("Native edit-world page initialized");
    }
    public void focusName() { body.focusEditor(screen, name); }
    @Override public void layout() {
        if (body == null) return;
        int margin = density().tight() ? 24 : compact() ? 16 : 36;
        width = Math.min(screen.width - 2 * margin, 450);
        left = (screen.width - width) / 2;
        top = density().tight() ? 12 : compact() ? 14 : 24;
        bottom = screen.height - top - density().control();
        cancel.setRectangle(density().control(), density().control(), left, top);
        save.setRectangle(76, density().control(), left + width - 76, bottom);
        int panelTop = top + density().control() + 10;
        panelHeight = Math.min(bottom - density().gap() - panelTop, body.naturalHeight(width));
        body.resize(left, panelTop, width, panelHeight);
    }
    @Override public void render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        ((UiBackdrop) screen).rodel$backdrop(graphics, theme(), actualWidth, actualHeight, partialTick);
        graphics.pose().pushMatrix();
        graphics.pose().scale(canvasScale);
        UiInputGeometry.push(canvasScale, 0, 0);
        try {
            int x = Math.round(mouseX / canvasScale), y = Math.round(mouseY / canvasScale);
            trimmedTooltip = null;
            int panelTop = top + density().control() + 10;
            graphics.fill(left, panelTop, left + width, panelTop + panelHeight, theme().surface());
            graphics.outline(left, panelTop, width, panelHeight, theme().edge());
            text(graphics, screen.getTitle(), left + density().control() + 10,
                top + (density().control() - density().heading()) / 2f, density().heading(), theme().ink(), false);
            body.refresh();
            for (AbstractWidget widget : NativeWidgets.get(screen)) widget.extractRenderState(graphics, x, y, partialTick);
            choices.overlay(graphics, x, y, partialTick);
            if (trimmedTooltip != null && !choices.visible) graphics.setTooltipForNextFrame(trimmedTooltip, x, y);
            UiInputGeometry.flushPreedit(graphics, x, y, partialTick);
        } finally {
            UiInputGeometry.pop();
            graphics.pose().popMatrix();
        }
    }
    @Override public void text(GuiGraphicsExtractor graphics, Component value, float x, float y, float size, int ink, boolean secondary) {
        MenuText.extract(graphics, Minecraft.getInstance().font, secondary ? MenuText.secondary(value) : MenuText.styled(value), x, y, size, ink);
    }
    @Override public Component controlText(AbstractWidget widget) { return widget.getMessage(); }
    @Override public Component fitLabel(Component full, float available, boolean tooltip) {
        var font = Minecraft.getInstance().font;
        if (font.width(full) <= available) return full;
        if (tooltip) contextualTooltip(full);
        return Component.literal(font.substrByWidth(full, Math.max(1, (int) available - font.width("…"))).getString() + "…");
    }
    @Override public Component fitValue(AbstractWidget widget, Component full, float available) {
        return fitLabel(full, available, widget.isHovered() || widget.isFocused() && focusVisible());
    }
    @Override public void contextualTooltip(Component full) { trimmedTooltip = full; }
    @Override public void openChoices(AbstractWidget widget) { choices.open(widget, screen.width, screen.height); }
    @Override public SettingsChoiceList choices() { return choices; }
    @Override public int controlTextSize() { return density().body() * 2; }
    @Override public float scale() { return .5f; }
    @Override public boolean compact() { return screen.width < 480; }
    @Override public MenuTheme theme() { return appearance.theme(); }
    @Override public int p(float value) { return Math.round(value * scale()); }
    @Override public double coordinate(double value) { return value / canvasScale; }
    @Override public MouseButtonEvent mouse(MouseButtonEvent event) {
        return new MouseButtonEvent(coordinate(event.x()), coordinate(event.y()), event.buttonInfo());
    }
    @Override public void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                                    int thumbHeight, double scroll, int maxScroll) {
        if (maxScroll > 0) {
            int thumb = y + (int) (scroll * (height - thumbHeight) / maxScroll);
            graphics.fill(x, thumb, x + width, thumb + thumbHeight, theme().muted());
        }
    }
}
