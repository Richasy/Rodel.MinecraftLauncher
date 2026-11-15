using Rodel.MinecraftLauncher.App.Features.Skins;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects editable palette ownership and failed preference commits, not native controls or skin document persistence.</summary>
[TestClass]
public sealed class SkinPalettePreferenceTests
{
    /// <summary>
    /// Built-in and user palettes must remain unchanged when their copies are edited.
    /// Existing skin persistence cases do not cover these App preferences. Editing two generations
    /// and independently reopening the serialized value detects shared arrays, dropped alpha or lost selection.
    /// The write callback substitutes only LocalSettings; the packaged UI flow checks that real boundary.
    /// </summary>
    [TestMethod]
    public void Copies_KeepTheirSourcesAndReopenTheSelectedEditablePalette()
    {
        var saved = string.Empty;
        var store = new SkinPaletteStore(string.Empty, value => saved = value);
        var original = store.Current.Colors.ToArray();
        Assert.Throws<InvalidOperationException>(() => store.SetColor(0, "#12345678"));
        store.Create("Warm tones", copy: true);
        var firstId = store.Current.Id;
        store.SetColor(0, "#12345678");
        var firstColors = store.Current.Colors.ToArray();
        store.Create("Warm tones copy", copy: true);
        var secondId = store.Current.Id;
        store.SetColor(0, "#aabbcc80");

        var reopened = new SkinPaletteStore(saved, _ => Assert.Fail("Reading preferences must not write them."));
        Assert.AreNotEqual(firstId, secondId);
        Assert.AreEqual(secondId, reopened.Current.Id);
        Assert.AreEqual("Warm tones copy", reopened.Current.Name);
        Assert.AreEqual("#AABBCC80", reopened.Current.Colors[0]);
        CollectionAssert.AreEqual(original, reopened.Items[0].Colors);
        CollectionAssert.AreEqual(firstColors, reopened.Items.Single(palette => palette.Id == firstId).Colors);
    }

    /// <summary>
    /// New palettes start empty rather than silently copying the active preset; adding and removing
    /// their final color must still leave a usable, persistently selected empty palette.
    /// This distinguishes the new-palette path from copy and exercises the empty-grid lifecycle without a desktop.
    /// </summary>
    [TestMethod]
    public void NewPalette_RemainsSelectedAndUsableAfterItsLastColorIsRemoved()
    {
        var saved = string.Empty;
        var store = new SkinPaletteStore(string.Empty, value => saved = value);
        store.Create("空白色板", copy: false);
        Assert.IsEmpty(store.Current.Colors);
        store.AddColor("#123456");
        Assert.AreEqual("#123456FF", store.Current.Colors.Single());
        store.RemoveColor(0);

        var reopened = new SkinPaletteStore(saved, value => saved = value);
        Assert.AreEqual(store.Current.Id, reopened.Current.Id);
        Assert.AreEqual("空白色板", reopened.Current.Name);
        Assert.IsEmpty(reopened.Current.Colors);
        reopened.AddColor("#11223344");
        Assert.AreEqual("#11223344", new SkinPaletteStore(saved, _ => { }).Current.Colors.Single());
    }

    /// <summary>
    /// Add must create a new editable slot even when its foreground seed already exists.
    /// Otherwise the user sees no change or accidentally edits the original color. Existing empty/copy cases
    /// never use a duplicate seed; checking the preserved prefix and independently reopened slot distinguishes
    /// an appended editable entry from deduplication or replacement, including the built-in copy-and-add path.
    /// </summary>
    [TestMethod]
    public void Add_ExistingForegroundSeed_CreatesAnIndependentPersistedSlot()
    {
        var saved = string.Empty;
        var store = new SkinPaletteStore(string.Empty, value => saved = value);
        store.Create("Editable", copy: true);
        var original = store.Current.Colors.ToArray();
        store.AddColor(original[0]);
        Assert.HasCount(original.Length + 1, store.Current.Colors);
        store.SetColor(original.Length, "#11223344");
        var reopened = new SkinPaletteStore(saved, value => saved = value);
        CollectionAssert.AreEqual(original, reopened.Current.Colors[..original.Length]);
        Assert.AreEqual("#11223344", reopened.Current.Colors[^1]);
        reopened.Select(SkinEditorPalettes.Ids[0]);
        reopened.Create("Copy and add", copy: true, append: original[0]);
        Assert.HasCount(original.Length + 1, reopened.Current.Colors);
        Assert.AreEqual(original[0], reopened.Current.Colors[^1]);
    }

