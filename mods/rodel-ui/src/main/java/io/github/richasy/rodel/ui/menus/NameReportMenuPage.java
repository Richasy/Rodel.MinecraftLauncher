package io.github.richasy.rodel.ui.menus;

import java.util.List;
import io.github.richasy.rodel.ui.MenuContext;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Checkbox;
import net.minecraft.client.gui.components.MultiLineEditBox;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.screens.Screen;

/** Groups the native name-report form without replacing its editor, validation or consent. */
public final class NameReportMenuPage extends NativeMenuPage {
    public NameReportMenuPage(Screen screen, MenuContext appearance) {
        super(screen, appearance);
    }

    @Override
    public void initialized() {
        super.initialized();
        List<AbstractWidget> widgets = screen().children().stream()
            .filter(AbstractWidget.class::isInstance).map(AbstractWidget.class::cast).toList();
        MultiLineEditBox editor = widgets.stream().filter(MultiLineEditBox.class::isInstance)
            .map(MultiLineEditBox.class::cast).findFirst().orElse(null);
        Checkbox consent = widgets.stream().filter(Checkbox.class::isInstance)
            .map(Checkbox.class::cast).findFirst().orElse(null);
        List<StringWidget> labels = widgets.stream().filter(StringWidget.class::isInstance)
            .map(StringWidget.class::cast).toList();
        List<Button> actions = widgets.stream().filter(Button.class::isInstance).map(Button.class::cast).toList();
        if (editor == null || consent == null || labels.size() != 3 || actions.size() != 2) return;

        // Keep the native text field's width, wrapping, cursor and IME coordinate system together.
        int width = editor.getWidth();
        int left = (screen().width - width) / 2;
        int gap = density().gap();
        int y = 12;
        for (int i = 0; i < labels.size(); i++) {
            StringWidget label = labels.get(i);
            label.setPosition(left, y);
            y += label.getHeight() + (i == 0 ? gap : i == 1 ? gap * 2 : 4);
        }
        editor.setPosition(left, y);
        // Native character count is drawn beneath the editor; reserve its full line.
        consent.setPosition(left, editor.getBottom() + 20);
        int actionTop = consent.getBottom() + gap;
        int actionWidth = (width - gap) / 2;
        actions.get(0).setRectangle(actionWidth, density().control(), left, actionTop);
        actions.get(1).setRectangle(width - actionWidth - gap, density().control(),
            left + actionWidth + gap, actionTop);
    }
}
