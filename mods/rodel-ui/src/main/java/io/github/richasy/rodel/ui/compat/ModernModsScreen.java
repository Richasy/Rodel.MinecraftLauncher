package io.github.richasy.rodel.ui.compat;

import com.mojang.blaze3d.platform.InputConstants;
import com.terraformersmc.modmenu.config.ModMenuConfig;
import com.terraformersmc.modmenu.gui.ModsScreen;
import com.terraformersmc.modmenu.gui.widget.DescriptionListWidget;
import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import com.terraformersmc.modmenu.gui.widget.entries.ModListEntry;
import com.terraformersmc.modmenu.util.ModMenuScreenTexts;
import com.terraformersmc.modmenu.util.mod.Mod;
import com.terraformersmc.modmenu.util.mod.ModBadgeRenderer;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.api.RodelUi;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ModernScreen;
import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.navigation.ScreenRectangle;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.PreeditEvent;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.CommonComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.FormattedText;
import net.minecraft.locale.Language;

/**
 * Only the browser's layout and paint are owned here. Mod Menu still owns discovery,
 * filtering, hierarchy, rich metadata, configuration factories and file-drop confirmation.
 */
public final class ModernModsScreen extends ModsScreen implements ModernScreen, ModBrowserPresentation {
    private final MenuContext appearance;
    private final MenuContext controls = new MenuContext() {
        @Override public float scale() { return .5f; }
        @Override public boolean compact() { return canvasWidth < 540; }
        @Override public MenuTheme theme() { return appearance.theme(); }
    };
    private final List<Button> buttons = new ArrayList<>();
    private Button back;
    private Button folder;
    private NativeTextBox searchBox;
    private float canvasScale = 1;
    private int canvasWidth;
    private int canvasHeight;
    private int outer;
    private int leftWidth;
    private int rightX;
    private int rightWidth;
    private int listTop;
    private int countTop;
    private int panelBottom;
    private int detailTop;
    private int panelTop;
    private int metadataTop;
    private int authorTop;
    private int badgeTop;
    private int detailsHeadingTop;
    private boolean canvasActive;

    private ModernModsScreen(Screen parent, MenuContext appearance) {
        super(parent);
        this.appearance = appearance;
    }

    public static Screen modernize(Screen parent, Screen original) {
        if (!UiConfig.enabled(Effect.MOD_BROWSER) || original == null || original.getClass() != ModsScreen.class)
            return original;
        MenuContext appearance = UiContexts.owned(parent);
        if (appearance == null && parent != null
            && (parent.getClass() == net.minecraft.client.gui.screens.TitleScreen.class && !UiConfig.enabled(Effect.TITLE)
                || parent.getClass() == net.minecraft.client.gui.screens.PauseScreen.class && !UiConfig.enabled(Effect.MENUS)
                || parent.getClass() == net.minecraft.client.gui.screens.options.OptionsScreen.class && !UiConfig.enabled(Effect.SETTINGS))) {
            appearance = new TitleMenu(Minecraft.getInstance(), parent.width, parent.height);
        }
        return appearance == null ? original : new ModernModsScreen(parent, appearance);
    }

    static Screen create(Screen parent, Screen original) {
        if (!UiConfig.enabled(Effect.MOD_BROWSER)) return original;
        Screen modern = modernize(parent, original);
        return modern == original && original.getClass() == ModsScreen.class
            ? new ModernModsScreen(parent, new TitleMenu(Minecraft.getInstance(), parent.width, parent.height))
            : modern;
    }

    private ModBrowserAccess nativeUi() { return (ModBrowserAccess) (Object) this; }

    @Override
    public MenuContext rodel$modernContext() { return controls; }

    public MenuTheme theme() { return appearance.theme(); }

