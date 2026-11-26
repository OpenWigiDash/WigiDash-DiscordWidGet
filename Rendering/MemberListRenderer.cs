using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using static WigiDash.DiscordWidget.Rendering.ButtonRenderer;

namespace WigiDash.DiscordWidget.Rendering;

/// <summary>O que a tela cheia do Server Stats mostra: a lista, ou o motivo de não ter lista.</summary>
public sealed record MemberListVisual(string GuildName, string? GuildIconUrl)
{
    /// <summary><c>null</c> enquanto carrega ou quando falhou (<see cref="Message"/> diz o porquê).</summary>
    public GuildWidget? Widget { get; init; }

    /// <summary>Título no meio da tela quando não há lista (carregando, widget desligado, erro).</summary>
    public string? Message { get; init; }

    /// <summary>Texto menor embaixo de <see cref="Message"/> (ex.: onde ligar o widget do servidor).</summary>
    public string? Hint { get; init; }

    public int Page { get; init; }
}

/// <summary>
/// Tela cheia de quem está online num servidor: cabeçalho com o logo, o nome e quantos estão online, e embaixo
/// cartões com avatar, bolinha de status, nome e o canal de voz ou o jogo. Passa de página com swipe.
/// </summary>
public static class MemberListRenderer
{
    public static readonly Color Background = Color.FromArgb(43, 45, 49);
    private static readonly Color Card = Color.FromArgb(56, 58, 64);
    private static readonly Color Header = Color.FromArgb(30, 31, 34);
    private static readonly Color Muted = Color.FromArgb(181, 186, 193);
    public static readonly Color Online = Color.FromArgb(35, 165, 90);
    public static readonly Color Idle = Color.FromArgb(240, 178, 50);
    public static readonly Color DoNotDisturb = Color.FromArgb(242, 63, 67);

    internal const int HeaderHeight = 64;
    internal const int Padding = 12;
    internal const int Gap = 8;
    internal const int CardHeight = 62;
    internal const int MinCardWidth = 220;

    /// <summary>Colunas e linhas de cartões que cabem em <paramref name="size"/> (no 1016x592: 4 x 7).</summary>
    public static (int Columns, int Rows) Layout(PixelSize size)
    {
        var columns = Math.Max(1, (size.Width - 2 * Padding + Gap) / (MinCardWidth + Gap));
        var rows = Math.Max(1, (size.Height - HeaderHeight - 2 * Padding + Gap) / (CardHeight + Gap));
        return (columns, rows);
    }

    /// <summary>Quantas páginas a lista de <paramref name="count"/> pessoas ocupa (pelo menos uma).</summary>
    public static int PageCount(PixelSize size, int count)
    {
        var (columns, rows) = Layout(size);
        var perPage = columns * rows;
        return Math.Max(1, (count + perPage - 1) / perPage);
    }

