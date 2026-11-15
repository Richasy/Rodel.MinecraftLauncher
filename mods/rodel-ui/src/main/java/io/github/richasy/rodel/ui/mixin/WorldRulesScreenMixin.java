package io.github.richasy.rodel.ui.mixin;

import java.util.Set;
import java.util.Map;
import java.util.HashMap;
import io.github.richasy.rodel.ui.worlds.WorldRulesAccess;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.gamerules.GameRule;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArg;

@Mixin(AbstractGameRulesScreen.class)
public abstract class WorldRulesScreenMixin implements WorldRulesAccess {
    @ModifyArg(method = "setInitialFocus", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/worldselection/AbstractGameRulesScreen;setInitialFocus(Lnet/minecraft/client/gui/components/events/GuiEventListener;)V"), index = 0)
    private GuiEventListener rodel$searchFocus(GuiEventListener widget) {
        var page = WorldSubpage.get((Screen) (Object) this);
        return page == null ? widget : page.focusTarget(widget);
    }

    @Shadow protected AbstractGameRulesScreen.RuleList ruleList;
    @Shadow protected EditBox searchBox;
    @Shadow @Final private Set<AbstractGameRulesScreen.RuleEntry> invalidEntries;
    @Shadow @Final protected GameRules gameRules;
    @Override public AbstractSelectionList<?> rodel$rules() { return ruleList; }
    @Override public EditBox rodel$ruleSearch() { return searchBox; }
    @Override public boolean rodel$invalidRule(Object entry) { return invalidEntries.contains(entry); }
    @Override public boolean rodel$hasInvalidRules() { return !invalidEntries.isEmpty(); }
    @Override public Map<String, String> rodel$ruleValues() {
        Map<String, String> values = new HashMap<>();
        gameRules.availableRules().forEach(rule -> values.put(rule.id(), rodel$value(rule)));
        return Map.copyOf(values);
    }
    private <T> String rodel$value(GameRule<T> rule) { return gameRules.getAsString(rule); }
}
