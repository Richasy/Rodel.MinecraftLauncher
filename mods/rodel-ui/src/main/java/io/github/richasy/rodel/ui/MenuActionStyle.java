package io.github.richasy.rodel.ui;

import java.util.List;
import io.github.richasy.rodel.ui.settings.SettingsText;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.BackupConfirmScreen;
import net.minecraft.client.gui.screens.Screen;

public final class MenuActionStyle {
    private MenuActionStyle() {}

    public static AbstractWidget primary(Screen screen, List<? extends AbstractWidget> actions) {
        var visible = actions.stream().<AbstractWidget>map(widget -> widget).filter(widget -> widget.visible).toList();
        if (visible.isEmpty()) return null;
        if (screen instanceof BackupConfirmScreen) return visible.getFirst();
        String type = screen.getClass().getName();
        if (type.equals("net.minecraft.client.gui.screens.RecoverWorldDataScreen"))
            return visible.size() > 1 ? visible.get(1) : visible.getFirst();
        if (type.equals("net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen"))
            return visible.size() > 1 ? visible.get(1) : visible.getFirst();
        if (SettingsText.hasKey(screen.getTitle(), "selectWorld.deleteQuestion"))
            return visible.stream().filter(widget -> cancel(widget)).findFirst().orElse(null);
        var commit = visible.stream().filter(widget -> SettingsText.hasKey(widget.getMessage(), "gui.done")
            || SettingsText.hasKey(widget.getMessage(), "gui.proceed")
            || SettingsText.hasKey(widget.getMessage(), "rodel.worlds.navigation.apply")).findFirst();
        if (commit.isPresent()) return commit.get();
        if (type.equals("net.minecraft.client.gui.screens.DatapackLoadFailureScreen"))
            return visible.stream().filter(MenuActionStyle::cancel).findFirst().orElse(visible.getLast());
        return visible.stream().filter(widget -> !cancel(widget)).findFirst()
            .orElse(visible.size() == 1 && !SettingsText.hasKey(visible.getFirst().getMessage(), "gui.cancel")
                ? visible.getFirst() : null);
    }

    private static boolean cancel(AbstractWidget widget) {
        return SettingsText.hasKey(widget.getMessage(), "gui.cancel")
            || SettingsText.hasKey(widget.getMessage(), "gui.no")
            || SettingsText.hasKey(widget.getMessage(), "gui.back");
    }

    public static boolean requiresPrimaryAction(Screen screen) {
        String type = screen.getClass().getName();
        return type.equals("net.minecraft.client.gui.screens.friends.FriendsListConfirmScreen")
            || type.equals("net.minecraft.client.gui.screens.reporting.AbstractReportScreen$DiscardReportWarningScreen")
            || type.equals("net.minecraft.client.gui.components.PopupScreen")
            || type.equals("net.minecraft.client.gui.screens.reporting.NameReportScreen")
            || type.equals("net.minecraft.client.gui.screens.reporting.ChatReportScreen")
            || type.equals("net.minecraft.client.gui.screens.reporting.SkinReportScreen");
    }

    public static void apply(MenuContext context, Screen screen, List<Button> actions) {
        var primary = primary(screen, actions);
        for (var button : actions) ((ThemedButton) button).rodel$setStyle(context, null, button == primary, false, false);
    }
}
