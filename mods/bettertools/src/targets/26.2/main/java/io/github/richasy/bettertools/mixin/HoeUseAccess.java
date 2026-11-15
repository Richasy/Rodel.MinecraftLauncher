package io.github.richasy.bettertools.mixin;

import com.mojang.datafixers.util.Pair;
import java.util.Map;
import java.util.function.Consumer;
import java.util.function.Predicate;
import net.minecraft.world.item.HoeItem;
import net.minecraft.world.item.context.UseOnContext;
import net.minecraft.world.level.block.Block;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Accesses pre-26.3 native tilling predicates without duplicating Minecraft's soil catalogue. */
@Mixin(HoeItem.class)
public interface HoeUseAccess {
    /** Returns the native map, including additions made by other mods; callers only read it. */
    @Accessor("TILLABLES") static Map<Block, Pair<Predicate<UseOnContext>, Consumer<UseOnContext>>> bettertools$tillables() {
        throw new AssertionError("Mixin accessor not applied");
    }
}
