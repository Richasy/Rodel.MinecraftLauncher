package io.github.richasy.rodel.ui.workstation;

import io.github.richasy.rodel.ui.workstation.mixin.WorkstationSlotAccess;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.client.gui.screens.inventory.CraftingScreen;
import net.minecraft.client.gui.screens.inventory.FurnaceScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.util.Mth;
import net.minecraft.world.inventory.AbstractFurnaceMenu;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/** Lays out and paints the two vanilla workstations in integer GUI units. */
public enum WorkstationSurface {
    FURNACE(new ItemStack(Items.FURNACE), 0xFF393B3F, 0xFF202124, 0xFF505256, 0xFF2B2C30,
        0xFF313236, 0xFF3D3F43, 0xFF535559, 0xFFEBDCC6, 0xFFC5BDB2, 0xFFE6A24F,
        new Recess(0xFF242529, 0xFF18191C, 0xFF45474C)),
    CRAFTING(new ItemStack(Items.CRAFTING_TABLE), 0xFFD8B181, 0xFF795638, 0xFFF4D4A6, 0xFFEACDA8,
        0xFFB9A183, 0xFFC6AE90, 0xFFAD8C68, 0xFF493020, 0xFF62432E, 0xFF995B2D,
        new Recess(0xFFA98B68, 0xFF886848, 0xFFD0AC7E));

    /** Width of the panel, including two eight-unit margins around nine native slots. */
    public static final int WIDTH = 178;
    /** Height shared by the workstation and its recipe side page. */
    public static final int HEIGHT = 212;
    private static final int CONTENT_INSET = 8;
    /** Visible and interactive extent of the output well. */
    public static final int RESULT_SIZE = 32;
    /** Centers the native item inside the larger output well. */
    public static final int RESULT_INSET = (RESULT_SIZE - 16) / 2;
    /** Header action's left edge, aligned with the content's right edge. */
    public static final int BOOK_X = WIDTH - CONTENT_INSET - 18;
    /** Header action's top edge. */
    public static final int BOOK_Y = 3;

    private static final int HEADER_END = 24;
    private static final int WORK_END = 106;
    private static final int INVENTORY_X = CONTENT_INSET;
    private static final int INVENTORY_Y = WORK_END + 20;
    private static final int HOTBAR_Y = INVENTORY_Y + 60;
    private static final int SLOT_SIZE = 18;
    private static final int WORK_SLOT_SIZE = 22;
    private static final int WORK_ITEM_INSET = (WORK_SLOT_SIZE - 16) / 2;
    private static final int PROCESS_CENTER_Y = (HEADER_END + WORK_END) / 2;
    private static final int INPUT_TOP = PROCESS_CENTER_Y - 3 * WORK_SLOT_SIZE / 2;
    private static final int ARROW_LENGTH = 18;
    private static final int CRAFTING_GAP = 6;
    private static final int CRAFTING_INPUT_LEFT =
        (WIDTH - (3 * WORK_SLOT_SIZE + CRAFTING_GAP * 2 + ARROW_LENGTH + RESULT_SIZE)) / 2;
    private static final int RESULT_LEFT = CRAFTING_INPUT_LEFT + 3 * WORK_SLOT_SIZE + CRAFTING_GAP * 2 + ARROW_LENGTH;
    private static final int COLUMNS = 9;
    private static final int INVENTORY_SLOTS = 36;
    private static final ItemStack RECIPE_BOOK_ICON = new ItemStack(Items.KNOWLEDGE_BOOK);
    private static final ThreadLocal<Boolean> EXTRACTING_SMALL_ITEM = new ThreadLocal<>();

    private final ItemStack icon;
    private final int frame;
    private final int edge;
    private final int light;
    private final int bay;
    private final int checkerA;
    private final int checkerB;
    private final int rim;
    private final int text;
    private final int secondary;
    private final int accent;
    private final Recess toolWell;

    WorkstationSurface(ItemStack icon, int frame, int edge, int light, int bay, int checkerA, int checkerB,
                       int rim, int text, int secondary, int accent, Recess toolWell) {
        this.icon = icon;
        this.frame = frame;
        this.edge = edge;
        this.light = light;
        this.bay = bay;
        this.checkerA = checkerA;
        this.checkerB = checkerB;
        this.rim = rim;
        this.text = text;
        this.secondary = secondary;
        this.accent = accent;
        this.toolWell = toolWell;
    }

