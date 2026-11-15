package io.github.richasy.rodel.ui.mixin;

import com.mojang.blaze3d.platform.TextInputManager;
import io.github.richasy.rodel.ui.UiInputGeometry;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(TextInputManager.class)
public abstract class TextInputGeometryMixin {
    @ModifyVariable(method = "setTextInputArea", at = @At("HEAD"), argsOnly = true, ordinal = 0)
    private int rodel$inputX(int value) { return UiInputGeometry.x(value); }
    @ModifyVariable(method = "setTextInputArea", at = @At("HEAD"), argsOnly = true, ordinal = 1)
    private int rodel$inputY(int value) { return UiInputGeometry.y(value); }
    @ModifyVariable(method = "setTextInputArea", at = @At("HEAD"), argsOnly = true, ordinal = 2)
    private int rodel$inputRight(int value) { return UiInputGeometry.x(value); }
    @ModifyVariable(method = "setTextInputArea", at = @At("HEAD"), argsOnly = true, ordinal = 3)
    private int rodel$inputBottom(int value) { return UiInputGeometry.y(value); }
}
