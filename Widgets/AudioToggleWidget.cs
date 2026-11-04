using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Settings;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(AudioToggleWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>Uma das duas combinações do toggle: o microfone e/ou a saída (nomes guardados para desenhar offline).</summary>
public sealed record AudioCombo
{
    public string? InputId { get; init; }
    public string? InputName { get; init; }
    public string? OutputId { get; init; }
    public string? OutputName { get; init; }
}

public sealed record AudioToggleOptions
{
    /// <summary>O que o toggle troca: só a entrada, só a saída ou as duas.</summary>
    public AudioSide Side { get; init; } = AudioSide.Both;
    public AudioCombo A { get; init; } = new();
    public AudioCombo B { get; init; } = new();

    /// <summary>Ícone principal escolhido na biblioteca do SDK (<see cref="WidgetIcons"/>); <c>null</c> = automático.</summary>
    public string? IconKey { get; init; }
}

/// <summary>Alterna o Discord entre duas combinações de dispositivo (SET_VOICE_SETTINGS input/output device_id).</summary>
public sealed class AudioToggleWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("c3f4f0a2-7d1e-4f63-9a59-2b8e6d1c5a47"),
        "Discord: Audio Device Toggle",
        "Cada toque alterna o microfone e/ou a saída do Discord entre duas combinações. Verde quando está numa delas.",
        iconButton: true);

    protected override ButtonVisual Preview => new(Glyphs.Headphone, "Audio Toggle")
    {
        DotCount = 2,
        DotIndex = 0,
        CornerGlyphs = [Glyphs.Microphone, Glyphs.Speakers],
        Accent = ButtonRenderer.Success,
    };

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new AudioToggleInstance(context);
}

public sealed class AudioToggleInstance(IWidgetContext context) : DiscordOptionsInstance<AudioToggleOptions>(context)
{
    protected override string Glyph => WidgetIcons.Find(Options.IconKey)?.Glyph is { Length: > 0 } glyph ? glyph : AutomaticGlyph;
    protected override string Label => "Audio Toggle";

    public override Type? OptionsComponent => typeof(AudioToggleOptionsPanel);

    /// <summary>O ícone que o widget usa sem escolha: microfone, caixa de som ou headset.</summary>
    public string AutomaticGlyph => Options.Side switch
    {
        AudioSide.Input => Glyphs.Microphone,
        AudioSide.Output => Glyphs.Speakers,
        _ => Glyphs.Headphone,
    };

    private bool UsesInput => Options.Side is AudioSide.Input or AudioSide.Both;
    private bool UsesOutput => Options.Side is AudioSide.Output or AudioSide.Both;

    /// <summary>A combinação tem todos os dispositivos que o toggle troca.</summary>
    private bool IsComplete(AudioCombo combo) =>
        (!UsesInput || combo.InputId is not null) && (!UsesOutput || combo.OutputId is not null);

    private bool Matches(AudioCombo combo, VoiceSettings? voice) =>
        IsComplete(combo)
        && (!UsesInput || voice?.Input.DeviceId == combo.InputId)
        && (!UsesOutput || voice?.Output.DeviceId == combo.OutputId);

    /// <summary>0 = A, 1 = B, -1 = nenhuma (trocado por fora).</summary>
    public int CurrentIndex(VoiceSettings? voice) =>
        Matches(Options.A, voice) ? 0 : Matches(Options.B, voice) ? 1 : -1;

    private string? Name(AudioCombo combo) => Options.Side switch
    {
        AudioSide.Input => combo.InputName,
        AudioSide.Output => combo.OutputName,
        // As duas: o nome da saída costuma dizer mais (headset, caixas de som).
        _ => combo.OutputName ?? combo.InputName,
    };

    private IReadOnlyList<string> CornerGlyphs => Options.Side switch
    {
        AudioSide.Input => [Glyphs.Microphone],
        AudioSide.Output => [Glyphs.Speakers],
        _ => [Glyphs.Microphone, Glyphs.Speakers],
    };

    protected override ButtonVisual GetVisual()
    {
        if (!IsComplete(Options.A) || !IsComplete(Options.B))
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha as duas combinações", Dimmed = true, CornerGlyphs = CornerGlyphs };

        var index = CurrentIndex(Session.Voice);
        if (index < 0)
        {
            // Alguém trocou o dispositivo por fora: azul, sem bolinha marcada; o toque vai para A.
            return new ButtonVisual(Glyph, "Outro dispositivo")
            {
                Accent = ButtonRenderer.Info,
                Detail = "Toque para ir para A",
                DotCount = 2,
                DotIndex = -1,
                CornerGlyphs = CornerGlyphs,
            };
        }
        var combo = index == 0 ? Options.A : Options.B;
        return new ButtonVisual(Glyph, Name(combo) ?? (index == 0 ? "A" : "B"))
        {
            Accent = ButtonRenderer.Success,
            Detail = index == 0 ? "A · toque para B" : "B · toque para A",
            DotCount = 2,
            DotIndex = index,
            CornerGlyphs = CornerGlyphs,
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || !IsComplete(Options.A) || !IsComplete(Options.B))
            return;
        var target = CurrentIndex(Session.Voice) == 0 ? Options.B : Options.A;
        var args = new JsonObject();
        if (UsesInput)
            args["input"] = new JsonObject { ["device_id"] = target.InputId };
        if (UsesOutput)
            args["output"] = new JsonObject { ["device_id"] = target.OutputId };
        await ConfirmAsync(() => Session.SetVoiceSettingsAsync(args, cancellationToken)).ConfigureAwait(false);
    }
}
