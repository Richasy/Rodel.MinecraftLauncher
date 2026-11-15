package io.github.richasy.rodel.ui.settings;

import java.util.List;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.screens.packs.PackSelectionModel;
import net.minecraft.client.gui.screens.packs.TransferableSelectionList;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.client.gui.navigation.ScreenRectangle;
import net.minecraft.util.FormattedCharSequence;

public final class PackSettingsBody {
    private final NativeFormContext page;
    private final PackSettingsHost source;
    private final List<TransferableSelectionList> lists;
    private List<ScreenRectangle> browserPanes = List.of();
    private List<FormattedCharSequence> browserNotice = List.of();
    private ScreenRectangle browserBounds;
    private ScreenRectangle browserSearch;
    private Component browserMessage = Component.empty();
    private AbstractWidget browserDone;
    private int browserRows;
    private int browserHeight;
    private int noticeTop;

    public PackSettingsBody(NativeFormContext page, PackSettingsHost source) {
        this.page = page;
        this.source = source;
        lists = List.of(source.rodel$availablePacks(), source.rodel$selectedPacks());
        lists.get(0).setMessage(source.rodel$availableTitle());
        lists.get(1).setMessage(source.rodel$selectedTitle());
    }

    public List<TransferableSelectionList> widgets() { return lists; }
    public boolean owns(Object list) { return lists.contains(list); }
    public boolean ownsEntry(Object entry) { return lists.stream().anyMatch(list -> list.children().contains(entry)); }
    public int rowWidth(Object list) {
        var packList = (TransferableSelectionList) list;
        // Native pack entries subtract the scrollbar again; browser geometry already reserves its strip.
        int reservedScrollbar = browser() && packList.maxScrollAmount() > 0 ? packList.scrollbarWidth() : 0;
        return ((AbstractWidget) list).getWidth() - (browser() ? page.density().inset() : page.p(12))
            + (browser() ? 2 * AbstractSelectionList.Entry.CONTENT_PADDING : 0) + reservedScrollbar;
    }
    public boolean browser() { return !browserPanes.isEmpty(); }
    public int rowLeft(Object list) { return ((AbstractWidget) list).getX() - AbstractSelectionList.Entry.CONTENT_PADDING; }
    public int browserHeight() { return browserHeight; }

    public void layoutBrowser(int x, int y, int width, int height, Component notice) {
        layoutBrowser(x, y, width, height, notice, null);
    }

    public void layoutBrowser(int x, int y, int width, int height, Component notice, AbstractWidget done) {
        browserBounds = new ScreenRectangle(x, y, width, height);
        browserMessage = notice;
        browserDone = done;
        // Filtering changes the visible entries, not the amount of space reserved for their model.
        browserRows = Math.max(1, source.rodel$packRows());
        int gap = page.density().gap();
        int inset = page.density().inset();
        int controlHeight = page.density().control();
        browserSearch = new ScreenRectangle(x, y, width, controlHeight);
        // Density follows the GUI budget; columns follow the actual reading width.
        boolean stacked = width < 2 * (page.density().tight() ? 144 : 200) + gap;
        int paneWidth = stacked ? width : (width - gap) / 2;
        var font = Minecraft.getInstance().font;
        int textSize = page.density().detail();
        int doneWidth = done == null ? 0 : Math.min(width / 3, page.density().tight() ? 80 : 120);
        int noticeWidth = width - (done == null ? 0 : doneWidth + gap * 2);
        browserNotice = notice.getString().isBlank() ? List.of()
            : font.split(MenuText.secondary(notice, page.theme().muted()),
                Math.max(1, Math.round(noticeWidth * font.lineHeight / (float) textSize)));
        int noticeHeight = browserNotice.isEmpty() ? 0 : textSize + (browserNotice.size() - 1) * (textSize + 3);
        int footerHeight = Math.max(done == null ? 0 : controlHeight, noticeHeight);
        int footerGap = footerHeight == 0 ? 0 : gap * 2;
        int panesTop = y + controlHeight + gap;
        int panesBudget = Math.max(1, height - controlHeight - gap - footerGap - footerHeight);
        // The row owns its content inset; account for the list's own leading padding once.
        int toolbarHeight = inset + page.density().body() - AbstractSelectionList.Entry.CONTENT_PADDING;
        // AbstractSelectionList keeps four pixels after its entries even when the list does not scroll.
        long naturalHeight = (long) toolbarHeight + (long) browserRows * page.density().row() + 4 + inset;
        int paneBudget = stacked ? Math.max(1, (panesBudget - gap) / 2) : panesBudget;
        int paneHeight = (int) Math.min(paneBudget, naturalHeight);
        browserPanes = List.of(new ScreenRectangle(x, panesTop, paneWidth, paneHeight),
            new ScreenRectangle(stacked ? x : x + paneWidth + gap,
                stacked ? panesTop + paneHeight + gap : panesTop, paneWidth, paneHeight));
        for (int i = 0; i < lists.size(); i++) {
            var pane = browserPanes.get(i);
            var list = lists.get(i);
            rows(list);
            double scroll = list.scrollAmount();
            list.updateSizeAndPosition(pane.width() - inset, Math.max(1, pane.height() - toolbarHeight - inset),
                pane.left() + inset, pane.top() + toolbarHeight);
            list.setScrollAmount(scroll);
        }
        int footerTop = browserPanes.getLast().bottom() + footerGap;
        noticeTop = footerTop + (footerHeight - noticeHeight) / 2;
        if (done != null) done.setRectangle(doneWidth, controlHeight, x + width - doneWidth,
            footerTop + (footerHeight - controlHeight) / 2);
        browserHeight = footerTop + footerHeight - y;
    }

