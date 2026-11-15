package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.widget.DescriptionListWidget;
import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import io.github.richasy.rodel.ui.compat.ModBrowserAccess;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.SpriteIconButton;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.gen.Invoker;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.ModsScreen", remap = false)
public interface ModBrowserAccessMixin extends ModBrowserAccess {
    @Override @Accessor("searchBox") EditBox rodel$search();
    @Override @Accessor("filtersButton") SpriteIconButton rodel$filters();
    @Override @Accessor("sortingButton") AbstractWidget rodel$sorting();
    @Override @Accessor("librariesButton") AbstractWidget rodel$libraries();
    @Override @Accessor("modList") ModListWidget rodel$list();
    @Override @Accessor("configureButton") SpriteIconButton rodel$configure();
    @Override @Accessor("websiteButton") AbstractWidget rodel$website();
    @Override @Accessor("issuesButton") AbstractWidget rodel$issues();
    @Override @Accessor("descriptionListWidget") DescriptionListWidget rodel$description();
    @Override @Invoker("computeModCountText") Component rodel$count(boolean combined, boolean total);
}
