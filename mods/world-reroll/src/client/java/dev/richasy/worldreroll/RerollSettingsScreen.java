package dev.richasy.worldreroll;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import com.mojang.blaze3d.platform.InputConstants;

/** Only the next world's spawn preference is edited here; cancel never changes the active world. */
public final class RerollSettingsScreen extends Screen {
    private static boolean lastVillagePreference;
    private final Screen parent;
    private final boolean structures;
    private boolean village;
    private RerollLook look;
    private int left, top, panelWidth, panelHeight, content, contentWidth, bodyTop, bodyBottom, randomY, villageY, warningY;
    private int warningHeight, totalHeight, scroll;
    private Button randomButton, villageButton;

    public RerollSettingsScreen(Screen parent) {
        super(Component.translatable("worldreroll.settings.title"));
        this.parent = parent;
        var server = net.minecraft.client.Minecraft.getInstance().getSingleplayerServer();
        structures = server != null && server.getWorldGenSettings().options().generateStructures();
        village = structures && lastVillagePreference;
    }

    @Override protected void init() {
        look = new RerollLook();
        panelWidth = Math.min(400, width - 24); contentWidth = panelWidth - look.inset * 2;
        int descriptionHeight = look.height(font, tr("description"), contentWidth, look.body);
        int randomHeight = look.choiceHeight(font, tr("random"), tr("random_hint"), contentWidth);
        int villageHeight = look.choiceHeight(font, tr("village"), villageHint(), contentWidth);
        warningHeight = look.height(font, tr("warning"), contentWidth, look.detail);
        randomY = descriptionHeight + look.gap * 2;
        villageY = randomY + randomHeight + look.gap;
        warningY = villageY + villageHeight + look.gap * 2;
        totalHeight = warningY + warningHeight;
        panelHeight = Math.min(height - 24, look.inset * 2 + look.heading + look.control + totalHeight + look.gap * 3);
        left = (width - panelWidth) / 2; top = Math.max(4, (height - panelHeight) / 2); content = left + look.inset;
        addRenderableWidget(Button.builder(Component.literal("…"), button -> minecraft.gui.setScreen(new RerollClientConfig(this)))
            .bounds(left + panelWidth - look.inset - look.control, top + look.inset - 3, look.control, look.control).build())
            .setTooltip(net.minecraft.client.gui.components.Tooltip.create(Component.translatable("worldreroll.config.title")));
        bodyTop = top + look.inset + Math.max(look.heading, look.control - 3) + look.gap;
        int footerY = top + panelHeight - look.inset - look.control;
        bodyBottom = footerY - look.gap;
        randomButton = addRenderableWidget(look.choice(content, bodyTop + randomY, contentWidth, randomHeight,
            "worldreroll.settings.random", tr("random_hint"), () -> !village, () -> select(false)));
        villageButton = addRenderableWidget(look.choice(content, bodyTop + villageY, contentWidth, villageHeight,
            "worldreroll.settings.village", villageHint(), () -> village, () -> select(true)));
        villageButton.active = structures;
        int actionWidth = (contentWidth - look.inset) / 2;
        addRenderableWidget(look.button(content, footerY, actionWidth, "worldreroll.settings.continue", true, this::confirm));
        addRenderableWidget(look.button(content + actionWidth + look.inset, footerY, contentWidth - actionWidth - look.inset,
            "gui.cancel", false, this::onClose));
        select(village); updateScroll(scroll);
    }

    private Component tr(String key) { return Component.translatable("worldreroll.settings." + key); }
    private Component villageHint() { return tr(structures ? "village_hint" : "structures_off"); }
    private void select(boolean nearVillage) {
        village = nearVillage; lastVillagePreference = village;
    }
    private void confirm() {
        if (!RerollController.available(minecraft)) return;
        String name = minecraft.getSingleplayerServer().getWorldData().getLevelName();
        minecraft.gui.setScreen(new RerollConfirmScreen(this, look, Component.translatable("worldreroll.confirm.title"),
            Component.translatable(village ? "worldreroll.confirm.village_body" : "worldreroll.confirm.body", name),
            () -> RerollController.start(minecraft, village)));
    }
    private void updateScroll(int value) {
        scroll = Math.clamp(value, 0, Math.max(0, totalHeight - (bodyBottom - bodyTop)));
        randomButton.setY(bodyTop + randomY - scroll); villageButton.setY(bodyTop + villageY - scroll);
        for (Button button : new Button[]{randomButton, villageButton})
            button.visible = button.getY() >= bodyTop && button.getBottom() <= bodyBottom;
    }
    @Override public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        if (y >= bodyTop && y <= bodyBottom) { updateScroll(scroll - (int) (vertical * 20)); return true; }
        return super.mouseScrolled(x, y, horizontal, vertical);
    }
    @Override public boolean keyPressed(KeyEvent event) {
        if (event.key() == InputConstants.KEY_PAGEDOWN) { updateScroll(scroll + bodyBottom - bodyTop); return true; }
        if (event.key() == InputConstants.KEY_PAGEUP) { updateScroll(scroll - (bodyBottom - bodyTop)); return true; }
        return super.keyPressed(event);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
        look.refresh(); look.panel(graphics, left, top, panelWidth, panelHeight);
        look.emphasizedText(graphics, font, title, content, top + look.inset, contentWidth - look.control - look.gap, look.heading, look.ink);
        graphics.enableScissor(content, bodyTop, content + contentWidth, Math.max(bodyTop, bodyBottom));
        int origin = bodyTop - scroll;
        look.text(graphics, font, tr("description"), content, origin, contentWidth, look.body, look.muted);
        look.text(graphics, font, tr("warning"), content, origin + warningY, contentWidth, look.detail, look.muted);
        graphics.disableScissor();
        if (totalHeight > bodyBottom - bodyTop) look.scrollbar(graphics, left + panelWidth - 6, bodyTop, bodyBottom - bodyTop, totalHeight, scroll);
        super.extractRenderState(graphics, mouseX, mouseY, delta);
    }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }
    @Override public boolean isPauseScreen() { return true; }
}
