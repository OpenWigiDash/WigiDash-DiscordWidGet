using System.Drawing;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;

namespace DiscordWidget.Tests;

public sealed class EmojiTextTests
{
    [Fact]
    public void Separa_emoji_do_texto_com_a_url_do_Twemoji()
    {
        var runs = EmojiText.Split("🎮 Jogos ⚔️ BR");

        Assert.Equal(["🎮", " Jogos ", "⚔️", " BR"], runs.Select(r => r.Text));
        Assert.EndsWith("/1f3ae.png", runs[0].EmojiUrl);
        Assert.Null(runs[1].EmojiUrl);
        Assert.EndsWith("/2694.png", runs[2].EmojiUrl);
    }

    [Theory]
    [InlineData("Voice 15")]
    [InlineData("#geral")]
    [InlineData("Ação & reação")]
    public void Texto_comum_nao_tem_emoji(string text) => Assert.False(EmojiText.HasEmoji(text));

    [Fact]
    public void Bandeira_e_sequencia_com_ZWJ_sao_um_emoji_so()
    {
        Assert.Single(EmojiText.Split("🇧🇷"));
        Assert.Single(EmojiText.Split("👨‍👩‍👧"));
    }

    [Fact]
    public void Rotulo_com_emoji_desenha_a_imagem()
    {
        var emoji = new Bitmap(72, 72);
        using (var g = Graphics.FromImage(emoji))
            g.Clear(Color.Magenta);
        AvatarCache.Put(EmojiText.Split("🎮")[0].EmojiUrl!, emoji);

        var size = TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan);
        var frame = ButtonRenderer.Render(size, new WidgetTheme(), new ButtonVisual(Glyphs.Volume, "🎮 Jogos"));
        PluginTests.Save(frame, Path.Combine(AppContext.BaseDirectory, "previews", "estado-emoji.png"));

        // Algum pixel magenta na linha do rótulo: o emoji virou imagem.
        var pixels = frame.Pixels.ToArray();
        var found = false;
        for (var y = size.Height / 2; y < size.Height && !found; y++)
            for (var x = 0; x < size.Width && !found; x++)
            {
                var i = y * frame.Stride + x * 4;
                found = pixels[i] == 255 && pixels[i + 1] == 0 && pixels[i + 2] == 255;
            }
        Assert.True(found);
    }
}
