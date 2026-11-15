package dev.richasy.worldreroll;

import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.TicketType;

/** Owns the transient preparation ticket without depending on a particular loader's lifecycle API. */
public final class VillageTickets {
    static TicketType PREPARATION;

    private VillageTickets() {}

    /** Registers during native ticket bootstrap, before the registry is frozen on any supported loader. */
    public static void register() {
        PREPARATION = Registry.register(BuiltInRegistries.TICKET_TYPE,
                Identifier.fromNamespaceAndPath("worldreroll", "village_preparation"),
                new TicketType(TicketType.NO_TIMEOUT, TicketType.FLAG_LOADING | TicketType.FLAG_KEEP_DIMENSION_ACTIVE));
    }
}
