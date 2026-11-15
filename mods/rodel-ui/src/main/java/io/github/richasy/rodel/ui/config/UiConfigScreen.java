package io.github.richasy.rodel.ui.config;

import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import java.io.IOException;
import java.util.EnumSet;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.client.gui.components.MultiLineTextWidget;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.toasts.SystemToast;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.options.OptionsSubScreen;
import net.minecraft.network.chat.CommonComponents;
import net.minecraft.network.chat.Component;
import org.slf4j.LoggerFactory;

/** Edits the mod's own appearance settings using native scrolling, keyboard navigation and narration. */
public final class UiConfigScreen extends OptionsSubScreen {
    private final EnumSet<Effect> saved = UiConfig.saved();
    private final EnumSet<Effect> draft = saved.clone();
    private MultiLineTextWidget restart;

    /** Creates a settings page that returns to the exact screen that opened it. */
    public UiConfigScreen(Screen parent) {
        super(parent, Minecraft.getInstance().options, Component.translatable("rodel.config.title"));
    }

    /** {@inheritDoc} */
    @Override protected void addTitle() {
        layout.setHeaderHeight(46);
        var header = layout.addToHeader(LinearLayout.vertical().spacing(5));
        header.defaultCellSetting().alignHorizontallyCenter();
        header.addChild(new StringWidget(title, font));
        restart = header.addChild(new MultiLineTextWidget(Component.translatable("rodel.config.restart"), font)
            .setCentered(true));
    }

    /** {@inheritDoc} */
    @Override protected void addOptions() {
        for (Effect effect : Effect.values()) {
            var toggle = CycleButton.onOffBuilder(draft.contains(effect))
                .create(0, 0, 310, 20, Component.translatable(effect.label()), (button, enabled) -> {
                    if (enabled) draft.add(effect);
                    else draft.remove(effect);
                });
            list.addSmall(toggle, null);
        }
    }

    /** {@inheritDoc} */
    @Override protected void addFooter() {
        var actions = layout.addToFooter(LinearLayout.horizontal().spacing(8));
        actions.addChild(Button.builder(Component.translatable("rodel.config.save"), button -> save()).width(100).build());
        actions.addChild(Button.builder(CommonComponents.GUI_CANCEL, button -> onClose()).width(100).build());
    }

    /** {@inheritDoc} */
    @Override protected void repositionElements() {
        restart.setMaxWidth(Math.clamp(width - 24, 1, 310));
        super.repositionElements();
    }

    /** Returns directly to the source page and discards any unsaved edits. */
    @Override public void onClose() {
        minecraft.gui.setScreen(lastScreen);
    }

    /** This page has no edits to Minecraft's own options to commit. */
    @Override public void removed() {}

    private void save() {
        try {
            if (!saved.equals(draft)) UiConfig.save(draft);
            onClose();
        } catch (IOException error) {
            LoggerFactory.getLogger("Rodel UI").error("Could not save Rodel UI settings", error);
            SystemToast.add(minecraft.gui.toastManager(), SystemToast.SystemToastId.PACK_LOAD_FAILURE,
                Component.translatable("rodel.config.saveError"), Component.translatable("rodel.config.retry"));
        }
    }
}
