package io.github.richasy.rodel.ui.settings;

import java.util.List;
import java.util.ArrayList;
import com.mojang.blaze3d.platform.InputConstants;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuActionStyle;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.AbstractScrollArea;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Checkbox;
import net.minecraft.client.gui.components.FittingMultiLineTextWidget;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.MultiLineTextWidget;
import net.minecraft.client.gui.components.AbstractStringWidget;
import net.minecraft.client.gui.components.events.ContainerEventHandler;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.gui.narration.NarratedElementType;
import net.minecraft.network.chat.Component;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.BackupConfirmScreen;
import net.minecraft.client.gui.screens.GenericWaitingScreen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.util.Util;
import org.slf4j.LoggerFactory;

public final class NativeDialog implements MenuContext {
    private final Screen screen;
    private final MenuContext appearance;
    private final boolean page;
    private List<AbstractWidget> widgets = List.of();
    private List<AbstractWidget> paragraphs = List.of();
    private DialogBody scrollBody;
    private float canvasScale = 1;
    private int actualWidth;
    private int actualHeight;
    private int left;
    private int top;
    private int width;
    private int height;
    private Button pageBack;
    private List<FormattedCharSequence> textTitle = List.of();
    private int progressTop;
    private boolean rendered;
    private int contentPadding;
    private int titleSize;
    private int titleStep;

    public NativeDialog(Screen screen, MenuContext appearance, boolean page) {
        this.screen = screen;
        this.appearance = appearance;
        this.page = page;
    }

    public void prepare() {
        var window = Minecraft.getInstance().getWindow();
        actualWidth = window.getGuiScaledWidth();
        actualHeight = window.getGuiScaledHeight();
        canvasScale = MenuContext.designScale() * 2;
        screen.width = Math.round(actualWidth / canvasScale);
        screen.height = Math.round(actualHeight / canvasScale);
    }

