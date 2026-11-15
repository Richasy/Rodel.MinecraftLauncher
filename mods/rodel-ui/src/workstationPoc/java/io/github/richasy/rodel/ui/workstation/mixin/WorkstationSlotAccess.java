package io.github.richasy.rodel.ui.workstation.mixin;

import net.minecraft.world.inventory.Slot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Provides layout writes for the existing slots of the two client screens. */
@Mixin(Slot.class)
public interface WorkstationSlotAccess {
    /** Sets the slot's horizontal GUI position for both item rendering and hit testing. */
    @Mutable @Accessor("x") void rodel$setX(int x);

    /** Sets the slot's vertical GUI position for both item rendering and hit testing. */
    @Mutable @Accessor("y") void rodel$setY(int y);
}
