// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the cape preview library across independent reads of real skin project files.</summary>
[TestClass]
public sealed class CapePreviewLibraryTests
{
    /// <summary>
    /// Per-project pairing tests cover preview output. This catalog can start empty alongside ordinary character
    /// projects and must discover both explicitly imported companions and older cape references after reopening.
    /// </summary>
    [TestMethod]
    public Task LibraryContainsOnlyImportedOrPreviouslyPairedCharacters() =>
        ReportStorage.RunAsync(async paths =>
        {
            var cancellation = TestContext.CancellationToken;
            var service = new SkinService(paths);
            var png = Path.Combine(paths.Root, "character.png");
            await File.WriteAllBytesAsync(png, SkinPng.Encode(SkinEditor.Create(SkinModel.Classic)), cancellation);
            var input = await SkinService.PrepareImportAsync(png, cancellationToken: cancellation);
            var regular = await service.CreateImportedAsync(input, SkinModel.Classic, "Regular", cancellation);
            var cape = await service.CreateAsync(SkinModel.Cape, name: "Cape", cancellationToken: cancellation);
            Assert.IsEmpty(service.ListCapePreviewSkins());

            var shared = await service.CreateCapePreviewSkinAsync(input, SkinModel.Classic, "Shared", cancellation);
            var legacy = await service.CreateAsync(SkinModel.Slim, name: "Previously paired", cancellationToken: cancellation);
            await service.SetCompanionAsync(cape, 0, legacy, cancellation);

            var reopened = new SkinService(paths);
            CollectionAssert.AreEquivalent(new[] { shared.Id, legacy }, reopened.ListCapePreviewSkins().Select(work => work.DocumentId).ToArray());
            await Assert.ThrowsAsync<ArgumentException>(() => reopened.SetCompanionAsync(regular.Id, regular.Revision, cape, cancellation));
        });

    /// <summary>Gets the test runner deadline for the isolated project writes.</summary>
    public TestContext TestContext { get; set; } = null!;
}