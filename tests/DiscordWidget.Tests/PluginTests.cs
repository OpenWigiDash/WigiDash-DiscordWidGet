using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

namespace DiscordWidget.Tests;

public sealed class PluginTests
{
    private static readonly Assembly Plugin = typeof(DiscordWidgetBase).Assembly;

    private static List<IWidget> LoadWidgets() => Plugin.GetCustomAttributes<OpenWigiWidgetAttribute>()
        .Select(a => (IWidget)Activator.CreateInstance(a.WidgetType)!)
        .ToList();

    [Fact]
    public void Todos_os_widgets_declarados_tem_ficha_valida_e_ids_unicos()
    {
        var widgets = LoadWidgets();

        Assert.NotEmpty(widgets);
        Assert.All(widgets, w => Assert.Empty(w.Manifest.Validate()));
        Assert.Equal(widgets.Count, widgets.Select(w => w.Manifest.Id).Distinct().Count());
    }

    [Fact]
    public void Conta_fica_na_pagina_do_plugin()
    {
        var attribute = Plugin.GetCustomAttribute<OpenWigiPluginSettingsAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("DiscordPluginSettings", attribute.ComponentType.Name);
        Assert.True(typeof(PluginSettingsComponent).IsAssignableFrom(attribute.ComponentType));
    }

    [Fact]
    public void Mini_so_nos_botoes_de_icone()
    {
        string[] iconButtons =
        [
            "Discord: Mute", "Discord: Deafen", "Discord: Push to Talk", "Discord: Push to Mute",
            "Discord: Mute inteligente", "Discord: Voice Input Mode", "Discord: Soundboard",
            "Discord: Video Toggle", "Discord: Stream Toggle", "Discord: Voice Channel", "Discord: Text Channel",
            "Discord: Set Audio Device", "Discord: Audio Device Toggle",
        ];
        foreach (var widget in LoadWidgets())
            Assert.Equal(iconButtons.Contains(widget.Manifest.Name), widget.Manifest.SupportedSizes.Contains(WidgetSize.Mini));
    }

    [Fact]
    public async Task Previa_de_cada_widget_desenha_em_todos_os_tamanhos_e_molduras()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(output);