    public void placeSearch(AbstractWidget search) {
        search.setRectangle(browserSearch.width(), browserSearch.height(), browserSearch.left(), browserSearch.top());
    }

    public void layout(int x, int y, int width, int height) {
        browserPanes = List.of();
        int gap = page.p(16);
        int inset = page.p(24);
        int paneWidth = page.compact() ? width - inset * 2 : (width - inset * 2 - gap) / 2;
        int paneHeight = page.compact() ? (height - gap) / 2 : height;
        for (int i = 0; i < lists.size(); i++) {
            TransferableSelectionList list = lists.get(i);
            rows(list);
            double scroll = list.scrollAmount();
            list.updateSizeAndPosition(paneWidth, Math.max(page.p(20), paneHeight - page.p(44)),
                x + inset + (page.compact() ? 0 : i * (paneWidth + gap)),
                y + page.p(44) + (page.compact() ? i * (paneHeight + gap) : 0));
            list.setScrollAmount(scroll);
        }
    }

    private boolean rows(TransferableSelectionList list) {
        boolean changed = false;
        var old = List.copyOf(list.children());
        var content = old.stream().filter(entry -> !(entry instanceof TransferableSelectionList.HeaderEntry)).toList();
        if (content.size() != old.size()) {
            var selected = list.getSelected();
            var focused = list.getFocused();
            double scroll = list.scrollAmount();
            list.replaceEntries(content);
            if (selected != null && content.contains(selected)) list.setSelected(selected);
            if (focused != null && content.contains(focused)) list.setFocused(focused);
            list.setScrollAmount(scroll);
            changed = true;
        }
        for (var entry : list.children()) {
            if (entry instanceof TransferableSelectionList.PackEntry && entry.getHeight() != page.density().row()) {
                entry.setHeight(page.density().row());
                changed = true;
            }
        }
        return changed;
    }

