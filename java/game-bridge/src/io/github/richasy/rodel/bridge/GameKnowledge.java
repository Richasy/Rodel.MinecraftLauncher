package io.github.richasy.rodel.bridge;

import java.lang.reflect.*;
import java.util.*;
import java.util.stream.Stream;

/** Reads loaded recipe/quest/book models. Optional integrations use audited public APIs, never pack scripts. */
final class GameKnowledge {
    private final Bindings b;
    GameKnowledge(Bindings bindings) { b = bindings; }

    Object recipes(GameAccess.World world, Map<String, Object> request) throws Exception {
        List<Map<String, Object>> results = new ArrayList<>();
        Object registries = b.call(world.level(), "levelRegistryAccess");
        boolean modern = b.namedApi();
        Object context = modern ? b.call(null, "slotContext", world.level()) : null;
        SortedMap<String, Object> loaded = new TreeMap<>();
        for (Object holder : (Collection<?>) b.call(b.call(world.server(), "serverRecipes"), "recipesCollection")) {
            Object recipe = !modern && b.has("recipeId") ? holder : b.call(holder, "recipeHolderValue");
            Object nativeId = !modern && b.has("recipeId") ? b.call(recipe, "recipeId") : b.call(holder, "recipeHolderId");
            String id = modern ? b.call(nativeId, "keyLocation").toString() : nativeId.toString();
            loaded.put(id, recipe);
        }
        int total = 0, unresolved = 0;
        int offset = (int) GamePlay.integer(request, "offset", 0), limit = (int) GamePlay.integer(request, "limit", 50);
        for (Map.Entry<String, Object> entry : loaded.entrySet()) {
            String id = entry.getKey(); Object recipe = entry.getValue();
            if (!prefix(request, id)) continue;
            int position = -1;
            try {
                List<Object> outputs = new ArrayList<>(), stations = new ArrayList<>();
                if (modern) {
                    for (Object display : (Iterable<?>) b.call(recipe, "recipeDisplays")) {
                        for (Object output : (Iterable<?>) b.call(b.call(display, "displayResult"), "slotStacks", context)) outputs.add(stack(output));
                        for (Object station : (Iterable<?>) b.call(b.call(display, "displayStation"), "slotStacks", context)) stations.add(stack(station));
                    }
                } else {
                    outputs.add(stack(b.call(recipe, "recipeResult", registries)));
                    if (b.has("recipeStation")) stations.add(stack(b.call(recipe, "recipeStation")));
                }
                if (outputs.isEmpty()) unresolved++;
                Object type = b.call(recipe, "recipeType");
                String typeId = b.has("registryRecipeType") ? b.call(b.field(null, "registryRecipeType"), "registryKey", type).toString() : type.toString();
                if (!matches(request, id + " " + typeId + " " + outputs + " " + stations)) continue;
                position = total++;
                if (position < offset || results.size() >= limit) continue;
                List<Object> ingredients = new ArrayList<>();
                Object nativeIngredients = modern ? b.call(b.call(recipe, "recipePlacement"), "placementIngredients") : b.call(recipe, "recipeIngredients");
                for (Object ingredient : (Iterable<?>) nativeIngredients) {
                    List<?> choices = modern ? (List<?>) b.call(b.call(ingredient, "ingredientDisplay"), "slotStacks", context)
                        : Arrays.asList((Object[]) b.call(ingredient, "ingredientItems"));
                    List<Object> items = new ArrayList<>();
                    for (int i = 0; i < Math.min(choices.size(), 64); i++) items.add(stack(choices.get(i)));
                    ingredients.add(Json.object("alternatives", items, "totalAlternatives", choices.size(), "truncated", choices.size() > 64));
                }
                Map<String, Object> result = Json.object("id", id, "type", typeId, "outputs", outputs,
                    "output", outputs.size() == 1 ? outputs.get(0) : null, "workstations", stations,
                    "ingredients", ingredients, "source", "loaded_recipe_manager", "conditionsComplete", false);
                if (b.has("recipeWidth") && b.has("recipeHeight") && b.officialType("net.minecraft.world.item.crafting.ShapedRecipe").isInstance(recipe)) {
                    int width = ((Number)b.call(recipe, "recipeWidth")).intValue(), height = ((Number)b.call(recipe, "recipeHeight")).intValue();
                    List<Object> slots = new ArrayList<>();
                    if (modern) for (Object index : (Iterable<?>)b.call(b.call(recipe, "recipePlacement"), "placementSlots")) slots.add(index);
                    else for (int i = 0; i < ingredients.size(); i++) slots.add(((List<?>)((Map<?, ?>)ingredients.get(i)).get("alternatives")).isEmpty() ? -1 : i);
                    result.put("shape", Json.object("width", width, "height", height, "ingredientIndexes", slots, "complete", slots.size() == width * height));
                }
                // Farmer's Delight's public recipe properties are shared by every pack using this recipe type.
                if (recipe.getClass().getName().equals("vectorwing.farmersdelight.common.crafting.CookingPotRecipe")) {
                    try {
                        result.put("cookingTimeTicks", invoke(recipe, "getCookTime"));
                        result.put("container", stack(invoke(recipe, "getOutputContainer")));
                    } catch (ReflectiveOperationException error) { result.put("additionalConditionsAvailable", false); }
                } else if (!modern && b.has("cookingTime") && b.officialType("net.minecraft.world.item.crafting.AbstractCookingRecipe").isInstance(recipe))
                    result.put("cookingTimeTicks", b.call(recipe, "cookingTime"));
                results.add(result);
            } catch (ReflectiveOperationException | RuntimeException error) {
                unresolved++;
                if (position < 0 && matches(request, id)) position = total++;
                if (position >= offset && results.size() < limit) results.add(Json.object("id", id, "detailsAvailable", false, "reason", "recipe_api_unavailable"));
            }
        }
        Map<String, Object> page = window(results, Math.min(offset, total), total, limit, "minecraft");
        page.put("unresolvedRecipes", unresolved);
        return page;
    }

