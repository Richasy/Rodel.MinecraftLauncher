package dev.richasy.worldreroll.mixin;

import dev.richasy.worldreroll.VillageSpawn;
import java.util.function.BooleanSupplier;
import net.minecraft.server.MinecraftServer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Keeps preparation and shutdown cleanup on the owning server across all loader adapters. */
@Mixin(MinecraftServer.class)
public abstract class VillageServerMixin {
    @Inject(method = "tickServer", at = @At("TAIL"))
    private void worldreroll$advance(BooleanSupplier hasTimeLeft, CallbackInfo callback) {
        VillageSpawn.tick((MinecraftServer) (Object) this);
    }

    @Inject(method = "stopServer", at = @At("HEAD"))
    private void worldreroll$release(CallbackInfo callback) {
        VillageSpawn.stop((MinecraftServer) (Object) this);
    }
}
