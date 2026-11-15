package dev.ftb.mods.ftbteams.api;

import java.util.*;

/** Synthetic optional API: the player must be resolved to a different team UUID before reading its progress. */
public final class FTBTeamsAPI {
    public static UUID player;
    public static final UUID TEAM = UUID.fromString("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static FTBTeamsAPI api() { return new FTBTeamsAPI(); }
    public Manager getManager() { return new Manager(); }
    public static final class Manager {
        public Optional<Team> getTeamForPlayerID(UUID id) {
            if (!id.equals(player)) throw new AssertionError("Wrong player progress scope");
            return Optional.of(new Team());
        }
    }
    public ClientManager getClientManager() { return new ClientManager(); }
    public static final class ClientManager {
        public Optional<KnownPlayer> getKnownPlayer(UUID id) {
            if (!id.equals(player)) throw new AssertionError("Wrong synchronized player scope");
            return Optional.of(new KnownPlayer());
        }
    }
    public static final class KnownPlayer { public UUID teamId() { return TEAM; } }
    public static final class Team { public UUID getId() { return TEAM; } }
}
