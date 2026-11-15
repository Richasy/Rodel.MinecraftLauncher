package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import io.github.richasy.rodel.ui.menus.NativeMenus;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.NativeDialog;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import io.github.richasy.rodel.ui.worlds.WorldPageHost;
import io.github.richasy.rodel.ui.worlds.WorldSurface;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import io.github.richasy.rodel.ui.worlds.WorldEditPage;
import io.github.richasy.rodel.ui.worlds.WorldBranchMenus;
import net.minecraft.client.gui.screens.worldselection.EditWorldScreen;
import net.minecraft.client.gui.screens.worldselection.WorldCreationGameRulesScreen;
import net.minecraft.client.gui.screens.worldselection.ExperimentsScreen;
import net.minecraft.client.gui.screens.GenericMessageScreen;
import net.minecraft.client.gui.screens.GenericWaitingScreen;
import net.minecraft.client.gui.screens.BackupConfirmScreen;
import net.minecraft.client.gui.screens.LevelLoadingScreen;
import net.minecraft.client.gui.screens.ProgressScreen;
import net.minecraft.client.gui.screens.worldselection.OptimizeWorldScreen;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import net.minecraft.client.gui.screens.worldselection.SelectWorldScreen;
import net.minecraft.client.gui.Gui;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.gui.screens.ConfirmScreen;
import net.minecraft.client.gui.screens.NoticeWithLinkScreen;
import net.minecraft.client.gui.screens.CreditsAndAttributionScreen;
import net.minecraft.client.gui.screens.telemetry.TelemetryInfoScreen;
import net.minecraft.client.gui.screens.multiplayer.WarningScreen;
import net.minecraft.client.gui.screens.options.UnsupportedGraphicsWarningScreen;
import net.minecraft.client.gui.screens.packs.PackSelectionScreen;
import com.mojang.realmsclient.gui.screens.RealmsGenericErrorScreen;
import com.mojang.realmsclient.gui.screens.RealmsConfirmScreen;
import com.mojang.realmsclient.gui.screens.RealmsClientOutdatedScreen;
import com.mojang.realmsclient.gui.screens.RealmsParentalConsentScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Gui.class)
public abstract class NativeUiTransitionMixin {
    @Shadow public abstract Screen screen();
    @Unique private MenuContext rodel$worldAppearance;
    @Unique private MenuContext rodel$menuAppearance;

