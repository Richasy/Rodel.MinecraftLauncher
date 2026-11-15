package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** GUI-only presentation: native tool model and bar, with a small recognizable cube in the corner. */
@Mixin(GuiGraphicsExtractor.class)
public abstract class CubeGuiItemMixin {
    @ModifyVariable(method = "item(Lnet/minecraft/world/entity/LivingEntity;Lnet/minecraft/world/level/Level;Lnet/minecraft/world/item/ItemStack;III)V",
            at = @At("HEAD"), argsOnly = true)
    private ItemStack bettertools$toolIcon(ItemStack stack) {
        if (!stack.has(BetterTools.STATE)) return stack;
        var tool = CubeInventory.displayedTool(stack);
        return tool.isEmpty() ? stack : tool;
    }

    @Inject(method = "itemDecorations(Lnet/minecraft/client/gui/Font;Lnet/minecraft/world/item/ItemStack;IILjava/lang/String;)V", at = @At("RETURN"))
    private void bettertools$cubeBadge(Font font, ItemStack stack, int x, int y, String count, CallbackInfo ci) {
        if (!stack.has(BetterTools.STATE) || CubeInventory.displayedTool(stack).isEmpty()) return;
        var graphics = (GuiGraphicsExtractor)(Object)this;
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(x - 1, y - 1);
            graphics.pose().scale(.5f);
            graphics.item(new ItemStack(BetterTools.CUBE), 0, 0);
        } finally { graphics.pose().popMatrix(); }
    }
}
