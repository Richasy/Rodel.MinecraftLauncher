package io.github.richasy.bettertools.checks;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;

import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.event.lifecycle.FMLClientSetupEvent;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.client.event.ClientTickEvent;

/** Test-only NeoForge lifecycle binding for the actual packaged-JAR acceptance instance. */
@Mod("bettertools_test")
public final class BetterToolsNativeTest {
    public BetterToolsNativeTest(IEventBus bus) {
        bus.addListener((FMLClientSetupEvent event) -> event.enqueueWork(() ->
                NeoForge.EVENT_BUS.addListener((ClientTickEvent.Post tick) -> NativeClientCheck.tick())));
    }
}
