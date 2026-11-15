using Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;
using Sprout.Graphics;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Checks orbit capture at Sprout's normalized input boundary without injecting desktop input.</summary>
[TestClass]
public sealed class SeedSceneNavigationTests
{
    /// <summary>
    /// Review: primary-only admission, foreign-pointer cancellation and busy-before-release are plausible failures
    /// that leave right-drag unavailable or capture stuck. GPU camera tests do not dispatch normalized pointer events.
    /// Literal movement and terminal capture results protect the player contract independently of the renderer.
    /// </summary>
    [TestMethod]
    [DataRow(PointerButton.Primary, PointerEventKind.Up)]
    [DataRow(PointerButton.Secondary, PointerEventKind.Up)]
    [DataRow(PointerButton.Primary, PointerEventKind.Cancel)]
    [DataRow(PointerButton.Secondary, PointerEventKind.Cancel)]
    public void OrbitGesture_MovesEitherButtonAndReleasesWhileUnavailable(PointerButton button, PointerEventKind terminal)
    {
        var gesture = new SeedSceneOrbitGesture();
        var down = gesture.Handle(Event(PointerEventKind.Down, button, 17, 20f, 20f), true, out _);
        Assert.AreEqual(PointerCaptureRequest.Capture, down.Capture);
        var move = gesture.Handle(Event(PointerEventKind.Move, PointerButton.None, 17, 30f, 15f), true, out var delta);
        Assert.IsTrue(move.Handled);
        Assert.AreEqual(new Point(10f, -5f), delta);
        var otherButton = button == PointerButton.Primary ? PointerButton.Secondary : PointerButton.Primary;
        Assert.IsFalse(gesture.Handle(Event(PointerEventKind.Up, otherButton, 17, 30f, 15f), true, out _).Handled);
        Assert.IsFalse(gesture.Handle(Event(PointerEventKind.Cancel, button, 18, 30f, 15f), false, out _).Handled);
        var release = gesture.Handle(Event(terminal, button, 17, 30f, 15f), false, out _);
        Assert.AreEqual(PointerCaptureRequest.Release, release.Capture, "Loading must not swallow the gesture's terminal event.");
        Assert.IsFalse(gesture.Active);
        Assert.IsFalse(gesture.Handle(Event(PointerEventKind.Move, PointerButton.None, 17, 40f, 20f), true, out _).Handled);
    }

    private static PointerEvent Event(PointerEventKind kind, PointerButton button, uint id, float x, float y) =>
        new(kind, new Point(x, y), button, PointerDeviceType.Mouse) { PointerId = id };
}