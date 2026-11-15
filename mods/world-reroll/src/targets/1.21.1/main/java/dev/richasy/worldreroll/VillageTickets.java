package dev.richasy.worldreroll;

import java.util.Comparator;
import net.minecraft.server.level.TicketType;
import net.minecraft.world.level.ChunkPos;

/** The older distance manager owns exact keyed, non-expiring tickets without a ticket-type registry. */
public final class VillageTickets {
    static TicketType<ChunkPos> PREPARATION;
    private VillageTickets() { }
    /** Creates the key type before the first search; each search releases only its own positions and levels. */
    public static void register() {
        PREPARATION = TicketType.create("worldreroll:village_preparation", Comparator.comparingLong(ChunkPos::toLong));
    }
}
