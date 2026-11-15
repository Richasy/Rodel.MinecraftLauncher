package io.github.richasy.rodel.ui.compat;

import com.terraformersmc.modmenu.gui.widget.DescriptionListWidget;
import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.SpriteIconButton;
import net.minecraft.network.chat.Component;

public interface ModBrowserAccess {
    EditBox rodel$search();
    SpriteIconButton rodel$filters();
    AbstractWidget rodel$sorting();
    AbstractWidget rodel$libraries();
    ModListWidget rodel$list();
    SpriteIconButton rodel$configure();
    AbstractWidget rodel$website();
    AbstractWidget rodel$issues();
    DescriptionListWidget rodel$description();
    Component rodel$count(boolean combined, boolean total);
}