    Object quests(GameAccess.World world, Map<String, Object> request) throws Exception {
        Class<?> fileType = optional(world.local() ? "dev.ftb.mods.ftbquests.quest.ServerQuestFile" : "dev.ftb.mods.ftbquests.client.ClientQuestFile");
        if (fileType == null) return unavailable("ftbquests", "api_not_available");
        try {
            Object file = fileType.getField("INSTANCE").get(null);
            if (file == null) return unavailable("ftbquests", "not_loaded");
            Object api = invoke(optional("dev.ftb.mods.ftbteams.api.FTBTeamsAPI"), "api");
            UUID teamId;
            if (world.local()) {
                Object manager = invoke(api, "getManager");
                Optional<?> team = (Optional<?>) invoke(manager, "getTeamForPlayerID", world.playerId());
                if (team.isEmpty()) return unavailable("ftbquests", "player_team_unavailable");
                teamId = (UUID) invoke(team.get(), "getId");
            } else {
                Object manager = invoke(api, "getClientManager");
                Optional<?> player = (Optional<?>) invoke(manager, "getKnownPlayer", world.playerId());
                if (player.isEmpty()) return unavailable("ftbquests", "player_team_unavailable");
                teamId = (UUID) invoke(player.get(), "teamId");
            }
            Object progress = invoke(file, "getNullableTeamData", teamId);
            if (progress == null) return unavailable("ftbquests", "team_progress_unavailable");
            List<Map<String, Object>> results = new ArrayList<>();
            for (Object chapter : (Iterable<?>) invoke(file, "getAllChapters")) {
                for (Object quest : (Iterable<?>) invoke(chapter, "getQuests")) {
                    if (!(Boolean) invoke(quest, "isVisible", progress)) continue;
                    String id = invoke(quest, "getCodeString").toString();
                    if (!prefix(request, id)) continue;
                    String title = text(invoke(quest, "getTitle"));
                    boolean complete = (Boolean) invoke(progress, "isCompleted", quest);
                    boolean startable = (Boolean) invoke(progress, "canStartTasks", quest);
                    boolean hidden = (!startable && (Boolean) invoke(quest, "hideDetailsUntilStartable"))
                        || (!complete && (Boolean) invoke(invoke(quest, "getHideTextUntilComplete"), "get", false));
                    List<String> description = new ArrayList<>();
                    if (!hidden) for (Object line : (Iterable<?>) invoke(quest, "getDescription")) description.add(text(line));
                    List<String> dependencies;
                    try (Stream<?> stream = (Stream<?>) invoke(quest, "streamDependencies")) {
                        dependencies = stream.map(value -> {
                            try { return invoke(value, "getCodeString").toString(); }
                            catch (ReflectiveOperationException error) { throw new IllegalStateException(error); }
                        }).toList();
                    }
                    List<Object> tasks = new ArrayList<>();
                    int totalTasks = hidden ? 0 : ((Collection<?>) invoke(quest, "getTasks")).size();
                    if (!hidden) for (Object task : (Iterable<?>) invoke(quest, "getTasks")) {
                        if (tasks.size() == 64) break;
                        Map<String, Object> detail = Json.object("id", invoke(task, "getCodeString"), "title", text(invoke(task, "getTitle")),
                            "progress", invoke(progress, "getProgress", task).toString(), "maximum", invoke(task, "getMaxProgress").toString());
                        try {
                            detail.put("type", invoke(invoke(task, "getType"), "getTypeId").toString());
                            Object definition = b.officialType("net.minecraft.nbt.CompoundTag").getConstructor().newInstance();
                            invoke(task, "writeData", definition);
                            String snbt = definition.toString();
                            if (snbt.length() <= 8192) detail.put("definitionSnbt", snbt);
                            else detail.put("definitionUnavailable", "task_definition_too_large");
                        } catch (ReflectiveOperationException error) { detail.put("definitionUnavailable", "task_definition_api_unavailable"); }
                        tasks.add(detail);
                    }
                    if (!matches(request, id + " " + title + " " + description + " " + tasks)) continue;
                    results.add(Json.object("id", id, "chapterId", invoke(chapter, "getCodeString"), "chapter", text(invoke(chapter, "getTitle")),
                        "title", title, "description", description, "detailsHidden", hidden, "completed", complete,
                        "startable", startable, "dependencies", dependencies, "tasks", tasks, "totalTasks", totalTasks,
                        "tasksTruncated", totalTasks > tasks.size(), "teamId", teamId.toString()));
                }
            }
            results.sort(Comparator.comparing(value -> value.get("id").toString()));
            return page(results, request, "ftbquests");
        } catch (ReflectiveOperationException | RuntimeException error) {
            b.report("ftbquests_api", error);
            return unavailable("ftbquests", "unsupported_api");
        }
    }