        foreach (var widget in LoadWidgets())
        {
            foreach (var size in widget.Manifest.SupportedSizes)
            {
                // A folga da moldura vai de 0 a 5 px de cada lado: o quadro tem que caber em todas.
                foreach (var inset in TileFrame.InsetOptions)
                {
                    var pixels = new PixelSize(size.CellSpan.Width - 2 * inset, size.CellSpan.Height - 2 * inset);
                    var frame = await widget.GetPreviewAsync(size, pixels, CancellationToken.None);
                    Assert.NotNull(frame);
                    Assert.Equal(pixels, frame.Size);
                    if (inset == TileFrame.Default.Inset)
                        Save(frame, Path.Combine(output, $"{widget.Manifest.Name.Replace(':', '-')}-{size.Columns}x{size.Rows}.png"));
                }
            }
        }
    }

    [Fact]
    public void Estados_desenham_sem_erro()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(output);
        var theme = new WidgetTheme();
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Video, "Câmera desligada") { Slashed = true }),
            Path.Combine(output, "estado-camera-off.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-volume-1x1.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Wide.CellSpan), theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-volume-2x1.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Microphone, "Detecção de voz") { GlyphBadge = Glyphs.Volume, Detail = "Modo de voz" }),
            Path.Combine(output, "estado-modo-voz.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Microphone, "Push to Talk") { Accent = ButtonRenderer.Success, GlyphBadge = Glyphs.Touch, Detail = "Modo de voz" }),
            Path.Combine(output, "estado-modo-ptt.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan), theme, new ButtonVisual(Glyphs.Microphone, "Mute") { GlyphBadge = Glyphs.Touch, Detail = "não aparece no Mini" }),
            Path.Combine(output, "estado-mini.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan), theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-mini-volume.png"));

        // Canal de voz com 8 pessoas: grade de avatares em cada tamanho (nome embaixo só no Large).
        string[] names = ["Ana", "Bruno", "Caio", "Duda", "Enzo", "Fê", "Gabi", "Hugo"];
        var faces = names.Select((n, i) => new AvatarFace(null, n, i == 1)).ToList();
        var logo = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(logo))
        {
            g.Clear(Color.FromArgb(232, 163, 61));
            g.FillEllipse(Brushes.Navy, 16, 16, 32, 32);
        }
        AvatarCache.Put("test://logo", logo);
        foreach (var size in new[] { WidgetSize.Small, WidgetSize.Wide, WidgetSize.Large })
            Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(size.CellSpan), theme,
                    new ButtonVisual(Glyphs.Volume, "Voice 15") { Faces = faces, Badge = faces.Count, BadgeGlyph = Glyphs.Volume, CornerImageUrl = "test://logo", Detail = "Toque para entrar" }),
                Path.Combine(output, $"estado-canal-{size.Columns}x{size.Rows}.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Large.CellSpan), theme,
                new ButtonVisual(Glyphs.Ringer, "3  Ana") { Accent = ButtonRenderer.Success, Detail = "bora jogar?",
                    Lines = [new("Ana", "bora jogar?"), new("Bruno", "chegando em 5 min"), new("Caio", "alguém viu o patch novo?")] }),
            Path.Combine(output, "estado-notificacoes-large.png"));
        var small = TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan);
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.Volume, "Voice 15") { Detail = "Canal vazio", ImageBadgeUrl = "test://logo" }),
            Path.Combine(output, "estado-canal-vazio.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.Headphone, "Outro dispositivo")
                { Accent = ButtonRenderer.Info, Detail = "Toque para ir para A", DotCount = 2, CornerGlyphs = [Glyphs.Microphone, Glyphs.Speakers] }),
            Path.Combine(output, "estado-toggle-fora.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.Headphone, "Headset")
                { Accent = ButtonRenderer.Success, Detail = "A · toque para B", DotCount = 2, DotIndex = 0, CornerGlyphs = [Glyphs.Microphone, Glyphs.Speakers] }),
            Path.Combine(output, "estado-toggle-a.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan), theme, new ButtonVisual(WidgetIcons.Find("headset")!.Glyph, "HeadSet")
                { Accent = ButtonRenderer.Success, DotCount = 2, DotIndex = 0, CornerGlyphs = [Glyphs.Microphone, Glyphs.Speakers] }),
            Path.Combine(output, "estado-toggle-mini.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.Server, "312 online")
                { ImageUrl = "test://logo", GlyphBadge = Glyphs.Server, Detail = "1.204 membros · Jogos" }),
            Path.Combine(output, "estado-server-logo.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.Message, "#geral") { Detail = "Jogos", ImageBadgeUrl = "test://logo" }),
            Path.Combine(output, "estado-texto.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.StreamArrow, "Transmitindo") { OnScreen = true, Accent = ButtonRenderer.Success }),
            Path.Combine(output, "estado-stream-on.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.StreamArrow, "Sem transmissão") { OnScreen = true, Slashed = true }),
            Path.Combine(output, "estado-stream-off.png"));
        Save(ButtonRenderer.Render(small, theme, new ButtonVisual(Glyphs.MicrophoneOff, "Mute") { Accent = ButtonRenderer.Danger, Detail = "Mutado" }),
            Path.Combine(output, "estado-mutado.png"));
    }

    internal static void Save(WidgetFrame frame, string path)
    {
        using var bitmap = new Bitmap(frame.Size.Width, frame.Size.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = frame.Pixels.ToArray();
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(pixels, y * frame.Stride, data.Scan0 + y * data.Stride, Math.Min(frame.Stride, data.Stride));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
