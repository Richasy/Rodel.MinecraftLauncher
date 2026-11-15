package io.github.richasy.bettertools;

import net.minecraft.world.inventory.tooltip.TooltipComponent;
import net.minecraft.world.item.component.ItemContainerContents;
import net.minecraft.network.chat.Component;

/** Native inventory hover preview; it does not create a menu, duplicate inventory or own mutable stacks. */
public record CubeTooltip(ItemContainerContents contents, int picked, Component title) implements TooltipComponent { }
