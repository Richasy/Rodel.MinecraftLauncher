package io.github.richasy.bettertools.mixin;

import net.minecraft.world.entity.LivingEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

/** Reuses vanilla's full equipment bookkeeping when a real tool changes before an attack. */
@Mixin(LivingEntity.class)
public interface EquipmentUpdateAccess {
    /** Applies vanilla equipment modifiers and synchronization without copying tool attributes. */
    @Invoker("detectEquipmentUpdates") void bettertools$updateEquipment();
}
