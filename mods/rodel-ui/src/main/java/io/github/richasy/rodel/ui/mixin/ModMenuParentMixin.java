package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ModMenuParent;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.gen.Accessor;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.ModsScreen", remap = false)
public interface ModMenuParentMixin extends ModMenuParent {
    @Override
    @Accessor("previousScreen")
    Screen rodel$previousScreen();
}