    @Override
    protected void init() {
        canvasScale = MenuContext.designScale() * 2;
        canvasWidth = Math.round(width / canvasScale);
        canvasHeight = Math.round(height / canvasScale);
        int actualWidth = width;
        int actualHeight = height;
        // Native controllers are initialized in the same units as their widgets and input.
        width = canvasWidth;
        height = canvasHeight;
        try {
            super.init();
        } finally {
            width = actualWidth;
            height = actualHeight;
        }
        buttons.clear();
        back = null;
        folder = null;
        for (var child : children()) {
            if (child instanceof Button button) {
                buttons.add(button);
                if (button.getMessage().equals(CommonComponents.GUI_DONE)) back = button;
                if (button.getMessage().equals(ModMenuScreenTexts.MODS_FOLDER)) folder = button;
            }
        }
        if (back != null) back.setMessage(CommonComponents.GUI_BACK);

        AbstractWidget configure = nativeUi().rodel$configure();
        if (configure != null && configure.getMessage().getString().isBlank()) {
            configure.setMessage(ModMenuScreenTexts.CONFIGURE);
        }
        nativeUi().rodel$search().setTextShadow(false);
        nativeUi().rodel$search().setHint(ModMenuScreenTexts.SEARCH);
        removeWidget(nativeUi().rodel$search());
        searchBox = addRenderableWidget(new NativeTextBox(controls, nativeUi().rodel$search()));
        setInitialFocus(searchBox);
        layout();
        ModListEntry selected = getSelectedEntry();
        if (selected != null) {
            var description = nativeUi().rodel$description();
            double scroll = description.scrollAmount();
            description.updateSelectedMod(selected.getMod());
            description.setScrollAmount(scroll);
        }
        updateAvailability();
    }

    private void layout() {
        ModBrowserAccess ui = nativeUi();
        var density = controls.density();
        int control = density.control();
        int inset = density.inset();
        int gap = density.gap();
        outer = density.tight() ? 12 : 24;
        int contentWidth = canvasWidth - outer * 2;
        leftWidth = Math.round((contentWidth - gap) * .46f);
        rightX = outer + leftWidth + gap;
        rightWidth = contentWidth - leftWidth - gap;
        int headerTop = outer;
        panelTop = headerTop + control + gap;
        place(back, outer, headerTop, control, control);
        int folderWidth = actionWidth(folder);
        place(folder, canvasWidth - outer - folderWidth, headerTop, folderWidth, control);
        int searchWidth = leftWidth - inset * 2;
        if (ui.rodel$filters() != null) searchWidth -= control + gap;
        place(searchBox, outer + inset, panelTop + inset, searchWidth, control);
        place(ui.rodel$filters(), outer + leftWidth - inset - control, panelTop + inset, control, control);
        int nextY = panelTop + inset + control + gap;
        place(ui.rodel$sorting(), outer + inset, nextY, leftWidth - inset * 2, control);
        place(ui.rodel$libraries(), outer + inset, nextY + control + gap, leftWidth - inset * 2, control);
        if (ui.rodel$sorting().visible) nextY += (control + gap) * 2;
        countTop = nextY;
        listTop = countTop + density.detail() + gap;
        int bottom = canvasHeight - outer - density.detail() - gap;
        ModListWidget list = ui.rodel$list();
        int listX = outer + inset;
        int listWidth = leftWidth - inset * 2;
        int listHeight = Math.max(1, bottom - inset - listTop);
        if (list.getX() != listX || list.getY() != listTop || list.getWidth() != listWidth
            || list.getHeight() != listHeight) {
            double scroll = list.scrollAmount();
            list.updateSizeAndPosition(listWidth, listHeight, listX, listTop);
            list.setScrollAmount(scroll);
        }
        panelBottom = bottom;
        metadataTop = panelTop + inset;
        ModListEntry selected = getSelectedEntry();
        int metadataHeight = Math.max(density.icon(), density.body() + density.detail() * 2 + 4);
        nextY = metadataTop + metadataHeight + gap;
        authorTop = nextY;
        if (selected != null && !selected.getMod().getAuthors().isEmpty()) nextY += density.detail() + gap;
        badgeTop = nextY;
        if (selected != null && !ModMenuConfig.HIDE_BADGES.getValue()) nextY += density.detail() + 2 + gap;
        int x = rightX + inset;
        int actionTop = nextY;
        boolean hasActions = false;
        for (AbstractWidget button : new AbstractWidget[] {ui.rodel$configure(), ui.rodel$website(), ui.rodel$issues()}) {
            if (button == null || !button.visible) continue;
            int actionWidth = actionWidth(button);
            if (x > rightX + inset && x + actionWidth > rightX + rightWidth - inset) {
                x = rightX + inset;
                nextY += control + gap;
            }
            place(button, x, nextY, actionWidth, control);
            x += actionWidth + gap;
            hasActions = true;
        }
        detailsHeadingTop = (hasActions ? nextY + control : actionTop) + gap;
        detailTop = detailsHeadingTop + density.body() + gap;
        DescriptionListWidget description = ui.rodel$description();
        int descriptionHeight = Math.max(1, bottom - inset - detailTop);
        if (description.getX() != rightX + 1 || description.getY() != detailTop
            || description.getWidth() != rightWidth - 2 || description.getHeight() != descriptionHeight) {
            double scroll = description.scrollAmount();
            description.updateSizeAndPosition(rightWidth - 2, descriptionHeight, rightX + 1, detailTop);
            if (selected != null) description.updateSelectedMod(selected.getMod());
            description.setScrollAmount(scroll);
        }
    }
    private int actionWidth(AbstractWidget button) {
        if (button == null) return 0;
        return Math.min(rightWidth - 12, (int) Math.ceil(font.width(actionLabel(button.getMessage()))
            * controls.density().body() / (float) font.lineHeight) + 16);
    }

