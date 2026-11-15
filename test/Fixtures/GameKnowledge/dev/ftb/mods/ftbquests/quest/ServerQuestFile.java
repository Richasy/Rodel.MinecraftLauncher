package dev.ftb.mods.ftbquests.quest;

import java.util.*;
import java.util.stream.Stream;
import dev.ftb.mods.ftbteams.api.FTBTeamsAPI;
import io.github.richasy.rodel.bridge.GameBridgeChecks.Text;

/** Independent quest model with mutable progress and visibility; no FTB implementation is copied. */
public final class ServerQuestFile {
    public static ServerQuestFile INSTANCE = new ServerQuestFile();
    public static boolean completed, hidden, missing;
    public List<Chapter> getAllChapters() { return List.of(new Chapter()); }
    public TeamData getNullableTeamData(UUID id) {
        if (!id.equals(FTBTeamsAPI.TEAM)) throw new AssertionError("Player UUID is not the team UUID");
        return missing ? null : new TeamData();
    }
    public TeamData getOrCreateTeamData(UUID id) { throw new AssertionError("Read query must not create progression state"); }
    public static final class Chapter {
        public String getCodeString() { return "0000000000000001"; }
        public Text getTitle() { return new Text("牧场"); }
        public List<Quest> getQuests() { return List.of(new Quest()); }
    }
    public static final class Quest {
        public String getCodeString() { return "000000000000002A"; }
        public Text getTitle() { return new Text("开始养殖"); }
        public List<Text> getDescription() { return List.of(new Text("使用混合饲料饲养动物")); }
        public boolean isVisible(TeamData data) { return true; }
        public boolean hideDetailsUntilStartable() { return hidden; }
        public Flag getHideTextUntilComplete() { return new Flag(); }
        public Stream<Chapter> streamDependencies() { return Stream.of(new Chapter()); }
        public List<Task> getTasks() { return List.of(new Task()); }
    }
    public static final class Flag { public boolean get(boolean fallback) { return false; } }
    public static final class Task {
        public String getCodeString() { return "000000000000002B"; }
        public Text getTitle() { return new Text("收集饲料"); }
        public long getMaxProgress() { return 5; }
        public TaskType getType() { return new TaskType(); }
        public void writeData(net.minecraft.nbt.CompoundTag tag) { tag.value = "{item:\"farmersdelight:animal_feed\",custom_field:\"retained\"}"; }
    }
    public static final class TaskType { public String getTypeId() { return "ftbquests:item"; } }
    public static class TeamData {
        public boolean isCompleted(Quest quest) { return completed; }
        public boolean canStartTasks(Quest quest) { return !hidden; }
        public long getProgress(Task task) { return completed ? 5 : 2; }
    }
}
