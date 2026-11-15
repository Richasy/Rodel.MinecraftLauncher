package io.github.richasy.rodel.ui.worlds;

import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.EditBox;
import java.util.Map;

public interface WorldRulesAccess {
    AbstractSelectionList<?> rodel$rules();
    EditBox rodel$ruleSearch();
    boolean rodel$invalidRule(Object entry);
    boolean rodel$hasInvalidRules();
    Map<String, String> rodel$ruleValues();
}
