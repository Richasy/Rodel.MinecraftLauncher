package io.github.richasy.rodel.ui.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.terraformersmc.modmenu.util.mod.Mod;
import io.github.richasy.rodel.ui.compat.ModBrowserDetails;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.FormattedText;
import net.minecraft.util.FormattedCharSequence;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.widget.DescriptionListWidget", remap = false)
public abstract class ModBrowserDetailsLayoutMixin<E extends AbstractSelectionList.Entry<E>>
    extends AbstractSelectionList<E> {
    @Shadow(remap = false) private Mod selectedMod;
    @Unique private static final String RODEL_SPLIT =
        "Lnet/minecraft/client/gui/Font;split(Lnet/minecraft/network/chat/FormattedText;I)Ljava/util/List;";

    protected ModBrowserDetailsLayoutMixin(Minecraft minecraft, int width, int height, int top, int rowHeight) {
        super(minecraft, width, height, top, rowHeight);
    }

    @Override
    public int getRowLeft() {
        var browser = ((ModBrowserList) this).rodel$browser();
        // The list starts one unit inside the panel; metadata uses the panel's density inset.
        return browser == null ? super.getRowLeft()
            : getX() + browser.rodel$modernContext().density().inset() - 1;
    }

    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$contentWidth(CallbackInfoReturnable<Integer> callback) {
        if (((ModBrowserList) this).rodel$browser() != null) {
            callback.setReturnValue(Math.max(1, scrollBarX() - 2 - getRowLeft()));
        }
    }

    // Mod Menu 21 rebuildUI has 13 split sites. These are the ordinary description,
    // headings, links, license and credit-role text; none retains native decorative indents.
    @WrapOperation(method = "rebuildUI", at = {
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 0),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 2),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 3),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 5),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 6),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 7),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 8),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 9),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 10),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 11)
    }, require = 10, allow = 10)
    private List<FormattedCharSequence> rodel$regularMetadata(Font font, FormattedText text, int nativeWidth,
                                                            Operation<List<FormattedCharSequence>> original) {
        return rodel$split(font, text, nativeWidth, 0, original);
    }

    @WrapOperation(method = "lambda$rebuildUI$0", at = @At(value = "INVOKE", target = RODEL_SPLIT),
        require = 1, allow = 1)
    private List<FormattedCharSequence> rodel$linkedMetadata(Font font, FormattedText text, int nativeWidth,
                                                           Operation<List<FormattedCharSequence>> original) {
        return rodel$split(font, text, nativeWidth, 0, original);
    }

    // The update and child-update headings reserve the same badge advance used when drawn.
    @WrapOperation(method = "rebuildUI", at = {
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 1),
        @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 4)
    }, require = 2, allow = 2)
    private List<FormattedCharSequence> rodel$updateMetadata(Font font, FormattedText text, int nativeWidth,
                                                           Operation<List<FormattedCharSequence>> original) {
        var browser = ((ModBrowserList) this).rodel$browser();
        return rodel$split(font, text, nativeWidth,
            browser == null ? 0 : ModBrowserDetails.updateAdvance(browser), original);
    }

    // Only the actual credit-name split can become a MailableContactEntry.
    @WrapOperation(method = "rebuildUI", at = @At(value = "INVOKE", target = RODEL_SPLIT, ordinal = 12),
        require = 1, allow = 1)
    private List<FormattedCharSequence> rodel$contactMetadata(Font font, FormattedText text, int nativeWidth,
                                                            Operation<List<FormattedCharSequence>> original) {
        var browser = ((ModBrowserList) this).rodel$browser();
        int suffix = 0;
        if (browser != null && selectedMod != null) {
            var contact = selectedMod.getContact(text.getString());
            if (contact != null && contact.get("email").isPresent()) {
                suffix = ModBrowserDetails.contactAdvance(browser, font);
            }
        }
        return rodel$split(font, text, nativeWidth, suffix, original);
    }

    @Unique
    private List<FormattedCharSequence> rodel$split(Font font, FormattedText text, int nativeWidth, int adornment,
                                                  Operation<List<FormattedCharSequence>> original) {
        var browser = ((ModBrowserList) this).rodel$browser();
        return browser == null ? original.call(font, text, nativeWidth)
            : ModBrowserDetails.split(browser, font, text, getRowWidth() - adornment);
    }
}
