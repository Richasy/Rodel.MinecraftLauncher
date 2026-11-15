package io.github.richasy.rodel.ui.mixin;

import java.util.function.Function;
import java.util.List;
import java.util.Objects;
import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import io.github.richasy.rodel.ui.settings.NativeCycleChoices;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.worlds.WorldSurface;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.input.InputWithModifiers;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.gen.Invoker;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(CycleButton.class)
public abstract class CyclePresentationMixin<T> implements NativeOptionPresentation, NativeCycleChoices {
    @Shadow @Final private Component name;
    @Shadow @Final @Mutable private Function<T, Component> valueStringifier;
    @Shadow private T value;
    @Shadow private int index;
    @Shadow @Final private CycleButton.ValueListSupplier<T> values;

    @Invoker("cycleValue")
    public abstract void rodel$cycleBy(int delta);

    @Shadow public abstract void setValue(T value);

    @Override
    public void rodel$valueOnlyFormatter() {
        Function<T, Component> original = valueStringifier;
        valueStringifier = candidate -> SettingsText.formattedValue(original.apply(candidate));
        setValue(value);
    }

    @Override
    public List<Choice> rodel$choices() {
        return values.getSelectedList().stream().map(candidate -> new Choice(candidate,
            SettingsText.choiceLabel(name, candidate, valueStringifier.apply(candidate)), Objects.equals(value, candidate), () -> {
                int target = values.getSelectedList().indexOf(candidate);
                if (target < 0) {
                    org.slf4j.LoggerFactory.getLogger("Rodel UI").warn("Native choices changed for {}; reopen the selector", name.getString());
                } else if (!Objects.equals(value, candidate)) {
                    rodel$cycleBy(target - index);
                }
            })).toList();
    }

    @Inject(method = "onPress", at = @At("HEAD"), cancellable = true)
    private void rodel$openChoices(InputWithModifiers input, CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && !(value instanceof Boolean) && !page.isMainHand(this)) {
            page.openChoices((CycleButton<?>) (Object) this);
            callback.cancel();
        } else if (WorldSurface.get(Minecraft.getInstance().gui.screen()) instanceof WorldSurface world && !(value instanceof Boolean)) {
            world.openChoices((CycleButton<?>) (Object) this);
            callback.cancel();
        } else if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) instanceof NativeMenuPage menu && !(value instanceof Boolean)) {
            menu.openChoices((CycleButton<?>) (Object) this);
            callback.cancel();
        }
    }

    @Inject(method = "mouseScrolled", at = @At("HEAD"), cancellable = true)
    private void rodel$scrollPage(double x, double y, double horizontal, double vertical,
                                 CallbackInfoReturnable<Boolean> callback) {
        if (NativeSettingsPage.get(Minecraft.getInstance().gui.screen()) != null
            || WorldSurface.get(Minecraft.getInstance().gui.screen()) != null
            || NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null) callback.setReturnValue(false);
    }

    @Override
    public Component rodel$caption() {
        return name;
    }

    @Override
    public Component rodel$valueText() {
        return SettingsText.choiceLabel(name, value, valueStringifier.apply(value));
    }
}
