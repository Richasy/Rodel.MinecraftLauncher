package io.github.richasy.bettertools.checks;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;

import net.minecraftforge.event.TickEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.event.lifecycle.FMLClientSetupEvent;
import net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext;

/** Test-only Forge binding, packaged separately and never included in the product JAR. */
@Mod("bettertools_test")
public final class BetterToolsNativeTest {
    public BetterToolsNativeTest(FMLJavaModLoadingContext context) {
        FMLClientSetupEvent.getBus(context.getModBusGroup()).addListener(event ->
                event.enqueueWork(() -> TickEvent.ClientTickEvent.Post.BUS.addListener(tick -> NativeClientCheck.tick())));
    }
}
