package io.github.richasy.rodel.ui.worlds;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.network.chat.Component;
import net.minecraft.util.Util;
import net.minecraft.util.worldupdate.UpgradeProgress;

final class WorldBranchProgressWidget extends AbstractWidget {
    private final UpgradeProgress progress;
    private final WorldBranchPage page;

    WorldBranchProgressWidget(UpgradeProgress progress, WorldBranchPage page) {
        super(0, 0, 1, page.density().tight() ? 90 : 126, Component.empty());
        this.progress = progress;
        this.page = page;
    }

    @Override protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int x, int y, float partialTick) {
        var total = progress.getTotalFileFixStats();
        var type = progress.getTypeFileFixStats();
        var running = progress.getRunningFileFixerStats();
        boolean tight = page.density().tight();
        int body = page.density().body();
        int detail = page.density().detail();
        Component status = progress.getType() == null ? Component.translatable("upgradeWorld.info.scanning") : progress.getType().label();
        page.text(graphics, status, getX(), getY(), body, page.theme().ink(), false);
        bar(graphics, getY() + (tight ? 12 : 16), total.getProgress(), total.totalOperations() > 0);
        page.text(graphics, Component.translatable("upgradeWorld.info.converted", total.finishedOperations()),
            getX(), getY() + (tight ? 22 : 28), detail, page.theme().muted(), true);
        page.text(graphics, Component.translatable("upgradeWorld.info.total", total.totalOperations()),
            getX(), getY() + (tight ? 31 : 42), detail, page.theme().muted(), true);
        bar(graphics, getY() + (tight ? 44 : 62), type.getProgress(), type.totalOperations() > 0);
        page.text(graphics, Component.translatable("upgradeWorld.progress.percentage", (int) (type.getProgress() * 100)),
            getX(), getY() + (tight ? 54 : 74), detail, page.theme().muted(), true);
        bar(graphics, getY() + (tight ? 67 : 98), running.getProgress(), running.totalOperations() > 0);
        page.text(graphics, Component.translatable("upgradeWorld.info.file_fix_stage",
            running.finishedOperations(), running.totalOperations()), getX(), getY() + (tight ? 77 : 110), detail, page.theme().muted(), true);
    }

    private void bar(GuiGraphicsExtractor graphics, int y, float value, boolean known) {
        graphics.fill(getX(), y, getRight(), y + 5, page.theme().edge());
        if (known) {
            int filled = Math.round(getWidth() * Math.clamp(value, 0, 1));
            graphics.fill(getX(), y, getX() + filled, y + 5, page.theme().primary());
        } else {
            int segment = Math.max(8, getWidth() / 5);
            int offset = (int) (Util.getMillis() % 1400 * (getWidth() - segment) / 1400);
            graphics.fill(getX() + offset, y, getX() + offset + segment, y + 5, page.theme().primary());
        }
    }

    @Override protected void updateWidgetNarration(NarrationElementOutput output) {}
}