    /// <summary>
    /// A rejected settings write must not publish an in-memory color that was never saved.
    /// The callback fails at the actual commit boundary after serialization, not at construction or validation.
    /// Comparing both retained colors and the saved value detects mutation-before-write; retry must then persist normally.
    /// </summary>
    [TestMethod]
    public void FailedColorCommit_KeepsTheSavedPaletteAndCanBeRetried()
    {
        var saved = string.Empty; var fail = false;
        var store = new SkinPaletteStore(string.Empty, value =>
        {
            if (fail) throw new IOException("Synthetic settings write failure.");
            saved = value;
        });
        store.Create("Editable", copy: true);
        var previous = saved;
        var colors = store.Current.Colors.ToArray();
        fail = true;
        Assert.Throws<IOException>(() => store.SetColor(0, "#12345678"));
        Assert.AreEqual(previous, saved);
        CollectionAssert.AreEqual(colors, store.Current.Colors);
        fail = false;
        store.SetColor(0, "#12345678");
        Assert.AreEqual("#12345678", new SkinPaletteStore(saved, _ => { }).Current.Colors[0]);
    }

    private static readonly string[] ReferencePaletteColors = ["#FE102080", "#2154FFFF"];

    /// <summary>
    /// Explicit preset removal must survive both later custom-palette writes and reopening.
    /// The existing copy tests assume presets remain present; removing every palette catches a
    /// fallback that silently restores forest or cannot represent an empty selection.
    /// </summary>
    [TestMethod]
    public void DeletedPresets_StayDeletedAfterCreationAndAfterDeletingTheLastPalette()
    {
        var saved = string.Empty;
        var store = new SkinPaletteStore(string.Empty, value => saved = value);
        var removed = store.Current.Id;
        store.Delete();
        store.Create("Reference", ["#FE102080", "#2154FF"]);
        var reopened = new SkinPaletteStore(saved, value => saved = value);
        Assert.DoesNotContain(palette => palette.Id == removed, reopened.Items);
        CollectionAssert.AreEqual(ReferencePaletteColors, reopened.Current.Colors);
        while (reopened.HasPalette)
            reopened.Delete();

        reopened = new(saved, value => saved = value);
        Assert.IsEmpty(reopened.Items);
        Assert.IsFalse(reopened.HasPalette);
        reopened.Create("Blank", copy: false);
        var final = new SkinPaletteStore(saved, _ => { });
        Assert.HasCount(1, final.Items);
        Assert.AreEqual("Blank", final.Current.Name);
        Assert.IsEmpty(final.Current.Colors);
    }

    /// <summary>
    /// A failed preset deletion must not publish the hidden identity or move selection.
    /// Failure occurs in the real preference commit callback, after the next value is serialized;
    /// retry and independent reopen distinguish durable removal from transient filtering.
    /// </summary>
    [TestMethod]
    public void FailedPresetDeletion_PreservesSelectionAndCanBeRetried()
    {
        var saved = string.Empty;
        var fail = false;
        var store = new SkinPaletteStore(string.Empty, value =>
        {
            if (fail)
                throw new IOException("Synthetic settings write failure.");
            saved = value;
        });
        store.Select("sunset");
        var previous = saved;
        fail = true;
        Assert.Throws<IOException>(store.Delete);
        Assert.AreEqual(previous, saved);
        Assert.AreEqual("sunset", store.Current.Id);
        Assert.Contains(palette => palette.Id == "sunset", store.Items);
        fail = false;
        store.Delete();
        var reopened = new SkinPaletteStore(saved, _ => { });
        Assert.DoesNotContain(palette => palette.Id == "sunset", reopened.Items);
        Assert.AreEqual(store.Current.Id, reopened.Current.Id);
    }
}