    public void initialized() {
        double bodyScroll = scrollBody == null ? 0 : scrollBody.scrollAmount();
        boolean bodyFocused = scrollBody != null && screen.getFocused() == scrollBody;
        if (scrollBody != null) NativeWidgets.get(screen).remove(scrollBody);
        scrollBody = null;
        contentPadding = density().tight() ? 12 : 16;
        titleSize = density().tight() ? 10 : 12;
        titleStep = density().tight() ? 14 : 16;
        widgets = List.copyOf(NativeWidgets.get(screen));
        AbstractWidget primary = page ? null : MenuActionStyle.primary(screen,
            widgets.stream().filter(Button.class::isInstance).toList());
        if (screen instanceof DialogTextHost text) {
            boolean textFocused = false;
            for (var widget : widgets) {
                if (widget instanceof AbstractStringWidget || widget instanceof FittingMultiLineTextWidget) {
                    textFocused |= screen.getFocused() == widget;
                    // This host supplies the same native text for our title/body; keep no hidden text focus target.
                    NativeWidgets.get(screen).remove(widget);
                }
            }
            var font = Minecraft.getInstance().font;
            var title = MenuText.styled(text.rodel$dialogTitle(screen.getTitle()));
            var messages = text.rodel$dialogText();
            List<AbstractWidget> content = new ArrayList<>();
            int naturalWidth = Math.round(font.width(title) * titleSize / (float) font.lineHeight);
            for (var message : messages) naturalWidth = Math.max(naturalWidth,
                Math.round(font.width(MenuText.secondary(message)) * density().body() / (float) font.lineHeight));
            int textWidth = Math.min(Math.min(screen.width - 64, density().tight() ? 320 : 400), Math.max(density().tight() ? 180 : 220, naturalWidth));
            textTitle = title.getString().isBlank() ? List.of()
                : font.split(title, Math.max(1, Math.round(textWidth * font.lineHeight / (float) titleSize)));
            int totalHeight = 0;
            for (var message : messages) {
                var presentation = MenuText.themedSecondary(message, theme().muted());
                var widget = new Paragraph(presentation, textWidth);
                content.add(widget);
                totalHeight += widget.getHeight() + density().gap();
            }
            totalHeight = Math.max(0, totalHeight - density().gap());
            List<AbstractWidget> buttons = widgets.stream().filter(widget -> widget instanceof Button).toList();
            List<AbstractWidget> controls = widgets.stream().filter(widget -> widget instanceof Checkbox).toList();
            int gap = density().gap();
            int controlsHeight = controls.stream().mapToInt(widget -> density().tight() ? density().control() : widget.getHeight()).sum()
                + Math.max(0, controls.size() - 1) * gap;
            int columns = screen instanceof BackupConfirmScreen ? Math.min(2, buttons.size()) : buttons.size();
            int buttonRows = columns == 0 ? 0 : (buttons.size() + columns - 1) / columns;
            int titleHeight = textTitle.size() * titleStep;
            int buttonHeight = density().tight() ? density().control() : 22;
            int buttonPitch = buttonHeight + gap;
            int titleGap = textTitle.isEmpty() || content.isEmpty() ? 0 : density().tight() ? 8 : 12;
            int progressHeight = screen instanceof DialogProgressHost ? density().tight() ? 48 : 60 : 0;
            int fixedHeight = contentPadding * 2 + titleHeight + titleGap
                + (controls.isEmpty() ? 0 : gap + controlsHeight)
                + (progressHeight == 0 ? 0 : gap + progressHeight)
                + (buttons.isEmpty() ? 0 : gap + buttonRows * buttonHeight + (buttonRows - 1) * gap);
            int visibleBodyHeight = Math.min(totalHeight, Math.max(1, screen.height - 16 - fixedHeight));
            height = fixedHeight + visibleBodyHeight;
            left = (screen.width - textWidth) / 2 - contentPadding;
            top = Math.max(8, (screen.height - height) / 2);
            width = textWidth + 2 * contentPadding;
            int y = top + contentPadding + titleHeight + titleGap;
            paragraphs = List.copyOf(content);
            if (totalHeight > visibleBodyHeight) {
                scrollBody = new DialogBody(paragraphs, totalHeight);
                // The reading inset belongs to the paragraphs; the scrollbar stays near the panel edge.
                scrollBody.setRectangle(textWidth + contentPadding - 3, visibleBodyHeight, left + contentPadding, y);
                scrollBody.setScrollAmount(bodyScroll);
                NativeWidgets.get(screen).add(scrollBody);
                if (bodyFocused) screen.setFocused(scrollBody);
                content.clear();
                content.add(scrollBody);
            } else {
                int paragraphY = y;
                for (var widget : content) {
                    widget.setPosition(left + contentPadding, paragraphY);
                    paragraphY += widget.getHeight() + gap;
                }
                if (bodyFocused) screen.setFocused(null);
            }
            y += visibleBodyHeight;
            if (!controls.isEmpty()) y += gap;
            for (var widget : controls) {
                widget.setRectangle(textWidth, density().tight() ? density().control() : widget.getHeight(), left + contentPadding, y);
                widget.setMessage(widget.getMessage().copy().withColor(theme().ink()));
                y += widget.getHeight();
                if (widget != controls.getLast()) y += gap;
            }
            if (progressHeight > 0) y += gap;
            progressTop = y;
            y += progressHeight;
            if (!buttons.isEmpty()) y += gap;
            int buttonWidth = Math.min(150, (textWidth - Math.max(0, columns - 1) * 8) / Math.max(1, columns));
            for (int i = 0; i < buttons.size(); i++) {
                int rowCount = Math.min(columns, buttons.size() - i / columns * columns);
                int x = (screen.width - rowCount * buttonWidth - (rowCount - 1) * 8) / 2;
                buttons.get(i).setRectangle(buttonWidth, buttonHeight,
                    x + i % columns * (buttonWidth + 8), y + i / columns * buttonPitch);
            }
            content.addAll(controls);
            content.addAll(buttons);
            widgets = List.copyOf(content);
            if (textFocused) screen.setFocused(content.stream()
                .filter(widget -> widget.active && widget.visible).findFirst().orElse(null));
        }
        int minX = screen.width;
        int minY = screen.height;
        int maxX = 0;
        int maxY = 0;
        for (AbstractWidget widget : widgets) {
            styleText(widget);
            if (widget instanceof Button button) {
                if (page && (button == pageBack || SettingsText.hasKey(button.getMessage(), "gui.done"))) {
                    pageBack = button;
                    button.setMessage(net.minecraft.network.chat.Component.translatable("gui.back"));
                    button.setRectangle(60, 22, 24, 10);
                }
                ((ThemedButton) button).rodel$setStyle(this, null, button == primary, false, false);
            } else if (widget.getMessage().getStyle().getColor() == null) {
                widget.setMessage(widget.getMessage().copy().withColor(theme().ink()));
            }
            minX = Math.min(minX, widget.getX());
            minY = Math.min(minY, widget.getY());
            maxX = Math.max(maxX, widget.getRight());
            maxY = Math.max(maxY, widget.getBottom());
        }
        if (screen instanceof DialogTextHost) return;
        left = Math.max(8, minX - 16);
        top = Math.max(8, minY - (screen instanceof DialogTextHost ? 40 : 16));
        width = Math.min(screen.width - left - 8, Math.max(160, maxX - left + 16));
        height = Math.min(screen.height - top - 8, Math.max(80, maxY - top + 16));
        if (page) {
            left = 24;
            top = 48;
            width = screen.width - 48;
            height = screen.height - 72;
        }
    }

