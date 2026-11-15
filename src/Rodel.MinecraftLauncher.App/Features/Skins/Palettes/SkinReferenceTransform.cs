// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Shares the reference image's fit, zoom, pan and original-pixel hit mapping.</summary>
internal readonly record struct SkinReferenceTransform(int ImageWidth, int ImageHeight, float Width, float Height, float Zoom = 1, float PanX = 0, float PanY = 0)
{
    internal float Fit => Math.Min(1, Math.Min(Width / ImageWidth, Height / ImageHeight));
    internal float Scale => Fit * Zoom;
    internal float Left => ((Width - (ImageWidth * Scale)) / 2) + PanX;
    internal float Top => ((Height - (ImageHeight * Scale)) / 2) + PanY;

    internal bool TryPixel(float x, float y, out int pixelX, out int pixelY)
    {
        pixelX = (int)MathF.Floor((x - Left) / Scale);
        pixelY = (int)MathF.Floor((y - Top) / Scale);
        return x >= 0 && x < Width && y >= 0 && y < Height
            && pixelX >= 0 && pixelX < ImageWidth && pixelY >= 0 && pixelY < ImageHeight;
    }

    internal SkinReferenceTransform ZoomAt(float factor, float x, float y)
    {
        var zoom = Math.Clamp(Zoom * factor, .25f, Math.Max(32, 16 / Fit));
        var ratio = zoom / Zoom;
        return (this with
        {
            Zoom = zoom,
            PanX = x - (Width / 2) - ((x - (Width / 2) - PanX) * ratio),
            PanY = y - (Height / 2) - ((y - (Height / 2) - PanY) * ratio),
        }).Clamp();
    }

    internal SkinReferenceTransform Move(float x, float y) => (this with { PanX = PanX + x, PanY = PanY + y }).Clamp();

    internal SkinReferenceTransform Reset() => this with { Zoom = 1, PanX = 0, PanY = 0 };

    private SkinReferenceTransform Clamp()
    {
        var horizontal = Math.Max(0, ((ImageWidth * Scale) - Width) / 2);
        var vertical = Math.Max(0, ((ImageHeight * Scale) - Height) / 2);
        return this with { PanX = Math.Clamp(PanX, -horizontal, horizontal), PanY = Math.Clamp(PanY, -vertical, vertical) };
    }
}