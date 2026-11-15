package io.github.richasy.rodel.ui.worlds;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.IdentityHashMap;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuActionStyle;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.AbstractStringWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.MultiLineTextWidget;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import net.minecraft.util.FormattedCharSequence;

public final class WorldBranchPage implements WorldSurface {
    public interface NativeInputHost {
        void rodel$replaceInput(EditBox editor, NativeTextBox adapter);
    }

    public record NativeRowText(Component message, int x, int color) {}

    private final Screen screen;
    private final MenuContext appearance;
    private final boolean editor;
    private final List<AbstractWidget> retained;
    private final SettingsChoiceList choices = new SettingsChoiceList(this);
    private List<AbstractWidget> controls = List.of();
    private List<AbstractWidget> manuallyRendered = List.of();
    private final Map<EditBox, NativeTextBox> inputs = new IdentityHashMap<>();
    private List<Button> actions = List.of();
    private AbstractSelectionList<?> nativeList;
    private WorldBranchTextList messages;
    private float canvasScale = 1;
    private int actualWidth;
    private int actualHeight;
    private int left;
    private int top;
    private int width;
    private int bodyTop;
    private int bodyHeight;
    private int presetShareTop;
    private int presetListTop;
    private List<FormattedCharSequence> presetShareLines = List.of();
    private Component trimmedTooltip;

    public WorldBranchPage(Screen screen, MenuContext appearance, boolean editor) {
        this.screen = screen;
        this.appearance = appearance;
        this.editor = editor;
        retained = nativeWidgets();
    }

    public static WorldBranchPage get(Screen screen) {
        return WorldSurface.get(screen) instanceof WorldBranchPage page ? page : null;
    }

    public boolean owns(Object list) { return nativeList == list; }

    @Override public void prepare() {
        var window = Minecraft.getInstance().getWindow();
        actualWidth = window.getGuiScaledWidth();
        actualHeight = window.getGuiScaledHeight();
        canvasScale = MenuContext.designScale() * 2;
        screen.width = Math.round(actualWidth / canvasScale);
        screen.height = Math.round(actualHeight / canvasScale);
    }

    @Override public void beginInit() { prepare(); }

    private List<AbstractWidget> nativeWidgets() {
        List<AbstractWidget> widgets = new ArrayList<>(NativeWidgets.get(screen));
        // PresetFlatWorldScreen registers its export field with addWidget and renders it separately.
        for (var child : screen.children())
            if (child instanceof AbstractWidget widget && !widgets.contains(widget)) widgets.add(widget);
        return widgets.stream().filter(widget -> widget != choices && widget != messages)
            .map(widget -> widget instanceof NativeTextBox box ? box.editor() : widget).toList();
    }

