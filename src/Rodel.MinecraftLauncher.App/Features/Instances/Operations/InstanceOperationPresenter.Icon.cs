// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Images;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

internal sealed partial class InstanceOperationPresenter
{
    private async Task ShowIconAsync(string id, Action? closed)
    {
        if (Opening || _operations.State.Active || _lifetime.IsCancellationRequested || Controls.ModalPopup.IsOpen(_window) || _popup?.IsOpen == true)
            return;
        Opening = true;
        var cancellation = _lifetime.Token;
        try
        {
            var presets = ImagePresetCatalog.Instances;
            var (instance, bytes, selected) = await Task.Run(() =>
                {
                    var record = _client.Instances.Get(id);
                    var encoded = record.Describe().Icon?["data"]?.ToString();
                    var data = encoded is null ? null : Convert.FromBase64String(encoded);
                    var preset = data is null ? presets[0].Id : presets.FirstOrDefault(p => File.ReadAllBytes(p.Path).AsSpan().SequenceEqual(data))?.Id;
                    return (record, data, preset);
                }, cancellation);
            if (cancellation.IsCancellationRequested || Controls.ModalPopup.IsOpen(_window))
                return;
            var initial = bytes is null ? presets[0].Source : ImageSource.FromBytes(bytes);
            ImageChoicePopup? content = null;
            _popup = new Flyout(close => content = new ImageChoicePopup(
                    _window,
                    instance.Name,
                    false,
                    selected,
                    initial,
                    async (choice, ct) =>
                    {
                        string? temporary = null;
                        try
                        {
                            var path = choice.Preset is { } preset ? presets.First(p => p.Id == preset).Path : null;
                            if (choice.Png is { } png)
                            {
                                temporary = Path.Combine(Path.GetTempPath(), "rodel-icon-" + Guid.NewGuid().ToString("N") + ".png");
                                await File.WriteAllBytesAsync(temporary, png, ct);
                                path = temporary;
                            }

                            await Task.Run(() => _client.Instances.SetIconAsync(id, choice.Preset == presets[0].Id ? null : path, ct), ct);
                            await _home.RefreshAsync();
                        }
                        finally
                        {
                            if (temporary is not null)
                                File.Delete(temporary);
                        }
                    },
                    close))
            {
                Placement = PopupPlacement.Full,
                IsModal = true,
                IsLightDismissEnabled = false
            }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(440f).Padding(0f)).Automation(new()
            {
                Name = string.Format(Resources.InstanceManageAction, Resources.InstanceIcon, instance.Name),
                AutomationId = "instances.icon.popup",
                IsDialog = true
            });
            _popup.Closing += e => e.Cancel = content?.CanClose == false;
            _popup.Closed += () => closed?.Invoke();
            Controls.ModalPopup.TryShow(_window, _popup);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
                Controls.LauncherToast.ShowError(_window, exception.Message);
        }
        finally
        {
            Opening = false;
        }
    }
}