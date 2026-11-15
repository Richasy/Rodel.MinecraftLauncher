package io.github.richasy.rodel.ui.mixin;

import com.mojang.realmsclient.dto.PlayerInfo;
import com.mojang.realmsclient.util.RealmsUtil;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "com.mojang.realmsclient.gui.screens.configuration.RealmsPlayersTab$PlayerEntry")
public abstract class RealmsPlayerTextMixin {
    @Shadow @Final private PlayerInfo playerInfo;
    @Shadow @Final private Button makeOpButton;
    @Shadow @Final private Button removeOpButton;
    @Shadow @Final private Button removeButton;

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$content(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered,
                               float tick, CallbackInfo callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) return;
        var density = page.density();
        var row = (AbstractSelectionList.Entry<?>) (Object) this;
        Button role = makeOpButton.visible ? makeOpButton : removeOpButton;
        int buttonY = row.getY() + (row.getHeight() - density.control()) / 2;
        int removeWidth = NetworkRowLayout.textWidth(removeButton.getMessage(), density.body()) + page.p(46);
        removeButton.setRectangle(removeWidth, density.control(), row.getContentRight() - removeWidth, buttonY);
        int roleX = removeButton.getX() - density.gap() - density.control();
        makeOpButton.setRectangle(density.control(), density.control(), roleX, buttonY);
        removeOpButton.setRectangle(density.control(), density.control(), roleX, buttonY);
        if (io.github.richasy.rodel.ui.settings.SettingsText.description(makeOpButton).getString().isBlank())
            makeOpButton.setTooltip(Tooltip.create(Component.translatable("rodel.realms.player_role.change", removeOpButton.getMessage())));
        if (io.github.richasy.rodel.ui.settings.SettingsText.description(removeOpButton).getString().isBlank())
            removeOpButton.setTooltip(Tooltip.create(Component.translatable("rodel.realms.player_role.change", makeOpButton.getMessage())));
        int badgeWidth = NetworkRowLayout.textWidth(role.getMessage(), density.detail()) + density.inset() * 2;
        int badgeHeight = density.detail() + density.gap();
        int badgeX = roleX - density.gap() - badgeWidth;
        int badgeY = row.getY() + (row.getHeight() - badgeHeight) / 2;
        graphics.fill(badgeX, badgeY, badgeX + badgeWidth, badgeY + badgeHeight, page.theme().hover());
        NetworkRowLayout.text(page, graphics, role.getMessage(), badgeX + density.inset(), badgeY + density.gap() / 2,
            badgeWidth - density.inset() * 2, density.detail(), page.theme().muted(), mouseX, mouseY);
        int faceY = row.getY() + (row.getHeight() - density.icon()) / 2;
        RealmsUtil.extractPlayerFace(graphics, row.getContentX(), faceY, density.icon(), playerInfo.uuid);
        int nameX = row.getContentX() + density.icon() + density.gap();
        int nameY = row.getY() + (row.getHeight() - density.body()) / 2;
        int color = !playerInfo.accepted ? page.theme().muted() : playerInfo.online ? page.theme().focus() : page.theme().ink();
        NetworkRowLayout.text(page, graphics, Component.literal(playerInfo.name), nameX, nameY,
            Math.max(0, badgeX - density.gap() - nameX), density.body(), color, mouseX, mouseY);
        makeOpButton.extractRenderState(graphics, mouseX, mouseY, tick);
        removeOpButton.extractRenderState(graphics, mouseX, mouseY, tick);
        removeButton.extractRenderState(graphics, mouseX, mouseY, tick);
        callback.cancel();
    }
}