    @Override public void initialized() {
        // Recovery owns its controls from construction, just like EditWorldScreen.
        var source = screen.getClass().getName().equals("net.minecraft.client.gui.screens.RecoverWorldDataScreen")
            ? retained : nativeWidgets();
        nativeList = source.stream().filter(AbstractSelectionList.class::isInstance)
            .map(widget -> (AbstractSelectionList<?>) widget).findFirst().orElse(null);
        actions = source.stream().filter(Button.class::isInstance).map(Button.class::cast).toList();
        controls = source.stream().filter(widget -> !(widget instanceof Button)
            && !(widget instanceof AbstractSelectionList<?>) && !(widget instanceof AbstractStringWidget)).toList();
        var nativeRenderables = NativeWidgets.get(screen).stream()
            .map(widget -> widget instanceof NativeTextBox box ? box.editor() : widget).toList();
        manuallyRendered = controls.stream().filter(widget -> !nativeRenderables.contains(widget)).toList();
        var focused = screen.getFocused();
        if (focused instanceof NativeTextBox box) focused = box.editor();
        inputs.clear();
        for (var control : controls) if (control instanceof EditBox editor) inputs.put(editor, new NativeTextBox(this, editor));
        MenuActionStyle.apply(this, screen, actions);
        List<AbstractWidget> paragraphs = new ArrayList<>();
        if (!editor) {
            paragraphs.add(new MultiLineTextWidget(screen.getTitle(), Minecraft.getInstance().font));
            source.stream().filter(AbstractStringWidget.class::isInstance)
                .filter(widget -> !widget.getMessage().equals(screen.getTitle())).forEach(paragraphs::add);
            if (screen instanceof WorldBranchTextHost text)
                paragraphs.add(new MultiLineTextWidget(text.rodel$branchMessage(), Minecraft.getInstance().font));
            if (screen instanceof WorldBranchProgressHost progress)
                paragraphs.add(new WorldBranchProgressWidget(progress.rodel$branchProgress(), this));
            messages = new WorldBranchTextList(this, paragraphs);
        }
        var widgets = NativeWidgets.get(screen);
        widgets.clear();
        if (nativeList != null) widgets.add(nativeList);
        if (messages != null) widgets.add(messages);
        for (var control : controls) {
            if (manuallyRendered.contains(control)) {
                if (control instanceof EditBox editor && screen instanceof NativeInputHost host)
                    host.rodel$replaceInput(editor, inputs.get(editor));
            } else widgets.add(displayControl(control));
        }
        widgets.addAll(actions);
        widgets.add(choices);
        layout();
        if (focused instanceof EditBox editor && inputs.containsKey(editor)) screen.setFocused(inputs.get(editor));
    }

    private AbstractWidget displayControl(AbstractWidget control) {
        return control instanceof EditBox editor ? inputs.get(editor) : control;
    }

    @Override public void layout() {
        int margin = compact() ? 16 : 30;
        width = Math.max(100, Math.min(screen.width - margin * 2, editor ? 520 : 440));
        left = (screen.width - width) / 2;
        top = compact() ? 16 : 24;
        int columns = Math.max(1, Math.min(actions.size(), width / 130));
        int rows = (actions.size() + columns - 1) / columns;
        int actionPitch = density().control() + density().gap();
        int actionTop = screen.height - top - rows * actionPitch;
        if (!editor && nativeList == null && messages != null && controls.isEmpty()) {
            bodyHeight = Math.min(Math.max(24, screen.height - top * 2 - rows * actionPitch - 12), messages.naturalHeight(width));
            top = Math.max(top, (screen.height - bodyHeight - rows * actionPitch - 12) / 2);
            bodyTop = top;
            actionTop = bodyTop + bodyHeight + 12;
            layoutActions(actionTop, columns);
            messages.resize(left, bodyTop, width, bodyHeight);
            return;
        }
        layoutActions(actionTop, columns);
        bodyTop = editor ? top + density().heading() + density().gap() * 2 : top;
        int y = bodyTop;
        presetShareLines = List.of();
        for (AbstractWidget control : controls) {
            boolean label = screen.getClass().getName().equals("net.minecraft.client.gui.screens.PresetFlatWorldScreen")
                && control instanceof EditBox;
            int controlTop = y + density().gap();
            if (label) {
                presetShareTop = controlTop;
                var font = Minecraft.getInstance().font;
                presetShareLines = font.split(MenuText.secondary(Component.translatable("createWorld.customize.presets.share")),
                    Math.max(1, Math.round((width - 24) * font.lineHeight / (float) density().detail())));
                controlTop += presetShareLines.size() * (density().detail() + 3) + density().gap();
            }
            var displayed = displayControl(control);
            displayed.setRectangle(width - 24, control instanceof EditBox ? density().control() : Math.max(density().control(), control.getHeight()),
                left + 12, controlTop);
            y = controlTop + displayed.getHeight() + density().gap();
        }
        bodyTop = y;
        if (screen.getClass().getName().equals("net.minecraft.client.gui.screens.PresetFlatWorldScreen")) {
            presetListTop = y;
            bodyTop += density().body() + density().gap();
        }
        bodyHeight = Math.max(24, actionTop - bodyTop - 12);
        if (nativeList != null)
            nativeList.updateSizeAndPosition(width, Math.max(12, bodyHeight - 12), left, bodyTop + 6);
        if (messages != null) messages.resize(left, bodyTop, width, bodyHeight);
    }

