package dev.ftb.mods.ftbquests.client;

import java.util.*;
import dev.ftb.mods.ftbquests.quest.ServerQuestFile;
import dev.ftb.mods.ftbteams.api.FTBTeamsAPI;

/** Synchronized fixture has different completion state, so a remote query cannot read the local server file. */
public final class ClientQuestFile {
    public static final ClientQuestFile INSTANCE = new ClientQuestFile();
    public List<ServerQuestFile.Chapter> getAllChapters() { return List.of(new ServerQuestFile.Chapter()); }
    public Progress getNullableTeamData(UUID id) {
        if (!id.equals(FTBTeamsAPI.TEAM)) throw new AssertionError("Remote team scope mismatch");
        return new Progress();
    }
    public static final class Progress extends ServerQuestFile.TeamData {
        @Override
        public boolean isCompleted(ServerQuestFile.Quest quest) { return false; }
        @Override
        public boolean canStartTasks(ServerQuestFile.Quest quest) { return true; }
        @Override
        public long getProgress(ServerQuestFile.Task task) { return 3; }
    }
}
