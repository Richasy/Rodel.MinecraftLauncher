package io.github.richasy.rodel.ui.menus;

import java.util.List;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.social.SocialInteractionsPlayerList;
import net.minecraft.network.chat.Component;

/** Social filtering and player actions stay on their native screen, list and controls. */
public final class SocialMenuPage extends NativeMenuPage {
    public interface Access {
        SocialInteractionsPlayerList rodel$players();
        EditBox rodel$search();
        Button rodel$all();
        Button rodel$hidden();
        Button rodel$blocked();
        Button rodel$blockingHint();
        void rodel$replaceSearch(NativeTextBox adapter);
    }

    private PageHeader header;
    private int left;
    private int width;
    private int bodyTop;
    private int bodyHeight;
    private NativeTextBox searchAdapter;

    public SocialMenuPage(Screen screen, MenuContext appearance) { super(screen, appearance); }

    private Access access() { return (Access) screen(); }

    @Override public void initialized() {
        var access = access();
        if (access.rodel$players() == null || access.rodel$search() == null || access.rodel$blockingHint() == null) return;
        int gap = density().gap();
        int inset = density().inset();
        int control = density().control();
        left = inset;
        width = Math.max(0, screen().width - inset * 2);
        header = PageHeader.measure(density(), left, inset, width, true, false);
        int tabsTop = header.top() + header.height() + gap;
        List<Button> tabs = List.of(access.rodel$all(), access.rodel$hidden(), access.rodel$blocked());
        int tabWidth = Math.max(0, (width - gap * 2) / 3);
        for (int i = 0; i < tabs.size(); i++) tabs.get(i).setRectangle(tabWidth, control,
            left + i * (tabWidth + gap), tabsTop);
        bodyTop = tabsTop + control + gap;
        int footer = screen().height - inset - control;
        bodyHeight = Math.max(0, footer - gap - bodyTop);
        if (searchAdapter == null || searchAdapter.editor() != access.rodel$search()) {
            searchAdapter = new NativeTextBox(this, access.rodel$search());
            access.rodel$replaceSearch(searchAdapter);
        }
        searchAdapter.setRectangle(Math.max(0, width - inset * 2), control, left + inset, bodyTop + inset);
        var players = access.rodel$players();
        for (var entry : players.children()) entry.setHeight(density().row());
        double scroll = players.scrollAmount();
        int listTop = searchAdapter.getBottom() + gap;
        players.updateSizeAndPosition(searchAdapter.getWidth(), Math.max(0, bodyTop + bodyHeight - inset - listTop), searchAdapter.getX(), listTop);
        players.setScrollAmount(scroll);
        Button done = null;
        for (AbstractWidget widget : NativeWidgets.get(screen())) {
            if (widget instanceof StringWidget && widget.getMessage().equals(screen().getTitle())) widget.visible = false;
            if (widget instanceof Button button && SettingsText.hasKey(button.getMessage(), "gui.done")) done = button;
        }
        int doneWidth = done == null ? 0 : Math.min(width, actionWidth(done));
        if (done != null) {
            done.setRectangle(doneWidth, control, left + width - doneWidth, footer);
        }
        Button hint = access.rodel$blockingHint();
        hint.visible = access.rodel$blocked().getMessage().getStyle().isUnderlined();
        hint.setRectangle(Math.min(actionWidth(hint), Math.max(0, width - doneWidth - gap)), control, left, footer);
        style(screen());
        if (done != null) ((ThemedButton) done).rodel$setStyle(this, null, true, false, false);
        if (screen().getFocused() == access.rodel$search()) screen().setFocused(searchAdapter);
    }

    private int actionWidth(Button button) {
        var font = Minecraft.getInstance().font;
        return (int) Math.ceil(font.width(MenuText.styled(button.getMessage())) * density().body() / (float) font.lineHeight) + p(28);
    }

    @Override public Component controlText(AbstractWidget widget) {
        var access = access();
        return widget == access.rodel$all() || widget == access.rodel$hidden() || widget == access.rodel$blocked()
            ? widget.getMessage().copy().withStyle(style -> style.withUnderlined(false)) : super.controlText(widget);
    }

    public boolean drawTab(AbstractWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        if (!rendering()) return false;
        var access = access();
        if (widget != access.rodel$all() && widget != access.rodel$hidden() && widget != access.rodel$blocked()) return false;
        MenuTabSurface.draw(this, graphics, widget, controlText(widget), widget.getMessage().getStyle().isUnderlined());
        return true;
    }

    @Override protected void panels(GuiGraphicsExtractor graphics) {
        if (header == null) return;
        theme().panel(graphics, left, bodyTop, width, bodyHeight);
        beginNativeText();
        try { header.draw(this, graphics, screen().getTitle(), null); }
        finally { endNativeText(); }
    }

    public void context(GuiGraphicsExtractor graphics, Component text, int mouseX, int mouseY) {
        if (header != null) NetworkRowLayout.text(this, graphics, text, header.textX(), header.subtitleY(),
            width, density().detail(), theme().muted(), mouseX, mouseY);
    }

    public void empty(GuiGraphicsExtractor graphics, Component text) {
        var players = access().rodel$players();
        var font = Minecraft.getInstance().font;
        int size = density().body();
        var lines = font.split(MenuText.secondary(text), Math.max(1, players.getWidth() * font.lineHeight / size));
        int stride = size + density().gap() / 2;
        int y = players.getY() + (players.getHeight() - lines.size() * stride) / 2;
        beginNativeText();
        try {
            for (var line : lines) {
                graphics.pose().pushMatrix();
                try {
                    graphics.pose().translate(players.getX() + (players.getWidth() - font.width(line) * size / (float) font.lineHeight) / 2, y);
                    graphics.pose().scale(size / (float) font.lineHeight);
                    graphics.text(font, line, 0, 0, theme().muted(), false);
                } finally { graphics.pose().popMatrix(); }
                y += stride;
            }
        } finally { endNativeText(); }
    }
}