    Object books(Map<String, Object> request) throws Exception {
        Class<?> registryType = optional("vazkii.patchouli.common.book.BookRegistry");
        if (registryType == null) return unavailable("patchouli", "api_not_available");
        try {
            Object registry = registryType.getField("INSTANCE").get(null);
            Map<?, ?> books = (Map<?, ?>) registryType.getField("books").get(registry);
            List<Map<String, Object>> results = new ArrayList<>();
            for (Map.Entry<?, ?> bookEntry : books.entrySet()) {
                Object book = bookEntry.getValue();
                Object contents = invoke(book, "getContents");
                Map<?, ?> entries = (Map<?, ?>) contents.getClass().getField("entries").get(contents);
                for (Map.Entry<?, ?> entry : entries.entrySet()) {
                    Object value = entry.getValue();
                    if ((Boolean) invoke(value, "isLocked")) continue;
                    String id = bookEntry.getKey() + "/" + entry.getKey();
                    if (!prefix(request, id)) continue;
                    List<Object> pages = new ArrayList<>();
                    int unlockedPages = 0;
                    for (Object bookPage : (Iterable<?>) invoke(value, "getPages")) {
                        if (!(Boolean) invoke(bookPage, "isPageUnlocked")) continue;
                        unlockedPages++;
                        if (pages.size() == 64) continue;
                        Object source = bookPage.getClass().getField("sourceObject").get(bookPage);
                        if (source == null) continue;
                        String json = source.toString();
                        if (json.length() > 8192) { pages.add(Json.object("available", false, "reason", "page_too_large")); continue; }
                        Map<String, Object> data = Json.parse(json);
                        for (String key : List.of("title", "text", "header")) if (data.get(key) instanceof String raw)
                            data.put(key, invoke(bookPage, "i18n", raw));
                        pages.add(data);
                    }
                    String title = text(invoke(value, "getName"));
                    if (matches(request, title + " " + id + " " + pages)) results.add(Json.object("id", id,
                        "bookId", bookEntry.getKey().toString(), "title", title, "pages", pages, "pagesTruncated", unlockedPages > pages.size(),
                        "source", "loaded_patchouli_book"));
                }
            }
            results.sort(Comparator.comparing(value -> value.get("id").toString()));
            return page(results, request, "patchouli");
        } catch (ReflectiveOperationException | RuntimeException error) {
            b.report("patchouli_api", error);
            return unavailable("patchouli", "unsupported_api");
        }
    }