    private void layoutActions(int actionTop, int columns) {
        for (int i = 0; i < actions.size(); i++) {
            int count = Math.min(columns, actions.size() - i / columns * columns);
            int buttonWidth = Math.min(150, (width - (count - 1) * 8) / count);
            int x = left + (width - count * buttonWidth - (count - 1) * 8) / 2;
            actions.get(i).setRectangle(buttonWidth, density().control(), x + i % columns * (buttonWidth + 8),
                actionTop + i / columns * (density().control() + density().gap()));
        }
    }

    @Override public void render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        ((UiBackdrop) screen).rodel$backdrop(graphics, theme(), actualWidth, actualHeight, partialTick);
        graphics.pose().pushMatrix();
        graphics.pose().scale(canvasScale);
        UiInputGeometry.push(canvasScale, 0, 0);
        try {
            int x = (int) coordinate(mouseX), y = (int) coordinate(mouseY);
            trimmedTooltip = null;
            if (editor) text(graphics, screen.getTitle(), left, top + 4, density().heading(), theme().ink(), false);
            if (screen.getClass().getName().equals("net.minecraft.client.gui.screens.PresetFlatWorldScreen")) {
                var font = Minecraft.getInstance().font;
                for (int i = 0; i < presetShareLines.size(); i++) {
                    graphics.pose().pushMatrix();
                    graphics.pose().translate(left + 12, presetShareTop + i * (density().detail() + 3));
                    graphics.pose().scale(density().detail() / (float) font.lineHeight);
                    graphics.text(font, presetShareLines.get(i), 0, 0, theme().muted(), false);
                    graphics.pose().popMatrix();
                }
                text(graphics, Component.translatable("createWorld.customize.presets.list"), left, presetListTop,
                    density().body(), theme().muted(), true);
            }
            theme().panel(graphics, left, bodyTop, width, bodyHeight);
            for (AbstractWidget widget : NativeWidgets.get(screen)) widget.extractRenderState(graphics, x, y, partialTick);
            for (AbstractWidget widget : manuallyRendered) displayControl(widget).extractRenderState(graphics, x, y, partialTick);
            choices.overlay(graphics, x, y, partialTick);
            if (trimmedTooltip != null && !choices.visible) graphics.setTooltipForNextFrame(trimmedTooltip, x, y);
            UiInputGeometry.flushPreedit(graphics, x, y, partialTick);
        } finally {
            UiInputGeometry.pop();
            graphics.pose().popMatrix();
        }
    }

    public void refreshRows(AbstractSelectionList<?> list) {
        if (!owns(list)) return;
        boolean changed = false;
        for (var entry : list.children()) {
            int height = entry instanceof WorldBranchDetail detail ? detailHeight(detail, list.getRowWidth() - 4)
                : entry.getClass().getName().equals("net.minecraft.client.gui.screens.CreateBuffetWorldScreen$BiomeList$Entry")
                    || entry.getClass().getName().equals("net.minecraft.client.gui.screens.CreateFlatWorldScreen$DetailsList$HeaderEntry")
                    ? density().body() + density().gap() * 2 : entry.getHeight();
            if (entry.getHeight() != height) {
                entry.setHeight(height);
                changed = true;
            }
        }
        if (changed) {
            double scroll = list.scrollAmount();
            list.updateSizeAndPosition(list.getWidth(), list.getHeight(), list.getX(), list.getY());
            list.setScrollAmount(scroll);
        }
    }

    public void nativeRowText(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry,
                              List<NativeRowText> runs, boolean hovered) {
        var font = Minecraft.getInstance().font;
        int size = density().body();
        float textScale = size / (float) font.lineHeight;
        float y = entry.getY() + (entry.getHeight() - size) / 2f;
        int right = entry.getContentRight();
        boolean tooltip = hovered || entry.isFocused() && focusVisible();
        if (runs.size() > 1) {
            var run = runs.getLast();
            var label = fitLabel(MenuText.styled(run.message()), entry.getContentWidth() / 2f / textScale, tooltip);
            int x = right - (int) Math.ceil(font.width(label) * textScale);
            MenuText.extract(graphics, font, label, x, y, size, run.color() == -1 ? theme().ink() : theme().muted());
            right = x - density().gap();
        }
        if (!runs.isEmpty()) {
            var run = runs.getFirst();
            var label = fitLabel(MenuText.styled(run.message()), Math.max(1, right - run.x()) / textScale, tooltip);
            MenuText.extract(graphics, font, label, run.x(), y, size, run.color() == -1 ? theme().ink() : theme().muted());
        }
    }

    private int detailHeight(WorldBranchDetail detail, int width) {
        return density().inset() * 2 + detailLines(detail.rodel$branchHeading(), width, true).size() * (density().body() + 3)
            + density().gap() + detailLines(detail.rodel$branchDescription(), width, false).size() * (density().detail() + 3);
    }

    private List<FormattedCharSequence> detailLines(Component message, int width, boolean heading) {
        var font = Minecraft.getInstance().font;
        int size = heading ? density().body() : density().detail();
        return font.split(heading ? MenuText.styled(message) : MenuText.secondary(message),
            Math.max(1, Math.round(width * font.lineHeight / (float) size)));
    }

    public void detail(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, WorldBranchDetail detail) {
        int y = entry.getY() + density().inset();
        var font = Minecraft.getInstance().font;
        var messages = List.of(detail.rodel$branchHeading(), detail.rodel$branchDescription());
        for (int index = 0; index < messages.size(); index++) {
            boolean heading = index == 0;
            int size = heading ? density().body() : density().detail();
            for (var line : detailLines(messages.get(index), entry.getContentWidth(), heading)) {
                graphics.pose().pushMatrix();
                graphics.pose().translate(entry.getContentX(), y);
                graphics.pose().scale(size / (float) font.lineHeight);
                graphics.text(font, line, 0, 0, heading ? theme().ink() : theme().muted(), false);
                graphics.pose().popMatrix();
                y += size + 3;
            }
            y += density().gap();
        }
    }

    @Override public void text(GuiGraphicsExtractor graphics, Component value, float x, float y, float size, int color, boolean secondary) {
        MenuText.extract(graphics, Minecraft.getInstance().font,
            secondary ? MenuText.secondary(value) : MenuText.styled(value), x, y, size, color);
    }
    @Override public MenuTheme theme() { return appearance.theme(); }
    @Override public float scale() { return .5f; }
    @Override public boolean compact() { return screen.width < 480; }
    @Override public int p(float value) { return Math.round(value * scale()); }
    @Override public int controlTextSize() { return density().body() * 2; }
    @Override public Component controlText(AbstractWidget widget) { return widget.getMessage(); }
    @Override public Component fitLabel(Component full, float available, boolean tooltip) {
        var font = Minecraft.getInstance().font;
        if (font.width(full) <= available) return full;
        if (tooltip) contextualTooltip(full);
        return MenuText.styled(Component.literal(
            font.substrByWidth(full, Math.max(1, (int) available - font.width("…"))).getString() + "…"));
    }
    @Override public Component fitValue(AbstractWidget widget, Component full, float available) {
        return fitLabel(full, available, widget.isHovered() || widget.isFocused() && focusVisible());
    }
    @Override public void contextualTooltip(Component full) { trimmedTooltip = full; }
    @Override public void openChoices(AbstractWidget widget) { choices.open(widget, screen.width, screen.height); }
    @Override public SettingsChoiceList choices() { return choices; }
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