    /** Selects the two exact screens when their full surface fits at the player's GUI scale. */
    public static WorkstationSurface forScreen(Object screen) {
        if (!(screen instanceof AbstractContainerScreen<?> container)
            || container.width < WIDTH + 8 || container.height < HEIGHT + 8) return null;
        if (screen.getClass() == FurnaceScreen.class) return FURNACE;
        if (screen.getClass() == CraftingScreen.class) return CRAFTING;
        return null;
    }

    /** Positions the existing client slots so drawing, hit testing and recipe ghosts share one geometry. */
    public void placeSlots(AbstractContainerMenu menu) {
        int inventoryStart = menu.slots.size() - INVENTORY_SLOTS;
        move(resultSlot(menu), RESULT_LEFT + RESULT_INSET, PROCESS_CENTER_Y - 8);
        if (this == FURNACE) {
            int inputX = CRAFTING_INPUT_LEFT + WORK_SLOT_SIZE + WORK_ITEM_INSET;
            move(menu.getSlot(0), inputX, INPUT_TOP + WORK_ITEM_INSET);
            move(menu.getSlot(1), inputX, INPUT_TOP + 2 * WORK_SLOT_SIZE + WORK_ITEM_INSET);
        } else {
            for (int index = 0; index < COLUMNS; index++) {
                move(menu.getSlot(index + 1), CRAFTING_INPUT_LEFT + WORK_ITEM_INSET + index % 3 * WORK_SLOT_SIZE,
                    INPUT_TOP + WORK_ITEM_INSET + index / 3 * WORK_SLOT_SIZE);
            }
        }
        for (int index = 0; index < INVENTORY_SLOTS; index++) {
            int row = index / COLUMNS;
            int y = row == 3 ? HOTBAR_Y : INVENTORY_Y + row * SLOT_SIZE;
            move(menu.getSlot(inventoryStart + index), INVENTORY_X + 1 + index % COLUMNS * SLOT_SIZE, y + 1);
        }
    }

    private static void move(Slot slot, int x, int y) {
        WorkstationSlotAccess position = (WorkstationSlotAccess) slot;
        position.rodel$setX(x);
        position.rodel$setY(y);
    }

