// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects default identity selection across real registration, explicit clearing and removal.</summary>
[TestClass]
public sealed class AccountSelectionTests
{
    /// <summary>
    /// The first registered account must be immediately usable without a separate select command. Additional registrations
    /// must preserve both an existing choice and an explicitly cleared choice; removing every account resets this rule.
    /// Existing appearance and external-owner tests do not establish this lifecycle. Real Core storage and an independent
    /// selection read detect either missing first selection or an implementation that selects every newly added account.
    /// </summary>
    [TestMethod]
    public async Task Registration_SelectsFirst_PreservesLaterChoicesAndResetsAfterRemoval()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var service = new IdentityService(paths);
            var first = await service.AddOfflineAsync("FirstPlayer", TestContext.CancellationToken);
            Assert.AreEqual(first.Id, service.GetSelection().AccountId);

            var second = await service.AddOfflineAsync("SecondPlayer", TestContext.CancellationToken);
            Assert.AreEqual(first.Id, service.GetSelection().AccountId);
            await service.SelectAsync(null, TestContext.CancellationToken);
            var third = await service.AddOfflineAsync("ThirdPlayer", TestContext.CancellationToken);
            Assert.IsNull(service.GetSelection().AccountId);

            foreach (var account in new[] { first, second, third }) await service.RemoveAsync(account.Id!, TestContext.CancellationToken);
            var next = await service.AddOfflineAsync("NewFirstPlayer", TestContext.CancellationToken);
            Assert.AreEqual(next.Id, service.GetSelection().AccountId);
        });
    }

    public TestContext TestContext { get; set; }
}