    private static void place(AbstractWidget widget, int x, int y, int width, int height) {
        if (widget != null) widget.setRectangle(width, height, x, y);
    }

    private void updateAvailability() {
        ModBrowserAccess ui = nativeUi();
        ModListEntry entry = getSelectedEntry();
        if (entry == null) {
            if (ui.rodel$configure() != null) ui.rodel$configure().visible = false;
            ui.rodel$website().visible = false;
            ui.rodel$issues().visible = false;
            return;
        }
        if (ui.rodel$configure() != null) {
            String id = entry.getMod().getId();
            ui.rodel$configure().active = getModHasConfigScreen(id);
            ui.rodel$configure().visible = true;
        }
    }

    @Override
    public void extractBackground(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        ((UiBackdrop) (Object) this).rodel$backdrop(graphics, theme(), width, height, partialTick);
    }

    @Override
    public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        updateAvailability();
        layout();
        int x = Math.round(mouseX / canvasScale);
        int y = Math.round(mouseY / canvasScale);
        graphics.pose().pushMatrix();
        graphics.pose().scale(canvasScale);
        canvasActive = true;
        UiInputGeometry.push(canvasScale, 0, 0);
        try {
            MenuTheme theme = theme();
            int headingX = outer + controls.density().control() + controls.density().gap();
            drawText(graphics, getTitle(), headingX,
                outer + (controls.density().control() - controls.density().heading()) / 2,
                folder.getX() - controls.density().gap() - headingX, theme.ink(), x, y, false, controls.density().heading());
            panel(graphics, outer, panelTop, leftWidth, panelBottom - panelTop);
            panel(graphics, rightX, panelTop, rightWidth, panelBottom - panelTop);
            secondaryText(graphics, nativeUi().rodel$count(true, false), outer + controls.density().inset(), countTop,
                leftWidth - 16, theme.muted(), x, y);
            ModListEntry selected = getSelectedEntry();
            if (selected != null) details(graphics, selected, x, y);
            else secondaryText(graphics, label("Select a mod to view its details", "选择模组以查看详情"),
                rightX + 8, 64, rightWidth - 16, theme.muted(), x, y);
            if (!ModMenuConfig.DISABLE_DRAG_AND_DROP.getValue()) {
                secondaryText(graphics, Component.empty().append(ModMenuScreenTexts.DROP_INFO_LINE_1).append(" ")
                    .append(ModMenuScreenTexts.DROP_INFO_LINE_2), outer, canvasHeight - outer - controls.density().detail(),
                    canvasWidth - outer * 2, theme.muted(), x, y);
            }
            for (var child : children()) {
                if (child instanceof AbstractWidget widget && widget.visible) {
                    if (widget == nativeUi().rodel$description() && selected == null) continue;
                    widget.extractRenderState(graphics, x, y, partialTick);
                }
            }
            // Vanilla defers both tooltip and IME paint; flush before leaving their coordinate space.
            graphics.extractDeferredElements(x, y, partialTick);
            graphics.setPreeditOverlay(null);
        } finally {
            UiInputGeometry.pop();
            canvasActive = false;
            graphics.pose().popMatrix();
        }
    }

    private void panel(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(.5f);
        graphics.fill(0, 0, width * 2, height * 2, theme().surface());
        graphics.outline(0, 0, width * 2, height * 2, theme().edge());
        graphics.pose().popMatrix();
    }

    private void details(GuiGraphicsExtractor graphics, ModListEntry entry, int mouseX, int mouseY) {
        Mod mod = entry.getMod();
        boolean tight = controls.density().tight();
        int x = rightX + (tight ? 6 : 8);
        int icon = controls.density().icon();
        graphics.blit(RenderPipelines.GUI_TEXTURED, entry.getIconTexture(), x, metadataTop, 0, 0,
            icon, icon, icon, icon, -1);
        var density = controls.density();
        int textX = x + icon + density.gap();
        int textWidth = rightX + rightWidth - density.inset() - textX;
        text(graphics, Component.literal(mod.getTranslatedName()), textX, metadataTop, textWidth,
            theme().ink(), mouseX, mouseY);
        secondaryText(graphics, Component.literal(mod.getId()), textX, metadataTop + density.body() + 2, textWidth,
            theme().muted(), mouseX, mouseY);
        if (mod.isReal()) secondaryText(graphics, Component.literal(mod.getPrefixedVersion()), textX,
            metadataTop + density.body() + density.detail() + 4, textWidth, theme().muted(), mouseX, mouseY);
        if (!mod.getAuthors().isEmpty()) {
            secondaryText(graphics, Component.translatable("modmenu.authorPrefix", String.join(", ", mod.getAuthors())),
                x, authorTop, rightWidth - 16, theme().muted(), mouseX, mouseY);
        }
        if (!ModMenuConfig.HIDE_BADGES.getValue()) {
            new ModBadgeRenderer(x, badgeTop, rightX + rightWidth - 8, mod, this).draw(graphics, mouseX, mouseY);
        }
        Throwable error = modScreenErrors.get(mod.getId());
        if (error != null) {
            text(graphics, ModMenuScreenTexts.configureError(mod.getId(), error), x, detailsHeadingTop,
                rightWidth - 16, theme().ink(), mouseX, mouseY);
        } else {
            Component heading = getModHasConfigScreen(mod.getId()) || nativeUi().rodel$configure() == null
                ? label("Mod details", "模组详情")
                : label("No configuration screen provided", "此模组未提供配置界面");
            text(graphics, heading, x, detailsHeadingTop, rightWidth - 16,
                theme().muted(), mouseX, mouseY);
        }
    }

    private Component label(String english, String chinese) {
        return Component.literal(minecraft.getLanguageManager().getSelected().startsWith("zh") ? chinese : english);
    }

    private void text(GuiGraphicsExtractor graphics, Component full, int x, int y, int width, int color,
                      int mouseX, int mouseY) {
        drawText(graphics, full, x, y, width, color, mouseX, mouseY, false);
    }

    private void secondaryText(GuiGraphicsExtractor graphics, Component full, int x, int y, int width, int color,
                               int mouseX, int mouseY) {
        drawText(graphics, full, x, y, width, color, mouseX, mouseY, true);
    }

    private void drawText(GuiGraphicsExtractor graphics, Component full, int x, int y, int width, int color,
                          int mouseX, int mouseY, boolean secondary) {
        drawText(graphics, full, x, y, width, color, mouseX, mouseY, secondary,
            secondary ? controls.density().detail() : controls.density().body());
    }

    private void drawText(GuiGraphicsExtractor graphics, Component full, int x, int y, int width, int color,
                          int mouseX, int mouseY, boolean secondary, int size) {
        Component readable = full.getString().contains("\n")
            ? Component.literal(full.getString().replace('\n', ' ')) : full;
        if (secondary) readable = MenuText.secondary(readable);
        var layout = RodelUi.fitText(font, readable, size, Math.max(0, width));
        RodelUi.drawText(graphics, layout, x, y, color);
        if (layout.trimmed() && mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + size)
            graphics.setTooltipForNextFrame(secondary ? MenuText.secondary(full) : full, mouseX, mouseY);
    }

    @Override
    public boolean rodel$drawWidget(AbstractWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        if (!(widget instanceof Button button) || !buttons.contains(button)) return false;
        MenuTheme theme = theme();
        boolean highlighted = button.isActive() && (button.isHovered() || button.isFocused() && controls.focusVisible());
        boolean pressed = highlighted && (button.isHovered() && minecraft.mouseHandler.isLeftPressed()
            || button.isFocused() && controls.focusVisible() && (
                InputConstants.isKeyDown(InputConstants.KEY_RETURN)
                || InputConstants.isKeyDown(InputConstants.KEY_NUMPADENTER)
                || InputConstants.isKeyDown(InputConstants.KEY_SPACE)));
        graphics.pose().pushMatrix();
        graphics.pose().translate(button.getX(), button.getY());
        graphics.pose().scale(.5f);
        int ink = theme.button(graphics, button.getWidth() * 2, button.getHeight() * 2,
            false, highlighted, pressed, button.isActive());
        if (button.isFocused() && controls.focusVisible()) {
            graphics.outline(-2, -2, button.getWidth() * 2 + 4, button.getHeight() * 2 + 4, theme.focus());
        }
        graphics.pose().popMatrix();
        if (!button.isActive()) ink = theme.muted();
        boolean icon = button == back || button == nativeUi().rodel$filters();
        if (icon) {
            (button == back ? MenuIcon.BACK : MenuIcon.SETTINGS).extract(graphics,
                button.getX() + (button.getWidth() - 11) / 2f, button.getY() + (button.getHeight() - 11) / 2f, 11, ink);
            if (button.isHovered() || button.isFocused() && controls.focusVisible()) {
                graphics.setTooltipForNextFrame(button.getMessage(), mouseX, mouseY);
            }
        } else {
            Component message = actionLabel(button.getMessage());
            int textHeight = controls.density().body();
            int textWidth = Math.min((int) Math.ceil(font.width(message) * textHeight / (float) font.lineHeight),
                button.getWidth() - 14);
            text(graphics, message, button.getX() + (button.getWidth() - textWidth) / 2,
                button.getY() + (button.getHeight() - textHeight) / 2, button.getWidth() - 14, ink, mouseX, mouseY);
        }
        return true;
    }

    private Component actionLabel(Component nativeLabel) {
        String original = nativeLabel.getString();
        String clean = original.replaceFirst("(?:\\.{3}|…)+\\s*$", "").stripTrailing();
        return clean.equals(original) ? nativeLabel : Component.literal(clean).setStyle(nativeLabel.getStyle());
    }

    @Override
    public boolean rodel$drawInput(AbstractWidget widget, GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        var search = nativeUi().rodel$search();
        if (!canvasActive || widget != search && (widget != null || x != search.getX() || y != search.getY()
            || width != search.getWidth() || height != search.getHeight())) return false;
        panel(graphics, x, y, width, height);
        if (search.isFocused() && controls.focusVisible()) graphics.outline(x, y, width, height, theme().focus());
        return true;
    }

    @Override public boolean rodel$canvasActive() { return canvasActive; }
    @Override public int rodel$canvasWidth() { return canvasWidth; }
    @Override public int rodel$canvasHeight() { return canvasHeight; }
    @Override public float rodel$canvasScale() { return canvasScale; }

    @Override
    public ScreenRectangle getRectangle() {
        return canvasWidth == 0 ? super.getRectangle() : new ScreenRectangle(0, 0, canvasWidth, canvasHeight);
    }

    private MouseButtonEvent local(MouseButtonEvent event) {
        return new MouseButtonEvent(event.x() / canvasScale, event.y() / canvasScale, event.buttonInfo());
    }

    @Override
    public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        return super.mouseClicked(local(event), doubleClick);
    }

    @Override
    public boolean mouseReleased(MouseButtonEvent event) { return super.mouseReleased(local(event)); }

    @Override
    public boolean mouseDragged(MouseButtonEvent event, double x, double y) {
        return super.mouseDragged(local(event), x / canvasScale, y / canvasScale);
    }

    @Override
    public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        return super.mouseScrolled(x / canvasScale, y / canvasScale, horizontal, vertical);
    }

    @Override
    public void mouseMoved(double x, double y) { super.mouseMoved(x / canvasScale, y / canvasScale); }

    @Override
    public boolean preeditUpdated(PreeditEvent event) { return nativeUi().rodel$search().preeditUpdated(event); }
}