    @Inject(method = "setScreen", at = @At("HEAD"))
    private void rodel$nativeSurface(Screen next, CallbackInfo callback) {
        Screen previous = screen();
        MenuContext priorAppearance = UiContexts.owned(previous);
        if (priorAppearance != null) rodel$menuAppearance = priorAppearance;
        if (next == null || next == previous) return;
        if (next instanceof TitleScreen || next.getClass() == SelectWorldScreen.class) rodel$worldAppearance = null;
        boolean realms = next.getClass().getName().startsWith("com.mojang.realmsclient.");
        boolean missingContent = next.getClass().getName().equals(
            "net.fabricmc.fabric.impl.client.registry.sync.validate.DetailedBackupConfirmScreen");
        boolean missingDetails = next.getClass().getName().equals(
            "net.fabricmc.fabric.impl.client.registry.sync.validate.DetailsScreen");
        boolean worldRoot = next.getClass() == SelectWorldScreen.class || next.getClass() == CreateWorldScreen.class
            || next.getClass() == EditWorldScreen.class;
        boolean waiting = next.getClass() == GenericMessageScreen.class || next.getClass() == GenericWaitingScreen.class
            || next.getClass() == LevelLoadingScreen.class || next.getClass() == ProgressScreen.class
            || next.getClass() == OptimizeWorldScreen.class;
        if (!UiConfig.enabled(Effect.WORLDS) && (worldRoot || waiting || missingContent || missingDetails
            || next.getClass() == BackupConfirmScreen.class)) return;
        if (!UiConfig.enabled(Effect.MENUS) && (realms || NativeMenus.supported(next))) return;
        if (!realms && !missingContent && !missingDetails
            && next.getClass() != net.minecraft.client.gui.components.PopupScreen.class
            && !next.getClass().getName().startsWith("net.minecraft.client.gui.screens.")) return;
        MenuContext source = UiContexts.owned(previous);
        if (source == null && UiConfig.enabled(Effect.MENUS) && NativeMenus.inGameRoot(next)) {
            // Quick Play may enter a world without ever constructing an owned title page.
            var client = Minecraft.getInstance();
            if (rodel$menuAppearance == null && previous == null && client.level != null)
                rodel$menuAppearance = rodel$fallbackAppearance();
            source = rodel$menuAppearance;
        }
        if (previous != null && NativeMenuPage.get(previous) == null
            && (previous.getClass().getName().startsWith("com.mojang.realmsclient.")
            || previous.getClass() == GenericMessageScreen.class || previous.getClass() == GenericWaitingScreen.class)) {
            MenuContext inherited = ((SettingsHost) previous).rodel$rootContext();
            if (inherited != null) source = inherited;
        }
        MenuContext inheritedWorld = previous == null ? null : ((SettingsHost) previous).rodel$rootContext();
        MenuContext worldOwner = source instanceof WorldSurface ? source : inheritedWorld instanceof WorldSurface ? inheritedWorld : null;
        if (source == null) source = worldOwner;
        if (worldOwner != null && waiting) rodel$worldAppearance = worldOwner;
        if (source == null && waiting) source = rodel$worldAppearance;
        boolean titleDisabled = !UiConfig.enabled(Effect.TITLE);
        if (source == null && (titleDisabled && (worldRoot || realms || NativeMenus.supported(next))
            || worldRoot && !UiConfig.enabled(Effect.MENUS)))
            source = rodel$fallbackAppearance();
        if (source == null) return;
        if (next.getClass() == net.minecraft.client.gui.screens.options.OptionsScreen.class
            && NativeMenuPage.get(previous) != null) {
            ((SettingsHost) next).rodel$rootContext(source);
        }
        if (next.getClass() == net.minecraft.client.gui.screens.DisconnectedScreen.class) {
            ((SettingsHost) next).rodel$rootContext(source);
            var host = (DialogHost) next;
            if (host.rodel$dialog() == null) host.rodel$dialog(new NativeDialog(next, source, false));
            return;
        }
        if (WorldBranchMenus.attach(next, source)) {
            ((SettingsHost) next).rodel$rootContext(worldOwner != null ? worldOwner : source);
            return;
        }
        if (NativeMenus.attach(next, source)) return;
        if (worldOwner instanceof WorldSubpage child && child.moreNavigation() != null
            && next == child.moreNavigation().parent()) child.moreNavigation().returningToParent();
        if (worldOwner != null && !(next instanceof TitleScreen)
            && next.getClass() != SelectWorldScreen.class && next.getClass() != CreateWorldScreen.class)
            ((SettingsHost) next).rodel$rootContext(worldOwner);
        if (waiting) ((SettingsHost) next).rodel$rootContext(source);
        if (next.getClass() == SelectWorldScreen.class || next.getClass() == CreateWorldScreen.class) {
            WorldPageHost host = (WorldPageHost) next;
            if (host.rodel$worldPage() == null) host.rodel$worldPage(new WorldPage(next, source));
        }
        if (next.getClass() == EditWorldScreen.class) {
            WorldPageHost host = (WorldPageHost) next;
            if (host.rodel$worldPage() == null) host.rodel$worldPage(new WorldEditPage(next, source));
        }
        if (worldOwner != null && (next.getClass() == WorldCreationGameRulesScreen.class
            || next.getClass() == ExperimentsScreen.class || next.getClass() == PackSelectionScreen.class)) {
            WorldPageHost host = (WorldPageHost) next;
            if (host.rodel$worldPage() == null) host.rodel$worldPage(new WorldSubpage(next, source,
                worldOwner instanceof WorldSurface world ? world.moreNavigation() : null));
        }
        if (realms) ((SettingsHost) next).rodel$rootContext(source);
        if (next.getClass() == PackSelectionScreen.class) {
            SettingsHost host = (SettingsHost) next;
            if (host.rodel$nativeParent() == null) host.rodel$nativeParent(previous);
        } else if (waiting || next.getClass() == BackupConfirmScreen.class || missingContent || missingDetails
            || next instanceof ConfirmScreen || next instanceof NoticeWithLinkScreen || next instanceof WarningScreen
            || next instanceof UnsupportedGraphicsWarningScreen || next instanceof CreditsAndAttributionScreen
            || next instanceof TelemetryInfoScreen
            || next.getClass() == RealmsGenericErrorScreen.class || next.getClass() == RealmsConfirmScreen.class
            || next.getClass() == RealmsClientOutdatedScreen.class || next.getClass() == RealmsParentalConsentScreen.class) {
            DialogHost host = (DialogHost) next;
            if (host.rodel$dialog() == null) host.rodel$dialog(new NativeDialog(next, source,
                missingDetails || next instanceof CreditsAndAttributionScreen || next instanceof TelemetryInfoScreen));
        }
    }

    @Unique
    private static MenuContext rodel$fallbackAppearance() {
        var client = Minecraft.getInstance();
        var window = client.getWindow();
        return new TitleMenu(client, window.getGuiScaledWidth(), window.getGuiScaledHeight());
    }
}