    private Map<String, Object> stack(Object value) throws Exception {
        return Json.object("itemId", b.call(b.field(null, "registryItem"), "registryKey", b.call(value, "stackItem")).toString(),
            "count", b.call(value, "stackCount"), "name", text(b.call(value, "stackName")));
    }
    private String text(Object component) throws Exception { return b.call(component, "componentText").toString(); }
    private Class<?> optional(String name) throws Exception {
        try { return b.type(name); } catch (ClassNotFoundException absent) { return null; }
    }
    // Only constant API names above reach this helper. Caller input cannot select Java members.
    private static Object invoke(Object target, String name, Object... args) throws ReflectiveOperationException {
        if (target == null) throw new ClassNotFoundException("Optional API unavailable");
        Class<?> type = target instanceof Class<?> staticType ? staticType : target.getClass();
        for (Method method : type.getMethods()) {
            if (!method.getName().equals(name) || method.getParameterCount() != args.length) continue;
            Class<?>[] parameters = method.getParameterTypes();
            boolean match = true;
            for (int i = 0; i < args.length; i++) if (args[i] != null && !parameters[i].isInstance(args[i])
                && !(parameters[i] == boolean.class && args[i] instanceof Boolean)) { match = false; break; }
            if (match) return method.invoke(target instanceof Class<?> ? null : target, args);
        }
        throw new NoSuchMethodException(type.getName() + "." + name);
    }
    private static boolean prefix(Map<String, Object> request, String id) {
        return request.get("resource") == null || id.startsWith(request.get("resource").toString());
    }
    private static boolean matches(Map<String, Object> request, String text) {
        return request.get("search") == null || text.toLowerCase(Locale.ROOT).contains(request.get("search").toString().toLowerCase(Locale.ROOT));
    }
    private static Object unavailable(String provider, String reason) { return Json.object("provider", provider, "available", false, "reason", reason); }
    private static Object page(List<Map<String, Object>> values, Map<String, Object> request, String provider) {
        int start = (int) Math.min(GamePlay.integer(request, "offset", 0), values.size());
        return window(values.subList(start, values.size()), start, values.size(), (int) GamePlay.integer(request, "limit", 50), provider);
    }
    private static Map<String, Object> window(List<Map<String, Object>> values, int start, int total, int limit, String provider) {
        int size = 0, index = 0;
        List<Object> items = new ArrayList<>();
        while (index < values.size() && items.size() < limit) {
            Map<String, Object> item = values.get(index);
            int bytes = Json.encode(item).length();
            if (size + bytes > 400_000 && !items.isEmpty()) break;
            if (bytes > 200_000) item = Json.object("id", item.get("id"), "detailsAvailable", false, "reason", "item_too_large");
            size += bytes; items.add(item); index++;
        }
        int end = start + items.size();
        return Json.object("provider", provider, "available", true, "items", items, "total", total, "offset", start,
            "hasMore", end < total, "nextOffset", end < total ? end : null);
    }
}