    /** Paints the header, inset work area, inventory and recessed hotbar before Minecraft draws the items. */
    public void background(GuiGraphicsExtractor g, AbstractContainerScreen<?> screen, int x, int y) {
        rect(g, x, y, WIDTH, HEIGHT, edge);
        rect(g, x + 1, y + 1, WIDTH - 2, HEIGHT - 2, frame);
        // Shallow stepped light on the material, always inside the one-pixel outer edge.
        for (int band = 0; band < 6; band++) {
            rect(g, x + 2, y + 2 + band * 3, WIDTH - 4, 3, (8 - band) << 24 | 0x00FFFFFF);
        }
        rect(g, x + 1, y + 1, WIDTH - 2, 1, light);
        rect(g, x + 1, y + 2, 1, HEIGHT - 3, this == FURNACE ? 0xFF45474B : 0xFFE6C496);
        rect(g, x + WIDTH - 2, y + 2, 1, HEIGHT - 3, this == FURNACE ? 0xFF303135 : 0xFFC79F72);
        rect(g, x + 1, y + HEADER_END - 1, WIDTH - 2, 1, edge);
        rect(g, x + CONTENT_INSET, y + HEADER_END, WIDTH - 2 * CONTENT_INSET, WORK_END - HEADER_END, bay);
        rect(g, x + CONTENT_INSET, y + WORK_END - 1, WIDTH - 2 * CONTENT_INSET, 1, rim);
        materialDetails(g, x, y);

        var menu = screen.getMenu();
        int inventoryStart = menu.slots.size() - INVENTORY_SLOTS;
        Slot result = resultSlot(menu);
        for (int index = 0; index < inventoryStart; index++) {
            Slot slot = menu.getSlot(index);
            int left = x + slot.x - WORK_ITEM_INSET;
            int top = y + slot.y - WORK_ITEM_INSET;
            if (slot == result) {
                int resultX = x + slot.x - RESULT_INSET;
                int resultY = y + slot.y - RESULT_INSET;
                recess(g, resultX, resultY, RESULT_SIZE, RESULT_SIZE, toolWell.floor(), toolWell);
                if (slot.hasItem()) g.outline(resultX, resultY, RESULT_SIZE, RESULT_SIZE, accent);
            } else {
                recess(g, left, top, WORK_SLOT_SIZE, WORK_SLOT_SIZE, checkerA, toolWell);
            }
        }

        if (menu instanceof AbstractFurnaceMenu furnace) {
            Slot input = menu.getSlot(0);
            flame(g, x + input.x + 2, y + PROCESS_CENTER_Y - 7, furnace.isLit() ? furnace.getLitProgress() : 0);
        }

        for (int index = 0; index < INVENTORY_SLOTS; index++) {
            Slot slot = menu.getSlot(inventoryStart + index);
            int left = x + slot.x - 1;
            int top = y + slot.y - 1;
            if (index < INVENTORY_SLOTS - COLUMNS) {
                int parity = (index / COLUMNS + index % COLUMNS) % 2;
                rect(g, left, top, SLOT_SIZE, SLOT_SIZE, parity == 0 ? checkerA : checkerB);
            } else {
                recess(g, left, top, SLOT_SIZE, SLOT_SIZE, toolWell.floor(), toolWell);
            }
        }

        // These seams belong to the checkerboard; the hotbar has only its nine individual wells.
        rect(g, x + CONTENT_INSET, y + INVENTORY_Y, SLOT_SIZE * COLUMNS, 1, toolWell.shadow());
        rect(g, x + CONTENT_INSET, y + INVENTORY_Y + 54 - 1, SLOT_SIZE * COLUMNS, 1, toolWell.lip());

        float progress = menu instanceof AbstractFurnaceMenu furnace ? furnace.getBurnProgress()
            : result.hasItem() ? 1 : 0;
        int inputRight = CRAFTING_INPUT_LEFT + (this == FURNACE ? 2 : 3) * WORK_SLOT_SIZE;
        int arrowX = x + (inputRight + RESULT_LEFT - ARROW_LENGTH) / 2;
        int arrowY = y + PROCESS_CENTER_Y;
        arrow(g, arrowX, arrowY, rim, ARROW_LENGTH);
        arrow(g, arrowX, arrowY, accent, Mth.ceil(ARROW_LENGTH * progress));
    }

    private void materialDetails(GuiGraphicsExtractor g, int x, int y) {
        if (this == FURNACE) {
            // Cut stone side rails. Seams stay in the frame and never cross usable slots.
            rect(g, x + 2, y + 2, 6, HEIGHT - 4, 0xFF414347);
            rect(g, x + 3, y + 3, 1, HEIGHT - 6, 0xFF494B4F);
            rect(g, x + 7, y + HEADER_END, 1, HEIGHT - HEADER_END - 2, 0xFF2B2D30);
            rect(g, x + WIDTH - 7, y + 2, 4, HEIGHT - 4, 0xFF36383C);
            rect(g, x + WIDTH - 3, y + 3, 1, HEIGHT - 6, 0xFF2C2E31);
            stoneJoint(g, x + 1, y + HEADER_END + 24);
            stoneJoint(g, x + 1, y + WORK_END);
            stoneJoint(g, x + 1, y + INVENTORY_Y);
            rect(g, x + CONTENT_INSET, y + HEADER_END, WIDTH - 2 * CONTENT_INSET, 1, 0xFF1E2022);
            rect(g, x + CONTENT_INSET, y + HEADER_END + 1, 1, WORK_END - HEADER_END - 2, 0xFF222326);
            rect(g, x + WIDTH - CONTENT_INSET - 1, y + HEADER_END + 1, 1,
                WORK_END - HEADER_END - 2, 0xFF3B3D41);
            rect(g, x + CONTENT_INSET + 1, y + HEADER_END + 1, WIDTH - 2 * CONTENT_INSET - 2, 2, 0x18000000);
            rect(g, x + CONTENT_INSET + 1, y + HEADER_END + 3, WIDTH - 2 * CONTENT_INSET - 2, 3, 0x0B000000);
            rect(g, x + CONTENT_INSET, y + WORK_END, WIDTH - 2 * CONTENT_INSET, 1, 0xFF45474B);
        } else {
            // Birch posts and two joined crosspieces, using restrained grain below the labels.
            rect(g, x + 2, y + HEADER_END, 5, HEIGHT - HEADER_END - 2, 0xFFE0B98B);
            rect(g, x + 3, y + HEADER_END + 2, 1, WORK_END - HEADER_END - 23, 0x1F795638);
            rect(g, x + 4, y + INVENTORY_Y + 16, 1, 32, 0x18795638);
            rect(g, x + WIDTH - 7, y + HEADER_END, 4, HEIGHT - HEADER_END - 2, 0xFFD3AA7A);
            rect(g, x + WIDTH - 5, y + 38, 1, WORK_END - HEADER_END - 31, 0x14795638);
            rect(g, x + WIDTH - 4, y + INVENTORY_Y + 21, 1, 36, 0x1B795638);
            rect(g, x + 2, y + WORK_END, WIDTH - 4, 1, 0xFFAA8056);
            rect(g, x + 2, y + WORK_END + 1, WIDTH - 4, 1, 0xFFE8C496);
            rect(g, x + 2, y + INVENTORY_Y + 37, 5, 1, 0xFFBF966A);
            rect(g, x + 2, y + INVENTORY_Y + 38, 5, 1, 0xFFE9C397);
            rect(g, x + 42, y + HEADER_END - 4, 47, 1, 0x12795638);
            rect(g, x + 89, y + HEADER_END - 5, 28, 1, 0x0D795638);
            rect(g, x + 116, y + INVENTORY_Y - 3, 37, 1, 0x12795638);
            rect(g, x + CONTENT_INSET, y + HEADER_END, WIDTH - 2 * CONTENT_INSET, 1, 0xFFF1D7B2);
            rect(g, x + CONTENT_INSET, y + HEADER_END + 1, 1, WORK_END - HEADER_END - 2, 0xFFF0D4AC);
        }
    }

