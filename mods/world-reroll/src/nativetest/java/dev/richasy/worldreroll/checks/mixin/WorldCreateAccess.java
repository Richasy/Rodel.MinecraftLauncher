package dev.richasy.worldreroll.checks.mixin;

import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

/** Invokes the real native creation callback, with Loom remapping its private method for a normal client. */
@Mixin(CreateWorldScreen.class)
public interface WorldCreateAccess {
    /** Starts only the isolated world configured through the native creation state. */
    @Invoker("onCreate") void check$create();
}
