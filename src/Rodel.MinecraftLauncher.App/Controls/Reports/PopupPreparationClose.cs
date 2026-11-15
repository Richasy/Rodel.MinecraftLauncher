// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Controls.Reports;

/// <summary>Closes a completed operation before follow-up refreshes, while retaining active or failed work.</summary>
internal sealed class PopupPreparationClose(Action close, Func<bool> active, Func<bool> canClose, Func<bool> cancellationFailed) : IDisposable
{
    private bool _pending;
    private bool _succeeded;

    /// <summary>Whether closure was requested or the popup was disposed; late notifications must not rebuild its content.</summary>
    internal bool IsClosing { get; private set; }

    /// <summary>Closes an idle popup or asks its owner to request cancellation once.</summary>
    internal bool Request()
    {
        if (IsClosing || _pending)
            return false;
        if (canClose())
        {
            Close();
            return false;
        }

        if (!active())
            return false;
        _pending = true;
        return true;
    }

    /// <summary>Closes once on successful completion or pending dismissal; cancellation acknowledgment alone is insufficient.</summary>
    /// <param name="succeeded">Whether the submitted operation succeeded, independently of subsequent list refreshes.</param>
    /// <returns>Whether this notification requested closure.</returns>
    internal bool Observe(bool succeeded = false)
    {
        if (IsClosing)
            return false;
        _succeeded |= succeeded;
        if (!_pending && !_succeeded)
            return false;
        if (canClose())
        {
            Close();
            return true;
        }
        else if (active() && cancellationFailed())
        {
            _pending = false;
        }

        return false;
    }

    private void Close()
    {
        _pending = false;
        IsClosing = true;
        close();
    }

    /// <summary>Prevents late task notifications from closing a disposed popup.</summary>
    public void Dispose() => IsClosing = true;
}