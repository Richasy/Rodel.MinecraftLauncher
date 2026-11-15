package vazkii.patchouli.common.book;

import java.util.*;
import io.github.richasy.rodel.bridge.GameBridgeChecks.Text;

/** Synthetic loaded book model exposes one localized page and independently controlled unlock state. */
public final class BookRegistry {
    public static final BookRegistry INSTANCE = new BookRegistry();
    public static boolean locked, pageLocked;
    public final Map<String, Book> books = Map.of("fixture:guide", new Book());
    public static final class Book { public Contents getContents() { return new Contents(); } }
    public static final class Contents { public final Map<String, Entry> entries = Map.of("fixture:feed", new Entry()); }
    public static final class Entry {
        public boolean isLocked() { return locked; }
        public Text getName() { return new Text("养殖手册"); }
        public List<Page> getPages() { return List.of(new Page()); }
    }
    public static final class Page {
        public final String sourceObject = "{\"type\":\"patchouli:text\",\"text\":\"guide.feed\"}";
        public boolean isPageUnlocked() { return !pageLocked; }
        public String i18n(String text) { return text.equals("guide.feed") ? "本包混合饲料说明" : text; }
    }
}
