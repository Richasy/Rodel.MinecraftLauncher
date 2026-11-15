package io.github.richasy.rodel.ui.menus;

import java.util.Set;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import net.minecraft.client.gui.screens.Screen;

/** Exact native menu ownership; a mod's subclasses and configuration screens are not included. */
public final class NativeMenus {
    private static final String SCREENS = "net.minecraft.client.gui.screens.";
    private static final Set<String> GAME = Set.of(
        "PauseScreen", "WorldOptionsScreen", "DirectJoinServerScreen", "ManageServerScreen",
        "achievement.StatsScreen", "advancements.AdvancementsScreen",
        "ConnectScreen", "DisconnectedScreen",
        "multiplayer.JoinMultiplayerScreen", "multiplayer.CodeOfConductScreen",
        "multiplayer.ServerReconfigScreen", "multiplayer.RestrictionsScreen",
        "friends.FriendsOverlayScreen", "friends.FriendsListConfirmScreen",
        "social.SocialInteractionsScreen",
        "reporting.ReportPlayerScreen", "reporting.ChatReportScreen",
        "reporting.ChatSelectionScreen", "reporting.NameReportScreen",
        "reporting.SkinReportScreen", "reporting.ReportReasonSelectionScreen",
        "reporting.AbstractReportScreen$DiscardReportWarningScreen",
        "options.InWorldGameRulesScreen", "dialog.ServerLinksDialogScreen"
    );
    private static final Set<String> REALMS = Set.of(
        "com.mojang.realmsclient.RealmsMainScreen",
        "com.mojang.realmsclient.gui.screens.RealmsBrokenWorldScreen",
        "com.mojang.realmsclient.gui.screens.RealmsCreateRealmScreen",
        "com.mojang.realmsclient.gui.screens.RealmsDownloadLatestWorldScreen",
        "com.mojang.realmsclient.gui.screens.RealmsJoinRealmWithCodeScreen",
        "com.mojang.realmsclient.gui.screens.RealmsLongRunningMcoConnectTaskScreen",
        "com.mojang.realmsclient.gui.screens.RealmsLongRunningMcoTaskScreen",
        "com.mojang.realmsclient.gui.screens.RealmsPdpScreen",
        "com.mojang.realmsclient.gui.screens.RealmsPendingInvitesScreen",
        "com.mojang.realmsclient.gui.screens.RealmsResetWorldScreen",
        "com.mojang.realmsclient.gui.screens.RealmsSelectFileToUploadScreen",
        "com.mojang.realmsclient.gui.screens.RealmsSelectWorldTemplateScreen",
        "com.mojang.realmsclient.gui.screens.RealmsTermsScreen",
        "com.mojang.realmsclient.gui.screens.RealmsUploadScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsBackupInfoScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsBackupScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsConfigureWorldScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsEditInviteCodeScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsInviteCodesScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsInviteScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsPreferredRegionSelectionScreen",
        "com.mojang.realmsclient.gui.screens.configuration.RealmsSlotOptionsScreen"
    );
    private static final Set<String> IN_GAME_ROOTS = Set.of(
        "PauseScreen", "WorldOptionsScreen", "DisconnectedScreen",
        "friends.FriendsOverlayScreen", "social.SocialInteractionsScreen",
        "achievement.StatsScreen", "advancements.AdvancementsScreen",
        "multiplayer.ServerReconfigScreen", "dialog.ServerLinksDialogScreen"
    );

    private NativeMenus() {}

    public static boolean supported(Screen screen) {
        String name = screen.getClass().getName();
        return REALMS.contains(name) || name.startsWith(SCREENS) && GAME.contains(name.substring(SCREENS.length()));
    }

    public static boolean inGameRoot(Screen screen) {
        String name = screen.getClass().getName();
        return name.startsWith(SCREENS) && IN_GAME_ROOTS.contains(name.substring(SCREENS.length()));
    }

    public static boolean attach(Screen screen, MenuContext appearance) {
        if (!UiConfig.enabled(Effect.MENUS)) return false;
        boolean menuPopup = screen.getClass() == net.minecraft.client.gui.components.PopupScreen.class
            && appearance instanceof NativeMenuPage;
        if (!supported(screen) && !menuPopup) return false;
        if (screen instanceof net.minecraft.client.gui.screens.PauseScreen pause && !pause.showsPauseMenu()) return false;
        MenuPageHost host = (MenuPageHost) screen;
        if (host.rodel$menuPage() == null) host.rodel$menuPage(
            screen.getClass() == net.minecraft.client.gui.screens.PauseScreen.class
                ? new PauseMenuPage(screen, appearance)
                : screen.getClass() == net.minecraft.client.gui.screens.achievement.StatsScreen.class
                    ? new StatsMenuPage(screen, appearance)
                    : screen.getClass() == net.minecraft.client.gui.screens.advancements.AdvancementsScreen.class
                        ? new AdvancementsMenuPage(screen, appearance)
                        : screen.getClass() == net.minecraft.client.gui.screens.reporting.NameReportScreen.class
                            ? new NameReportMenuPage(screen, appearance)
                            : screen.getClass() == net.minecraft.client.gui.screens.social.SocialInteractionsScreen.class
                                ? new SocialMenuPage(screen, appearance) : new NativeMenuPage(screen, appearance));
        ((SettingsHost) screen).rodel$rootContext(appearance);
        return true;
    }
}