    private static void stoneJoint(GuiGraphicsExtractor g, int x, int y) {
        rect(g, x, y, 7, 1, 0xFF202225);
        rect(g, x + 1, y + 1, 6, 1, 0xFF55575B);
    }

    /** Identifies the real menu result used for both the larger well and its hit area. */
    public Slot resultSlot(AbstractContainerMenu menu) {
        return menu.getSlot(this == FURNACE ? 2 : 0);
    }

    /** Shares the enlarged work-well bounds between rendering, hover and native slot input. */
    public int slotSize(Slot slot, AbstractContainerMenu menu) {
        if (slot == resultSlot(menu)) return RESULT_SIZE;
        return slot.index < menu.slots.size() - INVENTORY_SLOTS ? WORK_SLOT_SIZE : SLOT_SIZE;
    }

    /** Renders the real localized labels with independent space for the header action. */
    public void labels(GuiGraphicsExtractor g, Font font, Component title, Component inventoryTitle) {
        int titleWidth = (int) ((BOOK_X - 31) / 0.875F);
        Component caption = font.width(title) <= titleWidth ? title
            : Component.literal(font.plainSubstrByWidth(title.getString(), titleWidth - font.width("…")) + "…");
        smallItem(g, icon, CONTENT_INSET, 6);
        g.pose().pushMatrix();
        g.pose().translate(26, 8);
        g.pose().scale(0.875F, 0.875F);
        g.text(font, caption, 0, 0, text, false);
        g.pose().popMatrix();
        g.text(font, inventoryTitle, CONTENT_INSET, WORK_END + 6, secondary, false);
    }

    /** Paints a compact square book action on the native widget's bounds. */
    public void recipeButton(GuiGraphicsExtractor g, int x, int y, int w, int h,
                             boolean open, boolean hovered, boolean focused) {
        rect(g, x, y, w, h, open || hovered ? checkerB : bay);
        g.outline(x, y, w, h, focused ? text : open ? accent : rim);
        smallItem(g, RECIPE_BOOK_ICON, x + (w - 12) / 2, y + (h - 12) / 2);
    }

