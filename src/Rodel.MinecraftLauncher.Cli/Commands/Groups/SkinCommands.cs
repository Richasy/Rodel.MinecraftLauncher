// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class SkinCommands
{
    internal Option<string?> Companion { get; } = new("--with") { Description = S.SkinCompanionDescription };
    internal Option<bool> Clear { get; } = new("--clear") { Description = S.SkinOutfitDescription };
    internal Option<bool> Solo { get; } = new("--solo") { Description = S.SkinSoloDescription };
    internal Command Outfit { get; }

    internal Option<SkinModel> Model { get; } = new("--model") { Description = S.SkinModelDescription };
    internal Option<string?> Input { get; } = new("--input") { Description = S.SkinInputDescription };
    internal Option<string> Output { get; } = new("--output") { Description = S.SkinOutputDescription, Required = true };
    internal Option<string?> Color { get; } = new("--color") { Description = S.SkinColorDescription };
    internal Option<long> Revision { get; } = new("--revision") { Description = S.SkinRevisionDescription, Required = true };
    internal Option<string?> Edits { get; } = new("--edits") { Description = S.SkinEditsDescription };
    internal Option<string?> EditsFile { get; } = new("--edits-file") { Description = S.SkinEditsFileDescription };
    internal Option<SkinPart> Part { get; } = new("--part") { Description = S.SkinPartDescription, Required = true };
    internal Option<SkinLayer> Layer { get; } = new("--layer") { Description = S.SkinLayerDescription, Required = true };
    internal Option<SkinFace> Face { get; } = new("--face") { Description = S.SkinFaceDescription, Required = true };
    internal Option<float> Yaw { get; } = new("--yaw") { Description = S.SkinYawDescription, DefaultValueFactory = _ => 25 };
    internal Option<float> Pitch { get; } = new("--pitch") { Description = S.SkinPitchDescription, DefaultValueFactory = _ => 10 };
    internal Option<string?> View { get; } = new("--view") { Description = S.SkinViewDescription };
    internal Option<string> Request { get; } = new("--request") { Description = S.SkinRequestDescription, Required = true };
    internal Option<string?> PaintLayer { get; } = new("--paint-layer") { Description = S.SkinPaintLayerDescription };
    internal Option<bool> AsLayer { get; } = new("--as-layer") { Description = S.SkinImportAsLayerDescription };
    internal Option<int> Size { get; } = new("--size") { Description = S.SkinSizeDescription, DefaultValueFactory = _ => 64 };
    internal Option<string?> Template { get; } = new("--template") { Description = S.SkinTemplateDescription };
    internal Option<string> Name { get; } = new("--name") { Description = S.SkinNameDescription, DefaultValueFactory = _ => "Untitled" };
    internal Option<int> Index { get; } = new("--index") { Description = S.SkinIndexDescription, Required = true };
    internal Command List { get; }
    internal Command Templates { get; }
    internal Command Clone { get; }
    internal Command Rename { get; }
    internal Command SaveAs { get; }
    internal Command History { get; }
    internal Command RestoreTo { get; }
    internal Command Import { get; }
    internal Command Footprint { get; }
    internal Command PreviewEdits { get; }
    internal SkinReviewCommands Review { get; }
    internal Command Create { get; }
    internal Command Inspect { get; }
    internal Command ReadFace { get; }
    internal Command Edit { get; }
    internal Command Undo { get; }
    internal Command Redo { get; }
    internal Command Export { get; }
    internal Command Preview { get; }
    internal Command Pick { get; }
    internal Command Select { get; }
    internal Command Capture { get; }

    internal SkinCommands(Command root)
    {
        var group = Group(root, "skin", S.SkinGroupDescription);
        Outfit = Add(group, "outfit", S.SkinOutfitDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = result.GetRequiredValue(CliArguments.Target);
            await service.SetCompanionAsync(id, result.GetValue(Revision), result.GetValue(Clear) ? null : result.GetValue(Companion), ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id), SkinsJsonContext.Default.SkinSummary);
        }, [CliArguments.Target], [Revision, Companion, Clear]);
        Outfit.Validators.Add(result => { if ((result.GetValue(Companion) is null) == !result.GetValue(Clear)) result.AddError(S.SkinOutfitRequired); });
        Create = Add(group, "create", S.SkinCreateDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = await service.CreateAsync(result.GetValue(Model), result.GetValue(Input), result.GetValue(Color) ?? "#808080", result.GetValue(Size), result.GetValue(Template), result.GetValue(Name)!, ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id), SkinsJsonContext.Default.SkinSummary);
        }, options: [Model, Input, Color, Size, Template, Name]);
        Create.Validators.Add(result =>
        {
            if (result.GetValue(Input) is not null && result.GetValue(Template) is not null)
                result.AddError(S.SkinTemplateInputConflict);
            if (result.GetResult(Size) is { Implicit: false } && !SkinLayout.SupportedSizes.Contains(result.GetValue(Size)))
                result.AddError(S.SkinSizeError);
        });
        List = Add(group, "list", S.SkinListDescription, (app, _, _) =>
            Task.FromResult(JsonSerializer.SerializeToNode(new SkinService(app.Paths).List(), SkinsJsonContext.Default.SkinProjectInfoArray)));
        Templates = Add(group, "templates", S.SkinTemplatesDescription, (_, _, _) =>
            Task.FromResult<JsonNode?>(new JsonArray([.. SkinTemplates.Ids.Select(id => (JsonNode)new JsonObject
                { ["templateId"] = id, ["suggestedModel"] = SkinTemplates.SuggestedModel(id).ToString() })])));
        Clone = Add(group, "clone", S.SkinCloneDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = await service.CloneAsync(result.GetRequiredValue(CliArguments.Target), result.GetValue(Name)!, ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id), SkinsJsonContext.Default.SkinSummary);
        }, [CliArguments.Target], [Name]);
        Rename = Add(group, "rename", S.SkinRenameDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = result.GetRequiredValue(CliArguments.Target);
            await service.RenameAsync(id, result.GetValue(Name)!, ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id), SkinsJsonContext.Default.SkinSummary);
        }, [CliArguments.Target], [Name]);
        SaveAs = Add(group, "save-as", S.SkinSaveAsDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var output = result.GetRequiredValue(Output);
            await service.SaveProjectAsAsync(result.GetRequiredValue(CliArguments.Target), result.GetValue(Revision),
                output, result.GetValue(CliOptions.Overwrite), ct).ConfigureAwait(false);
            return new JsonObject { ["path"] = Path.GetFullPath(output), ["kind"] = "rmcs", ["sourceChanged"] = false };
        }, [CliArguments.Target], [Revision, Output, CliOptions.Overwrite]);
        History = Add(group, "history", S.SkinHistoryDescription, (app, result, _) =>
        {
            var document = new SkinService(app.Paths).Read(result.GetRequiredValue(CliArguments.Target));
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["revision"] = document.Revision,
                ["entries"] = JsonSerializer.SerializeToNode(SkinEditor.History(document), SkinJsonContext.Default.SkinHistoryEntryArray)
            });
        }, [CliArguments.Target]);
        RestoreTo = Add(group, "restore", S.SkinRestoreToDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = result.GetRequiredValue(CliArguments.Target);
            var document = await service.RestoreToAsync(id, result.GetValue(Revision), result.GetValue(Index), ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id, document), SkinsJsonContext.Default.SkinSummary);
        }, [CliArguments.Target], [Revision, Index]);
        Import = Add(group, "import", S.SkinImportDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = result.GetRequiredValue(CliArguments.Target);
            var input = result.GetValue(Input) ?? throw new ArgumentException(S.SkinInputRequiredError);
            var document = result.GetValue(AsLayer)
                ? await service.ImportLayerAsync(id, result.GetValue(Revision), input,
                    result.GetResult(Name) is { Implicit: false } ? result.GetValue(Name) : null, ct).ConfigureAwait(false)
                : await service.ImportAsync(id, result.GetValue(Revision), input, result.GetValue(Model), ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id, document), SkinsJsonContext.Default.SkinSummary);
        }, [CliArguments.Target], [Revision, Input, Model, AsLayer, Name]);
        Import.Validators.Add(result =>
        {
            if ((result.GetValue(AsLayer) && result.GetResult(Model) is { Implicit: false })
                || (!result.GetValue(AsLayer) && result.GetResult(Name) is { Implicit: false }))
            {
                result.AddError(S.SkinImportLayerOptionsError);
            }
        });
        Footprint = Add(group, "footprint", S.SkinFootprintDescription, (app, result, ct) =>
        {
            var document = new SkinService(app.Paths).Read(result.GetRequiredValue(CliArguments.Target));
            var edit = ParseRequest(result.GetRequiredValue(Request), SkinJsonContext.Default.SkinEdit);
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["revision"] = document.Revision,
                ["marks"] = JsonSerializer.SerializeToNode(SkinEditor.BrushFootprint(document, edit, ct), SkinJsonContext.Default.SkinBrushMarkArray)
            });
        }, [CliArguments.Target], [Request]);
        PreviewEdits = Add(group, "preview-edits", S.SkinPreviewEditsDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var document = service.PreviewEdits(result.GetRequiredValue(CliArguments.Target), result.GetValue(Revision),
                ParseRequest(result.GetValue(Edits) ?? throw new ArgumentException(S.SkinEditsRequiredError), SkinJsonContext.Default.SkinEditArray), ct);
            await SkinService.PublishPreviewAsync(document, result.GetRequiredValue(Output), result.GetValue(CliOptions.Overwrite), ct).ConfigureAwait(false);
            return new JsonObject { ["path"] = Path.GetFullPath(result.GetRequiredValue(Output)), ["kind"] = "png", ["saved"] = false, ["revision"] = document.Revision };
        }, [CliArguments.Target], [Revision, Edits, Output, CliOptions.Overwrite]);
        Review = new(group, this);
        Inspect = Add(group, "inspect", S.SkinInspectDescription, (app, result, _) =>
            Task.FromResult(JsonSerializer.SerializeToNode(new SkinService(app.Paths).Describe(result.GetRequiredValue(CliArguments.Target)), SkinsJsonContext.Default.SkinSummary)), [CliArguments.Target]);
        ReadFace = Add(group, "read-face", S.SkinReadDescription, (app, result, _) =>
        {
            var document = new SkinService(app.Paths).Read(result.GetRequiredValue(CliArguments.Target));
            var surface = new SkinSurface { Part = result.GetValue(Part), Layer = result.GetValue(Layer), Face = result.GetValue(Face) };
            var block = SkinEditor.Capture(document, surface, paintLayerId: result.GetValue(PaintLayer));
            var rows = new JsonArray();
            for (var y = 0; y < block.Height; y++)
            {
                var row = new JsonArray();
                for (var x = 0; x < block.Width; x++)
                    row.Add((JsonNode?)JsonValue.Create($"#{block.Pixels[(y * block.Width) + x]:X8}"));
                rows.Add((JsonNode)row);
            }

            return Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = document.Revision, ["width"] = block.Width, ["height"] = block.Height, ["rows"] = rows });
        }, [CliArguments.Target], [Part, Layer, Face, PaintLayer]);
        Edit = Add(group, "edit", S.SkinEditDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var text = result.GetValue(EditsFile) is { } file ? await File.ReadAllTextAsync(file, ct).ConfigureAwait(false)
                : result.GetValue(Edits) ?? throw new ArgumentException(S.SkinEditsRequiredError);
            var id = result.GetRequiredValue(CliArguments.Target);
            var document = await service.EditAsync(id, result.GetValue(Revision), ParseRequest(text, SkinJsonContext.Default.SkinEditArray), ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id, document), SkinsJsonContext.Default.SkinSummary);
        }, [CliArguments.Target], [Revision, Edits, EditsFile]);
        Edit.Validators.Add(result =>
        {
            if ((result.GetResult(Edits) is null) == (result.GetResult(EditsFile) is null))
                result.AddError(S.SkinEditsRequiredError);
        });
        Undo = Add(group, "undo", S.SkinUndoDescription, (app, result, ct) => RestoreAsync(app, result, false, ct), [CliArguments.Target], [Revision]);
        Redo = Add(group, "redo", S.SkinRedoDescription, (app, result, ct) => RestoreAsync(app, result, true, ct), [CliArguments.Target], [Revision]);
        Export = Add(group, "export", S.SkinExportDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths);
            var id = result.GetRequiredValue(CliArguments.Target);
            if (result.GetValue(PaintLayer) is { } layerId)
                await service.ExportLayerAsync(id, layerId, result.GetRequiredValue(Output), result.GetValue(CliOptions.Overwrite), ct).ConfigureAwait(false);
            else
                await service.ExportAsync(id, result.GetRequiredValue(Output), result.GetValue(CliOptions.Overwrite), ct).ConfigureAwait(false);
            return new JsonObject { ["path"] = Path.GetFullPath(result.GetRequiredValue(Output)), ["accountSkinChanged"] = false };
        }, [CliArguments.Target], [Output, PaintLayer, CliOptions.Overwrite]);
        Preview = Add(group, "preview", S.SkinPreviewDescription, async (app, result, ct) =>
        {
            var path = Path.GetFullPath(result.GetRequiredValue(Output));
            var view = result.GetValue(View) is { } json ? ParseRequest(json, SkinJsonContext.Default.SkinViewOptions)
                : new SkinViewOptions(result.GetValue(Yaw), result.GetValue(Pitch));
            await new SkinService(app.Paths).PreviewAsync(result.GetRequiredValue(CliArguments.Target), path, view, result.GetValue(CliOptions.Overwrite), result.GetValue(Solo), ct).ConfigureAwait(false);
            return new JsonObject { ["path"] = path, ["kind"] = "png" };
        }, [CliArguments.Target], [Output, Yaw, Pitch, View, Solo, CliOptions.Overwrite]);
        Preview.Validators.Add(result =>
        {
            if (result.GetResult(View) is not null
                && (result.GetResult(Yaw) is { Implicit: false } || result.GetResult(Pitch) is { Implicit: false }))
            {
                result.AddError(S.SkinViewConflictError);
            }
        });
        Pick = Add(group, "pick", S.SkinPickDescription, (app, result, _) =>
        {
            var document = new SkinService(app.Paths).Read(result.GetRequiredValue(CliArguments.Target));
            var request = ParseRequest(result.GetRequiredValue(Request), SkinJsonContext.Default.SkinPickRequest);
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["revision"] = document.Revision,
                ["hit"] = JsonSerializer.SerializeToNode(SkinGeometry.Pick(document, request), SkinJsonContext.Default.SkinHit)
            });
        }, [CliArguments.Target], [Request]);
        Select = Add(group, "select", S.SkinSelectionDescription, (app, result, _) =>
        {
            var document = new SkinService(app.Paths).Read(result.GetRequiredValue(CliArguments.Target));
            var request = ParseRequest(result.GetRequiredValue(Request), SkinJsonContext.Default.SkinSelectionRequest);
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["revision"] = document.Revision,
                ["selection"] = JsonSerializer.SerializeToNode(SkinSelections.Create(document, request), SkinJsonContext.Default.SkinSelection)
            });
        }, [CliArguments.Target], [Request]);
        Capture = Add(group, "capture", S.SkinCaptureDescription, (app, result, _) =>
        {
            var document = new SkinService(app.Paths).Read(result.GetRequiredValue(CliArguments.Target));
            var request = ParseRequest(result.GetRequiredValue(Request), SkinJsonContext.Default.SkinCaptureRequest);
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["revision"] = document.Revision,
                ["clipboard"] = JsonSerializer.SerializeToNode(SkinEditor.Capture(document, request.Surface, request.Selection, request.PaintLayerId), SkinJsonContext.Default.SkinPixelBlock)
            });
        }, [CliArguments.Target], [Request]);
    }

    internal static T ParseRequest<T>(string json, JsonTypeInfo<T> metadata) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, metadata) ?? throw new ArgumentException(S.SkinRequestRequiredError);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(string.Format(S.SkinRequestError, exception.Message), exception);
        }
    }

    private async Task<JsonNode?> RestoreAsync(LauncherApplication app, ParseResult result, bool redo, CancellationToken cancellationToken)
    {
        var service = new SkinService(app.Paths);
        var id = result.GetRequiredValue(CliArguments.Target);
        var document = await service.RestoreAsync(id, result.GetValue(Revision), redo, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(service.Describe(id, document), SkinsJsonContext.Default.SkinSummary)!.AsObject();
    }
}