    public static WidgetFrame Render(PixelSize size, WidgetTheme theme, MemberListVisual visual)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Background);
            var foreground = ToColor(theme.PrimaryForeground);

            var pages = visual.Widget is { } widget ? PageCount(size, widget.Members.Count) : 1;
            var page = Math.Clamp(visual.Page, 0, pages - 1);
            DrawHeader(g, size, theme, visual, foreground, page, pages);

            var body = new RectangleF(Padding, HeaderHeight + Padding, size.Width - 2 * Padding, size.Height - HeaderHeight - 2 * Padding);
            if (visual.Widget is { Members.Count: > 0 } list)
                DrawCards(g, size, theme, body, list.Members, page, foreground);
            else
                DrawMessage(g, theme, body, visual.Widget is null ? visual.Message ?? "Carregando..." : "Ninguém online",
                    visual.Widget is null ? visual.Hint : "Quem está invisível não aparece no widget do servidor.", foreground);
        }
        return ToFrame(bitmap);
    }

    private static void DrawHeader(Graphics g, PixelSize size, WidgetTheme theme, MemberListVisual visual, Color foreground, int page, int pages)
    {
        using (var fill = new SolidBrush(Header))
            g.FillRectangle(fill, 0, 0, size.Width, HeaderHeight);
        DrawLogo(g, new PixelSize(size.Width, HeaderHeight), Blend(Header, DiscordBlurple, 0.25));

        const float icon = 44;
        var iconRect = new RectangleF(Padding + 2, (HeaderHeight - icon) / 2, icon, icon);
        if (AvatarCache.Get(visual.GuildIconUrl) is { } image)
            DrawRoundImage(g, image, iconRect);
        else
        {
            using (var fill = new SolidBrush(DiscordBlurple))
                g.FillEllipse(fill, iconRect);
            using var glyph = new SolidBrush(foreground);
            DrawGlyph(g, Glyphs.Server, new PointF(iconRect.X + icon / 2, iconRect.Y + icon / 2), icon * 0.5f, glyph);
        }

        // Direita: "12 online" com a bolinha verde e, se tiver mais de uma página, "1/3" embaixo.
        using var countFont = new Font(theme.SecondaryFontFamily, 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font(theme.PrimaryFontFamily, 13, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(foreground);
        using var mutedBrush = new SolidBrush(Muted);
        using var right = new StringFormat(Centered) { Alignment = StringAlignment.Far };
        using var left = new StringFormat(Centered) { Alignment = StringAlignment.Near };
        var rightWidth = 0f;
        if (visual.Widget is { } widget)
        {
            var count = $"{widget.PresenceCount:N0} online";
            rightWidth = g.MeasureString(count, countFont).Width;
            var countRect = new RectangleF(size.Width - Padding - rightWidth, 8, rightWidth, 28);
            g.DrawString(count, countFont, textBrush, countRect, right);
            using (var dot = new SolidBrush(Online))
                g.FillEllipse(dot, countRect.X - 14, countRect.Y + 9, 10, 10);
            var sub = pages > 1 ? $"Página {page + 1} de {pages} · deslize para o lado" : "Toque para fechar";
            if (widget.Members.Count < widget.PresenceCount && pages == 1)
                sub = $"{widget.Members.Count:N0} na lista · toque para fechar";
            var subWidth = g.MeasureString(sub, smallFont).Width;
            rightWidth = Math.Max(rightWidth + 16, subWidth);
            g.DrawString(sub, smallFont, mutedBrush, new RectangleF(size.Width - Padding - subWidth, 38, subWidth, 18), right);
        }

        using var nameFont = new Font(theme.SecondaryFontFamily, 22, FontStyle.Bold, GraphicsUnit.Pixel);
        var nameX = iconRect.Right + 12;
        var nameRect = new RectangleF(nameX, 8, Math.Max(10, size.Width - nameX - Padding - rightWidth - 16), 28);
        EmojiText.Draw(g, visual.GuildName, nameFont, textBrush, nameRect, left);
        g.DrawString("Online agora", smallFont, mutedBrush, new RectangleF(nameX, 38, nameRect.Width, 18), left);
    }

    private static void DrawCards(Graphics g, PixelSize size, WidgetTheme theme, RectangleF body, IReadOnlyList<OnlineMember> members,
        int page, Color foreground)
    {
        var (columns, rows) = Layout(size);
        var perPage = columns * rows;
        var cardWidth = (body.Width - Gap * (columns - 1)) / columns;
        using var nameFont = new Font(theme.SecondaryFontFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var detailFont = new Font(theme.PrimaryFontFamily, 13, GraphicsUnit.Pixel);
        using var iconFont = new Font(IconFontName, 12, GraphicsUnit.Pixel);
        using var nameBrush = new SolidBrush(foreground);
        using var detailBrush = new SolidBrush(Muted);
        using var voiceBrush = new SolidBrush(Online);
        using var cardBrush = new SolidBrush(Card);
        using var left = new StringFormat(Centered) { Alignment = StringAlignment.Near };

        var first = page * perPage;
        for (var i = first; i < Math.Min(members.Count, first + perPage); i++)
        {
            var slot = i - first;
            var x = body.X + (cardWidth + Gap) * (slot % columns);
            var y = body.Y + (CardHeight + Gap) * (slot / columns);
            var card = new RectangleF(x, y, cardWidth, CardHeight);
            using (var path = RoundedRect(Rectangle.Round(card), 10))
                g.FillPath(cardBrush, path);

            var member = members[i];
            const float d = 42;
            var avatar = new RectangleF(x + 10, y + (CardHeight - d) / 2, d, d);
            DrawAvatar(g, avatar, new AvatarFace(member.AvatarUrl, member.Name, false));
            DrawStatus(g, avatar, member.Status);

            var textX = avatar.Right + 10;
            var textWidth = card.Right - textX - 8;
            var detail = member.VoiceChannel ?? member.Game;
            if (detail is null)
            {
                EmojiText.Draw(g, member.Name, nameFont, nameBrush, new RectangleF(textX, y, textWidth, CardHeight), left);
                continue;
            }
            EmojiText.Draw(g, member.Name, nameFont, nameBrush, new RectangleF(textX, y + 9, textWidth, 22), left);
            var detailRect = new RectangleF(textX, y + 32, textWidth, 18);
            if (member.VoiceChannel is not null)
            {
                // Em call: alto-falante verde e o nome do canal.
                g.DrawString(Glyphs.Volume, iconFont, voiceBrush, new RectangleF(textX - 1, detailRect.Y, 16, 18), left);
                detailRect = detailRect with { X = textX + 17, Width = textWidth - 17 };
                EmojiText.Draw(g, member.VoiceChannel, detailFont, voiceBrush, detailRect, left);
            }
            else
                EmojiText.Draw(g, $"Jogando {member.Game}", detailFont, detailBrush, detailRect, left);
        }
    }

    /// <summary>Bolinha de status no canto do avatar, com borda da cor do cartão (como no Discord).</summary>
    private static void DrawStatus(Graphics g, RectangleF avatar, MemberStatus status)
    {
        const float d = 14;
        var rect = new RectangleF(avatar.Right - d + 2, avatar.Bottom - d + 2, d, d);
        using (var ring = new SolidBrush(Card))
            g.FillEllipse(ring, RectangleF.Inflate(rect, 3, 3));
        using var fill = new SolidBrush(StatusColor(status));
        g.FillEllipse(fill, rect);
        // Ausente é uma lua e não perturbe um traço, para não depender só da cor.
        using var cut = new SolidBrush(Card);
        if (status == MemberStatus.Idle)
            g.FillEllipse(cut, rect.X - d * 0.12f, rect.Y - d * 0.12f, d * 0.62f, d * 0.62f);
        else if (status == MemberStatus.DoNotDisturb)
            g.FillRectangle(cut, rect.X + d * 0.22f, rect.Y + d * 0.42f, d * 0.56f, d * 0.16f);
    }

    public static Color StatusColor(MemberStatus status) => status switch
    {
        MemberStatus.Idle => Idle,
        MemberStatus.DoNotDisturb => DoNotDisturb,
        _ => Online,
    };

    private static void DrawMessage(Graphics g, WidgetTheme theme, RectangleF body, string title, string? hint, Color foreground)
    {
        using var titleFont = new Font(theme.SecondaryFontFamily, 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var hintFont = new Font(theme.PrimaryFontFamily, 16, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(foreground);
        using var hintBrush = new SolidBrush(Muted);
        var middle = body.Y + body.Height / 2;
        g.DrawString(title, titleFont, titleBrush, new RectangleF(body.X, middle - 40, body.Width, 36), Centered);
        if (hint is not null)
        {
            using var wrap = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
            g.DrawString(hint, hintFont, hintBrush, new RectangleF(body.X + 40, middle + 4, body.Width - 80, 60), wrap);
        }
        g.DrawString("Toque para fechar", hintFont, hintBrush, new RectangleF(body.X, body.Bottom - 24, body.Width, 22), Centered);
    }
}