    private final class DialogBody extends AbstractScrollArea {
        private final List<AbstractWidget> content;
        private final int naturalHeight;

        DialogBody(List<AbstractWidget> content, int naturalHeight) {
            super(0, 0, 1, 1, Component.empty(), defaultSettings(18));
            this.content = content;
            this.naturalHeight = naturalHeight;
        }

        @Override protected int contentHeight() { return naturalHeight; }
        @Override public int scrollbarWidth() { return 3; }
        @Override protected int scrollBarX() { return getRight() - scrollbarWidth(); }
        @Override protected int scrollerHeight() {
            return Math.max(1, Math.min(getHeight() - 2, Math.max(12, getHeight() * getHeight() / naturalHeight)));
        }
        @Override protected double scrollRate() { return density().body() * 3; }

        @Override public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
            return updateScrolling(event) || super.mouseClicked(event, doubleClick);
        }

        @Override public boolean keyPressed(KeyEvent event) {
            double amount = switch (event.key()) {
                case InputConstants.KEY_UP -> scrollAmount() - scrollRate();
                case InputConstants.KEY_DOWN -> scrollAmount() + scrollRate();
                case InputConstants.KEY_PAGEUP -> scrollAmount() - getHeight();
                case InputConstants.KEY_PAGEDOWN -> scrollAmount() + getHeight();
                case InputConstants.KEY_HOME -> 0;
                case InputConstants.KEY_END -> maxScrollAmount();
                default -> Double.NaN;
            };
            if (Double.isNaN(amount)) return super.keyPressed(event);
            setScrollAmount(amount);
            return true;
        }

