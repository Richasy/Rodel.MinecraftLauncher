package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.worlds.NativeRuleRow;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.events.ContainerEventHandler;
import net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.ComponentUtils;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.world.level.gamerules.GameRule;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen$BooleanRuleEntry",
    "net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen$IntegerRuleEntry"
})
public abstract class WorldRuleEntryMixin implements NativeRuleRow {
    @Unique private Component rodel$title;
    @Unique private Component rodel$description;
    @Unique private Component rodel$default;

    @Inject(method = "<init>", at = @At("RETURN"))
    private void rodel$metadata(AbstractGameRulesScreen owner, Component title, List<FormattedCharSequence> tooltip,
                                String narration, GameRule<?> rule, CallbackInfo callback) {
        rodel$title = title;
        Component description = Component.translatable(rule.getDescriptionId() + ".description");
        rodel$description = ComponentUtils.isTranslationResolvable(description) ? description : Component.empty();
        rodel$default = rodel$defaultValue(rule);
    }
    @Unique private static <T> Component rodel$defaultValue(GameRule<T> rule) {
        return Component.translatable("editGamerule.default", Component.literal(rule.serialize(rule.defaultValue())));
    }
    @Override public Component rodel$ruleTitle() { return rodel$title; }
    @Override public Component rodel$ruleDescription() { return rodel$description; }
    @Override public Component rodel$ruleDefault() { return rodel$default; }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$row(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float partialTick, CallbackInfo callback) {
        var page = WorldSubpage.get(Minecraft.getInstance().gui.screen());
        if (page == null) return;
        var children = ((ContainerEventHandler) this).children();
        page.ruleRow(graphics, (AbstractSelectionList.Entry<?>) (Object) this, (AbstractWidget) children.getFirst(),
            this, mouseX, mouseY, partialTick);
        callback.cancel();
    }
}
