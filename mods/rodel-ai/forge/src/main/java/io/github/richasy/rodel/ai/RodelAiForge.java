package io.github.richasy.rodel.ai;

import net.minecraftforge.fml.common.Mod;

/** Discovers the client adapter without initializing Minecraft client classes on a dedicated server. */
@Mod("rodel_ai")
public final class RodelAiForge {
    /** Registers the client configuration without changing the shared model or session implementation. */
    public RodelAiForge() {
        net.minecraftforge.fml.DistExecutor.safeRunWhenOn(net.minecraftforge.api.distmarker.Dist.CLIENT, () -> RodelAiClient::registerConfig);
    }
}