        @Override protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
            graphics.enableScissor(getX(), getY(), getRight(), getBottom());
            int y = getY() - (int) scrollAmount();
            for (var paragraph : content) {
                paragraph.setPosition(getX(), y);
                paragraph.extractRenderState(graphics, mouseX, mouseY, tick);
                y += paragraph.getHeight() + density().gap();
            }
            graphics.disableScissor();
            int thumb = scrollerHeight();
            int thumbY = getY() + (int) (scrollAmount() * (getHeight() - thumb) / Math.max(1, maxScrollAmount()));
            graphics.fill(scrollBarX(), thumbY, getRight(), thumbY + thumb, theme().muted());
            if (isFocused() && focusVisible()) graphics.outline(getX(), getY(), getWidth(), getHeight(), theme().focus());
        }

        @Override protected void updateWidgetNarration(NarrationElementOutput output) {
            var message = Component.empty();
            for (var paragraph : content) message.append(paragraph.getMessage()).append("\n");
            output.add(NarratedElementType.TITLE, message);
        }

        @Override public void playDownSound(net.minecraft.client.sounds.SoundManager manager) {}
    }

    private final class Paragraph extends AbstractWidget {
        private final List<FormattedCharSequence> lines;
        private final int size;
        private final int step;
        Paragraph(Component message, int width) {
            super(0, 0, width, 1, message);
            size = density().body();
            step = density().tight() ? 9 : Minecraft.getInstance().font.lineHeight;
            lines = Minecraft.getInstance().font.split(message,
                Math.max(1, Math.round(width * Minecraft.getInstance().font.lineHeight / (float) size)));
            setHeight(lines.size() * step);
            active = false;
        }
        @Override protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int x, int y, float tick) {
            var font = Minecraft.getInstance().font;
            for (int i = 0; i < lines.size(); i++) {
                graphics.pose().pushMatrix();
                graphics.pose().translate(getX(), getY() + i * step);
                graphics.pose().scale(size / (float) font.lineHeight);
                graphics.text(font, lines.get(i), 0, 0, theme().muted(), false);
                graphics.pose().popMatrix();
            }
        }
        @Override protected void updateWidgetNarration(NarrationElementOutput output) {
            output.add(NarratedElementType.TITLE, getMessage());
        }
    }

    private void styleText(AbstractWidget widget) {
        if (widget instanceof AbstractStringWidget) {
            var message = widget.getMessage().copy().withStyle(style -> style.withShadowColor(0));
            var color = message.getStyle().getColor();
            if (color == null || color.getValue() == 0xFFFFFF || color.getValue() == 0xE0E0E0) {
                message.withColor(theme().ink());
            } else if (color.getValue() == 0xA0A0A0 || color.getValue() == 0x808080) {
                message.withColor(theme().muted());
            }
            widget.setMessage(message);
        }
        if (widget instanceof ContainerEventHandler container) {
            for (var child : container.children()) {
                if (child instanceof AbstractWidget nested) styleText(nested);
            }
        }
    }

    public void render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        if (!rendered) {
            rendered = true;
            LoggerFactory.getLogger("Rodel UI").info("Native themed screen rendered: {}", screen.getClass().getSimpleName());
        }
        ((UiBackdrop) screen).rodel$backdrop(graphics, theme(), actualWidth, actualHeight, partialTick);
        graphics.pose().pushMatrix();
        graphics.pose().scale(canvasScale);
        UiInputGeometry.push(canvasScale, 0, 0);
        try {
            theme().panel(graphics, left, top, width, height);
            for (int i = 0; i < textTitle.size(); i++) {
                graphics.pose().pushMatrix();
                graphics.pose().translate(left + contentPadding, top + contentPadding + i * titleStep);
                graphics.pose().scale(titleSize / (float) Minecraft.getInstance().font.lineHeight);
                graphics.text(Minecraft.getInstance().font, textTitle.get(i), 0, 0, theme().ink(), false);
                graphics.pose().popMatrix();
            }
            if (screen instanceof DialogProgressHost progress) renderProgress(graphics, progress);
            for (AbstractWidget widget : widgets) {
                if (widget instanceof StringWidget && widget.getMessage().equals(screen.getTitle())) {
                    var title = MenuText.styled(screen.getTitle());
                    float size = titleSize;
                    float textScale = size / Minecraft.getInstance().font.lineHeight;
                    MenuText.extract(graphics, Minecraft.getInstance().font, title,
                        widget.getX() + (widget.getWidth() - Minecraft.getInstance().font.width(title) * textScale) / 2,
                        widget.getY() + (widget.getHeight() - size) / 2, size, theme().ink());
                } else if (paragraphs.contains(widget)) {
                    widget.extractRenderState(graphics, Math.round(mouseX / canvasScale), Math.round(mouseY / canvasScale), partialTick);
                } else {
                    widget.extractRenderState(graphics, Math.round(mouseX / canvasScale), Math.round(mouseY / canvasScale), partialTick);
                }
            }
            UiInputGeometry.flushPreedit(graphics, Math.round(mouseX / canvasScale), Math.round(mouseY / canvasScale), partialTick);
        } finally {
            UiInputGeometry.pop();
            graphics.pose().popMatrix();
        }
    }

    private void renderProgress(GuiGraphicsExtractor graphics, DialogProgressHost progress) {
        var font = Minecraft.getInstance().font;
        int x = left + 16;
        int available = width - 32;
        var status = MenuText.secondary(progress.rodel$progressStatus(), theme().muted());
        float size = Math.min(density().body(), available * font.lineHeight / (float) Math.max(1, font.width(status)));
        MenuText.extract(graphics, font, status,
            x + (available - font.width(status) * size / font.lineHeight) / 2, progressTop, size, theme().muted());
        int phase = (int) (Util.getMillis() / 220 % 4);
        int y = progressTop + 18;
        for (int i = 0; i < 4; i++) {
            int tileX = x + i % 2 * 6;
            int tileY = y + i / 2 * 6;
            graphics.fill(tileX, tileY, tileX + 5, tileY + 5, i == phase ? theme().primary() : theme().edge());
        }
        int barX = x + 22;
        int barWidth = available - 22;
        graphics.fill(barX, y + 3, barX + barWidth, y + 9, theme().edge());
        var value = progress.rodel$progressValue();
        if (value.isPresent()) {
            int filled = Math.round(barWidth * (float) Math.clamp(value.getAsDouble(), 0, 1));
            graphics.fill(barX, y + 3, barX + filled, y + 9, theme().primary());
        } else {
            int segment = Math.max(12, barWidth / 5);
            int offset = (int) (Util.getMillis() % 1400 * (barWidth - segment) / 1400);
            graphics.fill(barX + offset, y + 3, barX + offset + segment, y + 9, theme().primary());
        }
        var detail = MenuText.secondary(progress.rodel$progressDetail(), theme().muted());
        float detailSize = Math.min(density().detail(), available * font.lineHeight / (float) Math.max(1, font.width(detail)));
        MenuText.extract(graphics, font, detail,
            x + (available - font.width(detail) * detailSize / font.lineHeight) / 2,
            progressTop + 38, detailSize, theme().muted());
    }

    public MouseButtonEvent mouse(MouseButtonEvent event) {
        return new MouseButtonEvent(event.x() / canvasScale, event.y() / canvasScale, event.buttonInfo());
    }

    public double coordinate(double value) { return value / canvasScale; }
    @Override public float scale() { return 0.5f; }
    @Override public boolean compact() { return screen.width < 480; }
    @Override public MenuTheme theme() { return appearance.theme(); }
}