    /** Paints a recipe tab or choice while retaining its native state, icon and input. */
    public void recipeTile(GuiGraphicsExtractor g, int x, int y, int w, int h, boolean selected, boolean unavailable,
                           boolean multiple, boolean hovered, boolean focused) {
        int fill = selected || hovered ? 0xFFE5D9C2 : unavailable ? 0xFFBBB3A4 : 0xFFD6CEBE;
        rect(g, x, y, w, h, fill);
        rect(g, x, y, w, 1, 0xFFE7E0D2);
        rect(g, x, y + h - 1, w, 1, 0xFFA29988);
        if (focused || selected) g.outline(x, y, w, h, 0xFF8A7960);
        if (selected) rect(g, x + 1, y + 1, 2, h - 2, accent);
        if (multiple) rect(g, x + w - 5, y + h - 4, 3, 1, 0xFF5E554A);
    }

    /** Identifies only the native render states extracted by our compact icon drawing. */
    public static boolean extractingSmallItem() {
        return Boolean.TRUE.equals(EXTRACTING_SMALL_ITEM.get());
    }

    /** Draws a compact native icon with filtered atlas reduction, retaining its resource-pack model. */
    public static void smallItem(GuiGraphicsExtractor g, ItemStack item, int x, int y) {
        boolean previous = extractingSmallItem();
        g.pose().pushMatrix();
        EXTRACTING_SMALL_ITEM.set(true);
        try {
            g.pose().translate(x, y);
            g.pose().scale(0.75F, 0.75F);
            g.fakeItem(item, 0, 0);
        } finally {
            if (previous) EXTRACTING_SMALL_ITEM.set(true);
            else EXTRACTING_SMALL_ITEM.remove();
            g.pose().popMatrix();
        }
    }

    private void flame(GuiGraphicsExtractor g, int x, int y, float fuel) {
        // The vanilla lit-progress sprite includes an opaque gray/white backing. A procedural
        // silhouette keeps the host material visible and clips upward from the real fuel value.
        String[] pixels = {"    r       ", "    rr      ", "   ro r     ", "   ro rr    ",
            " r rooorr   ", " rrooyoor r ", " rroyyoorrr ", " rroyyyyoor ",
            "rroyyyyyyorr", "royyywyyyyor", "royywwwyyyor", " royywyyyor ", "  rooyyoor  ", "   rrrrrr   "};
        int filled = fuel <= 0 ? 0 : Mth.ceil(fuel * 13) + 1;
        for (int row = 0; row < pixels.length; row++) {
            for (int col = 0; col < pixels[row].length(); col++) {
                char pixel = pixels[row].charAt(col);
                if (pixel == ' ') continue;
                int color = row < pixels.length - filled ? 0xFF494441 : switch (pixel) {
                    case 'r' -> 0xFFB95726;
                    case 'o' -> 0xFFF28C26;
                    case 'y' -> 0xFFFFC646;
                    default -> 0xFFFFE68A;
                };
                rect(g, x + col, y + row, 1, 1, color);
            }
        }
    }

    /** Tints the native hovered slot beneath its item at the existing hit target. */
    public void highlight(GuiGraphicsExtractor g, Slot slot, AbstractContainerMenu menu) {
        if (slot == null || !slot.isHighlightable()) return;
        int size = slotSize(slot, menu);
        int inset = size > SLOT_SIZE ? (size - 16) / 2 - 1 : 1;
        if (size > SLOT_SIZE) size -= 2;
        rect(g, slot.x - inset, slot.y - inset, size, size,
            this == FURNACE ? 0x26FFF3DD : 0x26745236);
    }

    private static void recess(GuiGraphicsExtractor g, int x, int y, int w, int h, int floor, Recess material) {
        rect(g, x, y, w, h, floor);
        rect(g, x, y, w, 1, material.shadow());
        rect(g, x, y + 1, 1, h - 2, material.shadow());
        rect(g, x + w - 1, y + 1, 1, h - 2, material.lip());
        rect(g, x, y + h - 1, w, 1, material.lip());
    }

    private record Recess(int floor, int shadow, int lip) {}

    private static void rect(GuiGraphicsExtractor g, int x, int y, int w, int h, int color) {
        g.fill(x, y, x + w, y + h, color);
    }

    private static void arrow(GuiGraphicsExtractor g, int x, int y, int color, int progress) {
        if (progress <= 0) return;
        int shaft = ARROW_LENGTH - 6;
        rect(g, x, y - 2, Math.min(shaft, progress), 4, color);
        for (int step = 0; step < 6 && shaft + step < progress; step++) {
            rect(g, x + shaft + step, y - 6 + step, 1, 12 - 2 * step, color);
        }
    }
}
