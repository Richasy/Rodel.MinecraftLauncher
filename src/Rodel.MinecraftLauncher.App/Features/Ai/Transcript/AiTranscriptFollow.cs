// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Keeps following a deliberate reading choice, independently of content growth and layout callbacks.</summary>
internal sealed class AiTranscriptFollow
{
    private float _offset, _maximum;
    internal bool Following { get; private set; }

    internal void Set(bool following) => Following = following;

    internal void Observe(float offset, float maximum, bool automatic)
    {
        if (!automatic)
        {
            if (offset < _offset - .5f)
                Following = false;
            else if (offset > _offset + .5f && maximum - offset < .5f && MathF.Abs(maximum - _maximum) < .5f)
                Following = true;
        }

        _offset = offset;
        _maximum = maximum;
    }
}