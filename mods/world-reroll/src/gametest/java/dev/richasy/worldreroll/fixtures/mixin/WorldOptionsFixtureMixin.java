package dev.richasy.worldreroll.fixtures.mixin;

import dev.richasy.worldreroll.RerollSeedFixture;
import net.minecraft.world.level.levelgen.WorldOptions;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Controls one random seed in the positive village fixture; never packaged in the product mod. */
@Mixin(WorldOptions.class)
public abstract class WorldOptionsFixtureMixin {
    @Inject(method = "randomSeed", at = @At("HEAD"), cancellable = true)
    private static void worldreroll$fixtureSeed(CallbackInfoReturnable<Long> result) {
        Long seed = RerollSeedFixture.take();
        if (seed != null) result.setReturnValue(seed);
    }
}
