package io.github.richasy.rodel.ui.mixin;

import java.util.HashMap;
import java.util.Map;
import it.unimi.dsi.fastutil.objects.Object2BooleanMap;
import io.github.richasy.rodel.ui.worlds.WorldExperimentsAccess;
import net.minecraft.client.gui.screens.worldselection.ExperimentsScreen;
import net.minecraft.server.packs.repository.Pack;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(ExperimentsScreen.class)
public abstract class WorldExperimentsMixin implements WorldExperimentsAccess {
    @Shadow @Final private Object2BooleanMap<Pack> packs;
    @Override public Map<String, Boolean> rodel$experimentValues() {
        Map<String, Boolean> values = new HashMap<>();
        packs.forEach((pack, selected) -> values.put(pack.getId(), selected));
        return Map.copyOf(values);
    }
}
