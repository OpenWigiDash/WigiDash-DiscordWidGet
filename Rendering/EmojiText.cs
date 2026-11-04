using System.Drawing;
using System.Globalization;
using WigiDash.DiscordWidget.Discord;

namespace WigiDash.DiscordWidget.Rendering;

/// <summary>
/// Texto com emoji (nomes de servidor e canal costumam ter). O GDI+ não desenha emoji colorido nem troca para a
/// fonte de emoji do Windows, então cada emoji vira a imagem do Twemoji, desenhada entre os pedaços de texto.
/// </summary>
internal static class EmojiText
{
    /// <summary>Um pedaço do texto: letras, ou um emoji (com a URL da imagem).</summary>
    internal sealed record Run(string Text, string? EmojiUrl);

    public static bool HasEmoji(string? text) => text is not null && Split(text).Any(r => r.EmojiUrl is not null);

    public static IReadOnlyList<Run> Split(string text)
    {
        var runs = new List<Run>();
        var plain = new System.Text.StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (IsEmoji(element))
            {
                if (plain.Length > 0)
                {
                    runs.Add(new Run(plain.ToString(), null));
                    plain.Clear();
                }
                runs.Add(new Run(element, SoundboardSound.EmojiUrl(element, null)));
            }
            else
            {
                plain.Append(element);
            }
        }
        if (plain.Length > 0)
            runs.Add(new Run(plain.ToString(), null));
        return runs;
    }

    /// <summary>Emoji de figura, bandeira, seletor de emoji (FE0F) ou tecla (1️⃣).</summary>
    internal static bool IsEmoji(string element)
    {
        var first = char.ConvertToUtf32(element, 0);
        if (element.Contains('️') || element.Contains('⃣') || element.Contains('‍'))
            return first > 0x7F || element.Contains('⃣');
        return first is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF
            or 0x203C or 0x2049 or >= 0x2190 and <= 0x21FF or >= 0x2300 and <= 0x23FF;
    }

    /// <summary>
    /// Desenha <paramref name="text"/> numa linha dentro de <paramref name="rect"/>, centralizado ou à esquerda,
    /// com os emojis como imagem. O que não cabe é cortado com reticências.
    /// </summary>
    public static void Draw(Graphics g, string text, Font font, Brush brush, RectangleF rect, StringFormat format)
    {
        var runs = Split(text);
        if (runs.All(r => r.EmojiUrl is null))
        {
            g.DrawString(text, font, brush, rect, format);
            return;
        }

        var measure = StringFormat.GenericTypographic;
        var emojiSize = font.Size * 1.1f;
        var gap = font.Size * 0.12f;
        float Width(Run run) => run.EmojiUrl is not null ? emojiSize + gap * 2 : g.MeasureString(run.Text, font, int.MaxValue, measure).Width;

        // Corta do fim até caber, com "…".
        var shown = runs.ToList();
        var ellipsis = false;
        while (shown.Count > 0 && shown.Sum(Width) + (ellipsis ? g.MeasureString("…", font).Width : 0) > rect.Width)
        {
            ellipsis = true;
            var last = shown[^1];
            shown.RemoveAt(shown.Count - 1);
            if (last.EmojiUrl is null && last.Text.Length > 1)
                shown.Add(last with { Text = last.Text[..^1] });
        }
        if (ellipsis)
            shown.Add(new Run("…", null));

        var total = shown.Sum(Width);
        var x = format.Alignment == StringAlignment.Center ? rect.X + (rect.Width - total) / 2 : rect.X;
        var lineHeight = font.GetHeight(g);
        var top = rect.Y + (rect.Height - lineHeight) / 2;
        foreach (var run in shown)
        {
            var width = Width(run);
            if (run.EmojiUrl is null)
            {
                g.DrawString(run.Text, font, brush, x, top, measure);
            }
            else if (AvatarCache.Get(run.EmojiUrl) is { } image)
            {
                var r = new RectangleF(x + gap, top + (lineHeight - emojiSize) / 2, emojiSize, emojiSize);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                lock (image)
                    g.DrawImage(image, r);
            }
            x += width;
        }
    }
}
