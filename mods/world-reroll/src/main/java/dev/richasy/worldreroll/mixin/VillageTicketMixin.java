package dev.richasy.worldreroll.mixin;

import dev.richasy.worldreroll.VillageTickets;
import net.minecraft.server.level.TicketType;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Uses the same registry-bootstrap boundary for Fabric, Quilt, Forge and NeoForge. */
@Mixin(TicketType.class)
public abstract class VillageTicketMixin {
    @Inject(method = "<clinit>", at = @At("TAIL"))
    private static void worldreroll$registerTicket(CallbackInfo callback) {
        VillageTickets.register();
    }
}
