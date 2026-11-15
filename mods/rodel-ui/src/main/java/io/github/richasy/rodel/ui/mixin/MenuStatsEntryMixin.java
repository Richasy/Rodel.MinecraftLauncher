package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.StatsMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.gen.Invoker;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.achievement.StatsScreen$GeneralStatisticsList$Entry")
public abstract class MenuStatsEntryMixin {
    @Shadow @Final private Component statDisplay;
    @Invoker("getValueText") public abstract String rodel$valueText();

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$statRow(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float tick, CallbackInfo callback) {
        if (!(NativeMenuPage.get(Minecraft.getInstance().gui.screen()) instanceof StatsMenuPage page)) return;
        var row = (AbstractSelectionList.Entry<?>) (Object) this;
        var font = Minecraft.getInstance().font;
        int x = row.getContentX() + 2;
        int right = row.getContentRight() - 4;
        float size = page.density().body();
        var value = MenuText.styled(Component.literal(rodel$valueText()));
        var label = MenuText.secondary(statDisplay, page.theme().ink());
        float valueWidth = font.width(value) * size / font.lineHeight;
        int available = Math.max(1, Math.round((right - x - valueWidth - 12) * font.lineHeight / size));
        boolean trimmed = font.width(label) > available;
        var shown = trimmed ? MenuText.secondary(Component.literal(
            font.substrByWidth(label, Math.max(1, available - font.width("…"))).getString() + "…"), page.theme().ink()) : label;
        float y = row.getY() + (row.getHeight() - size) / 2f;
        MenuText.extract(graphics, font, shown, x, y, size, page.theme().ink());
        MenuText.extract(graphics, font, value, right - valueWidth, y, size, page.theme().ink());
        if (trimmed && hovered) graphics.setTooltipForNextFrame(statDisplay, mouseX, mouseY);
        graphics.fill(x, row.getY() + row.getHeight() - 1, right, row.getY() + row.getHeight(),
            page.theme().edge() & 0xFFFFFF | 0x30000000);
        callback.cancel();
    }
}
