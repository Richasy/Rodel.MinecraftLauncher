package io.github.richasy.rodel.ui.settings;

import java.util.ArrayList;
import java.util.List;
import java.util.stream.IntStream;
import io.github.richasy.rodel.ui.mixin.OptionCaptionAccessor;
import net.minecraft.client.OptionInstance;
import net.minecraft.client.Options;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.OptionsList;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

final class VideoSettingsLayout {
    private VideoSettingsLayout() {}

    static void arrange(OptionsList list, Screen screen, Options options) {
        List<OptionsList.AbstractEntry> original = List.copyOf(list.children());
        int start = -1;
        int end = original.size();
        for (int i = 0; i < original.size(); i++) {
            if (original.get(i) instanceof OptionsList.HeaderEntry) {
                if (start < 0) start = i;
                else { end = i; break; }
            }
        }
        if (start < 0) {
            org.slf4j.LoggerFactory.getLogger("Rodel UI").warn("Native video display group is missing; retaining its original row order");
            return;
        }
        List<OptionsList.OptionInstanceWidget> remaining = new ArrayList<>();
        List<OptionsList.AbstractEntry> extra = new ArrayList<>();
        for (var entry : original.subList(start + 1, end)) {
            if (entry instanceof NativeOptionRows row) remaining.addAll(row.rodel$options());
            else extra.add(entry);
        }
        List<OptionsList.OptionInstanceWidget> window = new ArrayList<>();
        take(remaining, window, options.fullscreen());
        take(remaining, window, options.exclusiveFullscreen());
        for (int i = 0; i < remaining.size(); i++) {
            var item = remaining.get(i);
            if (item.optionInstance() != null && SettingsText.hasKey(
                ((OptionCaptionAccessor<?>) (Object) item.optionInstance()).rodel$caption(), "options.fullscreen.exclusive.mode")) {
                AbstractWidget choice = resolutionChoice(item.optionInstance(), options);
                choice.active = item.widget().active;
                choice.visible = item.widget().visible;
                window.add(new OptionsList.OptionInstanceWidget(choice, item.optionInstance()));
                remaining.remove(i);
                break;
            }
        }
        List<OptionsList.OptionInstanceWidget> frameRate = new ArrayList<>();
        take(remaining, frameRate, options.framerateLimit());
        take(remaining, frameRate, options.enableVsync());
        take(remaining, frameRate, options.inactivityFpsLimit());
        List<OptionsList.AbstractEntry> result = new ArrayList<>(original.subList(0, start));
        group(result, window, screen, "rodel.settings.group.window");
        group(result, frameRate, screen, "rodel.settings.group.frame_rate");
        group(result, remaining, screen, "rodel.settings.group.interface");
        result.addAll(extra);
        result.addAll(original.subList(end, original.size()));
        list.replaceEntries(result);
    }

    private static void take(List<OptionsList.OptionInstanceWidget> source,
                             List<OptionsList.OptionInstanceWidget> target, OptionInstance<?> option) {
        for (int i = 0; i < source.size(); i++) {
            if (source.get(i).optionInstance() == option) {
                target.add(source.remove(i));
                return;
            }
        }
    }

    private static void group(List<OptionsList.AbstractEntry> target, List<OptionsList.OptionInstanceWidget> controls,
                              Screen screen, String title) {
        if (controls.isEmpty()) return;
        target.add(new OptionsList.HeaderEntry(screen, Component.translatable(title), 0));
        controls.forEach(control -> target.add(new OptionsList.Entry(List.of(control), screen)));
    }

    @SuppressWarnings("unchecked")
    private static <T> AbstractWidget resolutionChoice(OptionInstance<T> option, Options options) {
        if (!(option.values() instanceof OptionInstance.IntRange range)
            || !(option.values() instanceof OptionInstance.SliderableValueSet<T> slider)) {
            throw new IllegalStateException("Native fullscreen mode is not a finite integer range");
        }
        List<T> modes = IntStream.rangeClosed(range.minInclusive(), range.maxInclusive())
            .mapToObj(index -> slider.fromSliderValue(range.toSliderValue(index))).toList();
        // The mixin accessor carries the same T as its OptionInstance target.
        var presentation = (OptionCaptionAccessor<T>) (Object) option;
        AbstractWidget result = new OptionInstance.Enum<>(modes, option.codec())
            .createButton(presentation.rodel$tooltipSupplier(), options, 0, 0, 200, OptionInstance.NO_ACTION)
            .apply(option);
        if (!(result instanceof NativeCycleChoices choices)) throw new IllegalStateException("Native enum factory did not create a cycle control");
        choices.rodel$valueOnlyFormatter();
        return result;
    }
}