    public void chrome(GuiGraphicsExtractor graphics) {
        if (browser() && browserRows != Math.max(1, source.rodel$packRows())) {
            layoutBrowser(browserBounds.left(), browserBounds.top(), browserBounds.width(), browserBounds.height(),
                browserMessage, browserDone);
        }
        for (int i = 0; i < lists.size(); i++) {
            TransferableSelectionList list = lists.get(i);
            if (rows(list)) {
                double scroll = list.scrollAmount();
                list.updateSizeAndPosition(list.getWidth(), list.getHeight(), list.getX(), list.getY());
                list.setScrollAmount(scroll);
            }
            if (browser()) {
                var pane = browserPanes.get(i);
                graphics.fill(pane.left(), pane.top(), pane.right(), pane.bottom(), page.theme().surface());
                graphics.outline(pane.left(), pane.top(), pane.width(), pane.height(), page.theme().edge());
                Component title = i == 0 ? source.rodel$availableTitle() : source.rodel$selectedTitle();
                MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(title),
                    pane.left() + page.density().inset(),
                    pane.top() + page.density().inset(),
                    page.density().body(), page.theme().ink());
                continue;
            }
            int top = list.getY() - page.p(44);
            graphics.fill(list.getX(), top, list.getRight(), list.getBottom(), page.theme().surface());
            graphics.outline(list.getX(), top, list.getWidth(), list.getHeight() + page.p(44), page.theme().edge());
            Component title = i == 0 ? source.rodel$availableTitle() : source.rodel$selectedTitle();
            MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(title),
                list.getX() + page.p(16), top + page.p(16), page.p(16), page.theme().ink());
        }
        if (browser()) {
            int size = page.density().detail();
            int step = size + 3;
            for (int i = 0; i < browserNotice.size(); i++) {
                graphics.pose().pushMatrix();
                graphics.pose().translate(browserBounds.left(), noticeTop + i * step);
                graphics.pose().scale(size / (float) Minecraft.getInstance().font.lineHeight);
                graphics.text(Minecraft.getInstance().font, browserNotice.get(i), 0, 0, page.theme().muted(), false);
                graphics.pose().popMatrix();
            }
        }
    }

    public void row(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, PackSelectionModel.Entry model,
             boolean hovered, int mouseX, int mouseY) {
        int x = entry.getContentX();
        int y = entry.getY();
        int width = entry.getContentWidth();
        int icon = page.density().icon();
        int iconX = x + page.density().inset();
        int iconY = y + page.density().inset();
        int rowHeight = entry.getHeight();
        boolean selected = lists.stream().anyMatch(list -> list.getSelected() == entry);
        boolean focused = entry.isFocused() && page.focusVisible();
        if (selected) {
            graphics.fill(x, y, x + width, y + rowHeight, page.theme().selection());
            graphics.outline(x, y, width, rowHeight, page.theme().edge());
        }
        if (focused) {
            int inset = page.p(2);
            graphics.outline(x + inset, y + inset, width - inset * 2, rowHeight - inset * 2, page.theme().focus());
        }
        graphics.blit(RenderPipelines.GUI_TEXTURED, model.getIconTexture(), iconX, iconY, 0f, 0f,
            icon, icon, 32, 32, 32, 32);
        int textX = iconX + icon + (page.density().tight() ? 8 : page.p(24));
        int textWidth = Math.max(page.p(12), width - (textX - x) - page.density().inset());
        float labelScale = page.density().body() / (float) Minecraft.getInstance().font.lineHeight;
        Component name = page.fitLabel(MenuText.styled(model.getTitle()), textWidth / labelScale, hovered || focused);
        MenuText.extract(graphics, Minecraft.getInstance().font, name,
            textX, y + (page.density().tight() ? 6 : page.p(18)), page.density().body(), page.theme().ink());
        Component description = model.getCompatibility().isCompatible()
            ? model.getExtendedDescription() : model.getCompatibility().getDescription();
        var font = Minecraft.getInstance().font;
        float detailScale = page.density().detail() / (float) font.lineHeight;
        Component full = MenuText.secondary(Component.literal(description.getString()));
        int available = Math.max(1, Math.round(textWidth / detailScale));
        var lines = font.split(full, available);
        boolean trimmed = lines.size() > 2;
        if (trimmed) lines = font.split(full, Math.max(1, available - font.width("…")));
        for (int i = 0; i < Math.min(2, lines.size()); i++) {
            graphics.pose().pushMatrix();
            graphics.pose().translate(textX, y + (page.density().tight() ? 15 + i * 7 : page.p(42 + i * 16)));
            graphics.pose().scale(detailScale);
            graphics.text(font, lines.get(i), 0, 0, model.getCompatibility().isCompatible() ? page.theme().muted() : page.theme().focus(), false);
            if (trimmed && i == 1) graphics.text(font, "…", font.width(lines.get(i)), 0, page.theme().muted(), false);
            graphics.pose().popMatrix();
        }
        if (trimmed && (hovered || focused)) page.contextualTooltip(full);
        boolean iconHover = mouseX >= iconX && mouseX < iconX + icon && mouseY >= iconY && mouseY < iconY + icon;
        if ((iconHover || focused) && (!model.isFixedPosition() || !model.isRequired())) {
            graphics.fill(iconX, iconY, iconX + icon, iconY + icon, 0xB0000000);
            if (model.canSelect()) {
                arrow(graphics, iconX + icon / 2f, iconY + icon / 2f, 0);
                if (iconHover) page.contextualTooltip(Component.translatable("rodel.packs.enable"));
            } else {
                if (model.canUnselect()) arrow(graphics, iconX + icon / 4f, iconY + icon / 2f, (float) Math.PI);
                if (model.canMoveUp()) arrow(graphics, iconX + icon * .75f, iconY + icon / 4f, -(float) Math.PI / 2);
                if (model.canMoveDown()) arrow(graphics, iconX + icon * .75f, iconY + icon * .75f, (float) Math.PI / 2);
                if (iconHover) {
                    String key = mouseX < iconX + icon / 2 ? model.canUnselect() ? "rodel.packs.disable" : null
                        : mouseY < iconY + icon / 2 ? model.canMoveUp() ? "rodel.packs.up" : null
                        : model.canMoveDown() ? "rodel.packs.down" : null;
                    if (key != null) page.contextualTooltip(Component.translatable(key));
                }
            }
        }
    }

    private void arrow(GuiGraphicsExtractor graphics, float x, float y, float angle) {
        int size = page.p(20);
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().rotate(angle);
        MenuIcon.ARROW.extract(graphics, -size / 2f, -size / 2f, size, page.theme().highContrast() ? page.theme().ink() : 0xFFFFFFFF);
        graphics.pose().popMatrix();
    }
}
