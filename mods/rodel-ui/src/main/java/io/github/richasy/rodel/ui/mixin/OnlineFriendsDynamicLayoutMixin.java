package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.AbstractContainerWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.PlainTextButton;
import net.minecraft.client.gui.components.SpriteIconButton;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.narration.NarratableEntry;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(targets = "net.minecraft.client.gui.screens.friends.AddFriendWidget")
public abstract class OnlineFriendsDynamicLayoutMixin implements NetworkMenuLayouts.FriendComposer {
    @Shadow @Final private EditBox editBox;
    @Shadow @Final private SpriteIconButton addButton;
    @Shadow @Final private PlainTextButton profileNameButton;
    @Shadow @Final private LinearLayout layout;
    @Unique private StringWidget rodel$profileLabel;
    @Unique private NativeTextBox rodel$editor;

    @Override
    public void rodel$arrange(NativeMenuPage page, int width) {
        if (layout == null) return;
        if (rodel$profileLabel == null) {
            var widgets = new ArrayList<AbstractWidget>();
            layout.visitWidgets(widgets::add);
            rodel$profileLabel = widgets.stream().filter(StringWidget.class::isInstance)
                .map(StringWidget.class::cast).findFirst().orElse(null);
        }
        var density = page.density();
        if (rodel$editor == null) {
            rodel$editor = new NativeTextBox(page, editBox);
            var container = (AbstractContainerWidget) (Object) this;
            if (container.getFocused() == editBox) container.setFocused(rodel$editor);
        }
        rodel$editor.setRectangle(Math.max(0, width - density.control() - density.gap()), density.control(), editBox.getX(), editBox.getY());
        addButton.setSize(density.control(), density.control());
        LinearLayout input = LinearLayout.horizontal().spacing(density.gap());
        input.addChild(rodel$editor);
        input.addChild(addButton);
        LinearLayout profile = LinearLayout.horizontal().spacing(density.gap() / 2);
        int labelWidth = 0;
        if (rodel$profileLabel != null) {
            labelWidth = NetworkRowLayout.textWidth(rodel$profileLabel.getMessage(), density.detail());
            rodel$profileLabel.setMaxWidth(0);
            rodel$profileLabel.setSize(labelWidth, density.control());
            profile.addChild(rodel$profileLabel);
        }
        int nameWidth = Math.min(Math.max(0, width - labelWidth - density.gap() / 2),
            NetworkRowLayout.textWidth(profileNameButton.getMessage(), density.detail()));
        profileNameButton.setSize(nameWidth, density.control());
        profile.addChild(profileNameButton);
        layout.removeChildren();
        layout.defaultCellSetting().padding(0);
        layout.spacing(density.gap() / 2);
        layout.addChild(input);
        layout.addChild(profile);
        layout.arrangeElements();
        ((AbstractWidget) (Object) this).setSize(width, layout.getHeight());
    }

    @Inject(method = "children", at = @At("HEAD"), cancellable = true)
    private void rodel$inputChildren(CallbackInfoReturnable<List<? extends GuiEventListener>> callback) {
        if (rodel$editor != null) callback.setReturnValue(List.of(rodel$editor, addButton, profileNameButton));
    }

    @Inject(method = "getNarratables", at = @At("HEAD"), cancellable = true)
    private void rodel$inputNarration(CallbackInfoReturnable<Collection<? extends NarratableEntry>> callback) {
        if (rodel$editor != null) callback.setReturnValue(List.of(rodel$editor, addButton, profileNameButton));
    }

    @Inject(method = "applyState", at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.screen() instanceof NetworkMenuLayouts.Friends) NetworkMenuLayouts.layout(page);
    }

    @Redirect(method = "lambda$extractWidgetRenderState$0", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/components/AbstractWidget;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private static void rodel$caption(AbstractWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.rendering() && widget instanceof StringWidget) {
            int size = page.density().detail();
            NetworkRowLayout.text(page, graphics, widget.getMessage(), widget.getX(), widget.getY() + (widget.getHeight() - size) / 2,
                widget.getWidth(), size, page.theme().muted(), mouseX, mouseY);
        } else widget.extractRenderState(graphics, mouseX, mouseY, tick);
    }
}
