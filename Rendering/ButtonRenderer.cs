using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;

namespace WigiDash.DiscordWidget.Rendering;

/// <summary>
/// O que um botão mostra. <see cref="Glyph"/> é um caractere da fonte de ícones do Windows
/// (Segoe Fluent Icons, ou Segoe MDL2 Assets no Windows 10): os ícones são do sistema, não da Elgato.
/// </summary>
public sealed record ButtonVisual(string Glyph, string Label)
{
    /// <summary>Cor de fundo do estado (verde ativo, vermelho mutado). <c>null</c> = azul do Discord.</summary>
    public Color? Accent { get; init; }

    /// <summary>Segunda linha, menor (ex.: nome do canal, volume).</summary>
    public string? Detail { get; init; }

    /// <summary>Desenha apagado (Discord desconectado, falta configurar).</summary>
    public bool Dimmed { get; init; }

    /// <summary>Barra de nível no rodapé (volume), de 0 a 1. <c>null</c> = sem barra.</summary>
    public double? Level { get; init; }

    /// <summary>Avatares no lugar do ícone (quem está no canal), sobrepostos da esquerda para a direita.</summary>
    public IReadOnlyList<AvatarFace>? Faces { get; init; }

    /// <summary>Contador no canto superior direito (ex.: pessoas no canal).</summary>
    public int? Badge { get; init; }

    /// <summary>Ícone do contador; <c>null</c> = pessoas.</summary>
    public string? BadgeGlyph { get; init; }

    /// <summary>Imagem redonda pequena no canto superior esquerdo (ex.: logo do servidor junto dos avatares).</summary>
    public string? CornerImageUrl { get; init; }

    /// <summary>Ícone pequeno no canto inferior direito do principal (ex.: a mão do Push to Talk).</summary>
    public string? GlyphBadge { get; init; }

    /// <summary>Risco diagonal sobre o ícone (ex.: câmera desligada = a mesma câmera, cortada).</summary>
    public bool Slashed { get; init; }

    /// <summary>"−" e "+" nas bordas, mostrando onde tocar para abaixar e aumentar.</summary>
    public AdjustHints Hints { get; init; }

    /// <summary>Imagem no lugar do ícone (ex.: emoji do som), baixada pelo <see cref="AvatarCache"/>.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Imagem pequena e redonda no canto do ícone (ex.: logo do servidor no canal de voz).</summary>
    public string? ImageBadgeUrl { get; init; }

    /// <summary>Lista (ex.: últimas notificações), desenhada no lugar do ícone quando o tile é alto (Large).</summary>
    public IReadOnlyList<ListLine>? Lines { get; init; }

    /// <summary>
    /// Dois ícones lado a lado com <see cref="Glyph"/> pequeno entre eles (ex.: headset ⇄ caixa de som), no lugar
    /// do ícone único.
    /// </summary>
    public (string Left, string Right)? GlyphPair { get; init; }

    /// <summary>Glifo de destaque de um ícone combinado da biblioteca do SDK (<see cref="WidgetIcon.Accent"/>).</summary>
    public string? GlyphAccent { get; init; }

    /// <summary>Bolinhas no topo, uma por opção (como o Audio Device do plugin System). <c>null</c> = sem bolinhas.</summary>
    public int? DotCount { get; init; }

    /// <summary>Bolinha cheia; -1 = nenhuma.</summary>
    public int DotIndex { get; init; } = -1;

    /// <summary>Ícones pequenos numa pílula no canto superior direito (ex.: o que o botão troca: microfone, saída).</summary>
    public IReadOnlyList<string>? CornerGlyphs { get; init; }
}

/// <summary>Uma linha da lista: título em destaque e texto menor embaixo.</summary>
public sealed record ListLine(string Title, string? Body);

/// <summary>Onde o tile desenha o "−" e o "+" de ajuste (volume).</summary>
public enum AdjustHints
{
    None,
    /// <summary>"−" à esquerda e "+" à direita do ícone.</summary>
    Horizontal,
}

/// <summary>Um avatar: imagem do CDN (ou a inicial do nome enquanto não chega) e o anel de quem está falando.</summary>
public sealed record AvatarFace(string? Url, string Name, bool Speaking);

/// <summary>
/// Desenha um <see cref="ButtonVisual"/> na área de conteúdo do widget (SDK 3.0: o host pinta a moldura e o canto
/// arredondado; aqui o quadro inteiro é preenchido). Os tamanhos vão do Mini (98x70 menos a moldura) ao tela cheia.
/// </summary>
public static class ButtonRenderer
{
    public static readonly Color DiscordBlurple = Color.FromArgb(88, 101, 242);

    /// <summary>Fundo de ocioso: o azul do Discord.</summary>
    public static readonly Color Idle = DiscordBlurple;

    /// <summary>Azul de "fora das opções" (ex.: dispositivo trocado por fora do toggle).</summary>
    public static readonly Color Info = Color.FromArgb(0, 120, 212);
    public static readonly Color Danger = Color.FromArgb(218, 55, 60);
    public static readonly Color Success = Color.FromArgb(35, 165, 90);

    private static readonly string IconFont = PickIconFont();

    public static WidgetFrame Render(PixelSize size, WidgetTheme theme, ButtonVisual visual)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // Fundo do estado: azul do Discord de ocioso, verde quando a ação está ativa e vermelho quando algo
            // está cortado (mutado, ensurdecido), vindos em Accent. Por cima, o símbolo do Discord num tom mais
            // escuro da mesma cor, como nas imagens da Elgato.
            var background = visual.Accent ?? Idle;
            var logo = Blend(background, Color.Black, 0.16);
            var foreground = ToColor(theme.PrimaryForeground);
            if (visual.Dimmed)
            {
                background = Blend(background, Color.Black, 0.5);
                logo = Blend(logo, Color.Black, 0.5);
                foreground = Blend(foreground, background, 0.5);
            }
            g.Clear(background);
            DrawLogo(g, size, logo);

            // Mini (meia célula): só ícone e rótulo curto, sem a segunda linha.
            var compact = size.Height < 100;
            var hasDetail = !compact && !string.IsNullOrEmpty(visual.Detail);
            var labelHeight = compact ? Math.Max(12, size.Height / 5) : Math.Clamp(size.Height / 7, 16, 24);
            // No Mini, a barra de nível precisa de espaço próprio embaixo do rótulo.
            var textBlock = labelHeight + (hasDetail ? labelHeight * 4 / 5 : 0) + (compact ? (visual.Level is null ? 4 : 9) : 8);
            // Com as bolinhas do carrossel em cima, o ícone começa embaixo delas.
            var top = visual.DotCount is null ? (compact ? 3 : 6) : (compact ? 12 : 17);
            var iconArea = new RectangleF(0, top, size.Width, size.Height - textBlock - top);

            // Tile alto (Large) tem espaço para o nome embaixo de cada avatar e para a lista.
            var tall = size.Height >= 220;
            if (visual.Lines is { Count: > 0 } lines && tall)
                DrawLines(g, theme, new RectangleF(12, top + 4, size.Width - 24, size.Height - textBlock - top - 8), lines, background, foreground);
            else if (visual.Faces is { Count: > 0 } faces)
            {
                // O contador fica no canto de cima: a grade começa embaixo dele.
                var reserved = visual.Badge is null ? 0 : BadgeHeight(size) + 4;
                var facesArea = new RectangleF(6, iconArea.Y + reserved, size.Width - 12, iconArea.Height - reserved);
                DrawFaces(g, facesArea, faces, background, foreground, tall);
            }
            else if (AvatarCache.Get(visual.ImageUrl) is { } image)
            {
                var side = Math.Min(iconArea.Height * 0.8f, size.Width * 0.45f);
                var rect = new RectangleF(iconArea.X + (iconArea.Width - side) / 2, iconArea.Y + (iconArea.Height - side) / 2, side, side);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (visual.Dimmed)
                {
                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.4f });
                    lock (image)
                        g.DrawImage(image, Rectangle.Round(rect), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
                else
                {
                    lock (image)
                        g.DrawImage(image, rect);
                }
                if (visual.GlyphBadge is { } imageBadge)
                    DrawGlyphBadge(g, iconArea, side, imageBadge, background, foreground);
            }
            else
            {
                // No Mini o ícone é o tile: ocupa quase toda a altura livre.
                var glyphSize = compact ? Math.Min(iconArea.Height * 1.05f, size.Width * 0.45f) : Math.Min(iconArea.Height * 0.62f, size.Width * 0.4f);
                using var iconFont = new Font(IconFont, glyphSize, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(foreground);
                if (visual.GlyphPair is var (leftGlyph, rightGlyph))
                {
                    // Os dois ícones um pouco menores, afastados do centro, e o do meio (as setas) bem menor.
                    // No Mini, menores para não encostar no selo do canto.
                    var pairSize = glyphSize * (compact ? 0.5f : 0.8f);
                    var offset = pairSize * 0.85f;
                    var center = new PointF(iconArea.X + iconArea.Width / 2, iconArea.Y + iconArea.Height / 2);
                    DrawGlyph(g, leftGlyph, center with { X = center.X - offset }, pairSize * GlyphFill, brush);
                    DrawGlyph(g, rightGlyph, center with { X = center.X + offset }, pairSize * GlyphFill, brush);
                    DrawGlyph(g, visual.Glyph, center, pairSize * 0.5f * GlyphFill, brush);
                }
                else
                {
                    var box = new IconRect(iconArea.X, iconArea.Y, iconArea.Width, iconArea.Height);
                    var place = WidgetIconLayout.Place(visual.GlyphAccent is not null, box, glyphSize * GlyphFill);
                    DrawGlyph(g, visual.Glyph, new PointF(place.Main.CenterX, place.Main.CenterY), place.MainSize, brush);
                    if (visual.GlyphAccent is { } accent && place.AccentCircle is { } circle)
                    {
                        using (var cut = new SolidBrush(background))
                            g.FillEllipse(cut, circle.X, circle.Y, circle.Width, circle.Height);
                        DrawGlyph(g, accent, new PointF(circle.CenterX, circle.CenterY), place.AccentSize, brush);
                    }
                }
                if (visual.Slashed)
                    DrawSlash(g, iconArea, glyphSize, background, foreground);
                if (visual.GlyphBadge is { } badgeGlyph)
                    DrawGlyphBadge(g, iconArea, glyphSize, badgeGlyph, background, foreground);
                else if (AvatarCache.Get(visual.ImageBadgeUrl) is { } badgeImage)
                    DrawImageBadge(g, iconArea, glyphSize, badgeImage, background);
            }

            if (visual.Hints != AdjustHints.None)
                DrawHints(g, size, iconArea, background, foreground);

            if (visual.Badge is { } badge)
                DrawBadge(g, size, theme, badge, visual.BadgeGlyph ?? "\uE77B", background, foreground);
            else if (visual.CornerGlyphs is { Count: > 0 } corner)
                DrawCornerGlyphs(g, size, corner, background, foreground);

            if (AvatarCache.Get(visual.CornerImageUrl) is { } cornerImage)
                DrawCornerImage(g, size, cornerImage);

            if (visual.DotCount is { } dots)
                DrawDots(g, size, dots, visual.DotIndex, foreground);

            var y = size.Height - textBlock;
            using (var labelFont = new Font(theme.SecondaryFontFamily, labelHeight * 0.8f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(foreground))
                EmojiText.Draw(g, visual.Label, labelFont, brush, new RectangleF(4, y, size.Width - 8, labelHeight + 2), Centered);

            if (hasDetail)
            {
                using var detailFont = new Font(theme.PrimaryFontFamily, labelHeight * 0.65f, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Blend(foreground, background, 0.25));
                EmojiText.Draw(g, visual.Detail!, detailFont, brush,
                    new RectangleF(4, y + labelHeight, size.Width - 8, labelHeight), Centered);
            }

            if (visual.Level is { } level)
            {
                // Longe dos cantos: o host corta o canto arredondado do conteúdo.
                const int inset = 10;
                var track = new RectangleF(inset, size.Height - 6, size.Width - inset * 2, compact ? 3 : 4);
                using var trackBrush = new SolidBrush(Blend(background, foreground, 0.2));
                using var fillBrush = new SolidBrush(foreground);
                g.FillRectangle(trackBrush, track);
                g.FillRectangle(fillBrush, track with { Width = (float)(track.Width * Math.Clamp(level, 0, 1)) });
            }
        }
        return ToFrame(bitmap);
    }

    /// <summary>
    /// Avatares em grade, no maior tamanho em que todo mundo cabe (cada um com pelo menos 28 px). Se não cabe,
    /// o último lugar vira "+N". Com <paramref name="names"/>, sobra uma linha para o nome embaixo de cada um.
    /// </summary>
    internal static FaceGrid LayoutFaces(SizeF area, int count, bool names)
    {
        const float minD = 28;
        var label = names ? 18f : 0;
        FaceGrid? best = null;
        for (var rows = 1; rows <= 6; rows++)
        {
            for (var cols = 1; cols <= 10; cols++)
            {
                var d = Math.Min(Math.Min(area.Width * 0.96f / (1 + Step(names) * (cols - 1)), names ? (area.Height / rows - label) * 0.86f : area.Height * 0.96f / (1 + RowStep * (rows - 1))), 96);
                if (d < minD)
                    continue;
                var grid = new FaceGrid(rows, cols, d);
                if (best is null || Better(grid, best, count))
                    best = grid;
            }
        }
        return best ?? new FaceGrid(1, 1, Math.Max(8, Math.Min(area.Width, area.Height - label) * 0.84f));
    }

    /// <summary>
    /// Distância entre os centros, em diâmetros: sobrepostos um pouco (como no plugin da Elgato, mas menos); com
    /// nome embaixo, lado a lado para o nome caber.
    /// </summary>
    private static float Step(bool names) => names ? 1.2f : 0.82f;

    /// <summary>Distância entre fileiras sem nome, em diâmetros: as fileiras também se sobrepõem um pouco.</summary>
    private const float RowStep = 0.84f;

    /// <summary>Cabe todo mundo vence; entre os que cabem, avatar maior e menos lugares sobrando; senão, mais lugares.</summary>
    private static bool Better(FaceGrid grid, FaceGrid best, int count)
    {
        bool fits = grid.Slots >= count, bestFits = best.Slots >= count;
        if (fits != bestFits)
            return fits;
        if (fits)
            return grid.Diameter > best.Diameter || (grid.Diameter == best.Diameter && grid.Slots < best.Slots);
        return grid.Slots > best.Slots || (grid.Slots == best.Slots && grid.Diameter > best.Diameter);
    }

    internal sealed record FaceGrid(int Rows, int Columns, float Diameter)
    {
        public int Slots => Rows * Columns;
    }

    private static void DrawFaces(Graphics g, RectangleF area, IReadOnlyList<AvatarFace> faces, Color background, Color foreground, bool names)
    {
        var grid = LayoutFaces(area.Size, faces.Count, names);
        var slots = Math.Min(faces.Count, grid.Slots);
        var extra = faces.Count - slots;
        var counter = extra > 0 ? slots - 1 : -1;
        var usedRows = (slots + grid.Columns - 1) / grid.Columns;
        var label = names ? 18f : 0;
        var d = grid.Diameter;
        var step = d * Step(names);
        var rowStep = names ? Math.Min(area.Height / grid.Rows, d * 1.12f + label) : d * RowStep;
        var totalHeight = names ? rowStep * usedRows : d + rowStep * (usedRows - 1);
        var y0 = area.Y + (area.Height - totalHeight) / 2;
        var ring = Math.Max(2.5f, d / 18);
        using var nameFont = names ? new Font("Segoe UI", 12.5f, GraphicsUnit.Pixel) : null;
        using var nameBrush = new SolidBrush(Blend(foreground, background, 0.15));

        for (var i = 0; i < slots; i++)
        {
            var row = i / grid.Columns;
            var inRow = Math.Min(grid.Columns, slots - row * grid.Columns);
            var rowWidth = d + step * (inRow - 1);
            var cx = area.X + (area.Width - rowWidth) / 2 + d / 2 + step * (i % grid.Columns);
            var cy = names ? y0 + rowStep * row + (rowStep - label) / 2 : y0 + d / 2 + rowStep * row;
            var rect = new RectangleF(cx - d / 2, cy - d / 2, d, d);

            // Borda da cor do fundo separa cada avatar do que ele cobre.
            using (var border = new SolidBrush(background))
                g.FillEllipse(border, RectangleF.Inflate(rect, ring, ring));

            if (i == counter)
            {
                using (var fill = new SolidBrush(Color.FromArgb(150, Blend(background, Color.Black, 0.6))))
                    g.FillEllipse(fill, rect);
                using var font = new Font("Segoe UI Semibold", d * 0.36f, GraphicsUnit.Pixel);
                g.DrawString($"+{extra + 1}", font, Brushes.White, rect, Centered);
                continue;
            }

            if (faces[i].Speaking)
            {
                // Anel de quem fala; no fundo verde (você na call) o verde sumiria, então vai branco.
                var speaking = background.ToArgb() == Success.ToArgb() ? Color.White : Success;
                using var glow = new SolidBrush(speaking);
                g.FillEllipse(glow, RectangleF.Inflate(rect, ring, ring));
            }
            DrawAvatar(g, rect, faces[i]);
            if (nameFont is not null)
                EmojiText.Draw(g, faces[i].Name, nameFont, nameBrush, new RectangleF(cx - step / 2 + 1, rect.Bottom + 3, step - 2, label), Centered);
        }
    }

    /// <summary>Lista de títulos e textos, de cima para baixo, até onde couber.</summary>
    private static void DrawLines(Graphics g, WidgetTheme theme, RectangleF area, IReadOnlyList<ListLine> lines, Color background, Color foreground)
    {
        using var titleFont = new Font(theme.SecondaryFontFamily, 15, GraphicsUnit.Pixel);
        using var bodyFont = new Font(theme.PrimaryFontFamily, 13, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(foreground);
        using var bodyBrush = new SolidBrush(Blend(foreground, background, 0.3));
        using var divider = new Pen(Blend(background, foreground, 0.2));
        using var left = new StringFormat(Centered) { Alignment = StringAlignment.Near };
        const float lineHeight = 40;
        var y = area.Y;
        foreach (var line in lines)
        {
            if (y + lineHeight > area.Bottom + 1)
                break;
            if (y > area.Y)
                g.DrawLine(divider, area.X, y - 3, area.Right, y - 3);
            EmojiText.Draw(g, line.Title, titleFont, titleBrush, new RectangleF(area.X, y, area.Width, 19), left);
            if (!string.IsNullOrEmpty(line.Body))
                EmojiText.Draw(g, line.Body, bodyFont, bodyBrush, new RectangleF(area.X, y + 19, area.Width, 17), left);
            y += lineHeight + 6;
        }
    }

    /// <summary>Imagem redonda (logo do servidor) no canto inferior direito do ícone, com borda da cor do fundo.</summary>
    private static void DrawImageBadge(Graphics g, RectangleF area, float glyphSize, Image image, Color background)
    {
        var d = glyphSize * 0.5f;
        var cx = area.X + area.Width / 2 + glyphSize * 0.5f;
        var cy = area.Y + area.Height / 2 + glyphSize * 0.36f;
        var circle = new RectangleF(cx - d / 2, cy - d / 2, d, d);
        using (var fill = new SolidBrush(background))
            g.FillEllipse(fill, RectangleF.Inflate(circle, 3, 3));
        using var clip = new GraphicsPath();
        clip.AddEllipse(circle);
        var state = g.Save();
        g.SetClip(clip);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        lock (image)
            g.DrawImage(image, circle);
        g.Restore(state);
    }

    private static void DrawAvatar(Graphics g, RectangleF rect, AvatarFace face)
    {
        using var clip = new GraphicsPath();
        clip.AddEllipse(rect);
        var image = AvatarCache.Get(face.Url);
        if (image is not null)
        {
            var state = g.Save();
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            lock (image)
                g.DrawImage(image, rect);
            g.Restore(state);
            return;
        }
        // Enquanto o avatar não chega: a inicial sobre a cor da pessoa (estável pelo nome).
        var hue = (uint)face.Name.GetHashCode() % 6;
        Color[] palette = [Color.FromArgb(155, 132, 238), Success, Danger, Color.FromArgb(240, 178, 50), Color.FromArgb(235, 69, 158), Color.FromArgb(87, 158, 230)];
        using (var fill = new SolidBrush(palette[hue]))
            g.FillEllipse(fill, rect);
        using var font = new Font("Segoe UI Semibold", rect.Height * 0.45f, GraphicsUnit.Pixel);
        var initial = face.Name.Length > 0 ? char.ToUpperInvariant(face.Name[0]).ToString() : "?";
        g.DrawString(initial, font, Brushes.White, rect, Centered);
    }

    /// <summary>Ícone pequeno num círculo da cor do fundo, encostado no canto inferior direito do principal.</summary>
    private static void DrawGlyphBadge(Graphics g, RectangleF area, float glyphSize, string glyph, Color background, Color foreground)
    {
        var d = glyphSize * 0.62f;
        var cx = area.X + area.Width / 2 + glyphSize * 0.55f;
        var cy = area.Y + area.Height / 2 + glyphSize * 0.36f;
        var circle = new RectangleF(cx - d / 2, cy - d / 2, d, d);
        using (var fill = new SolidBrush(background))
            g.FillEllipse(fill, RectangleF.Inflate(circle, 2, 2));
        using var font = new Font(IconFont, d * 0.8f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(foreground);
        g.DrawString(glyph, font, brush, circle, Centered);
    }

    /// <summary>Risco do canto superior esquerdo ao inferior direito, com contorno da cor do fundo.</summary>
    private static void DrawSlash(Graphics g, RectangleF area, float glyphSize, Color background, Color foreground)
    {
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        var half = glyphSize * 0.55f;
        var width = Math.Max(3f, glyphSize / 12);
        using (var gap = new Pen(background, width * 3) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(gap, cx - half, cy - half, cx + half, cy + half);
        using var line = new Pen(foreground, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, cx - half, cy - half, cx + half, cy + half);
    }

    /// <summary>
    /// Botões redondos de "−" (esquerda) e "+" (direita), na altura do ícone: mostram que a metade esquerda
    /// do tile abaixa e a direita aumenta.
    /// </summary>
    private static void DrawHints(Graphics g, PixelSize size, RectangleF iconArea, Color background, Color foreground)
    {
        var d = Math.Clamp(iconArea.Height * 0.5f, 16f, 44f);
        var y = iconArea.Y + (iconArea.Height - d) / 2;
        var margin = Math.Max(8f, size.Width * 0.05f);
        DrawRoundButton(g, new RectangleF(margin, y, d, d), "−", background, foreground);
        DrawRoundButton(g, new RectangleF(size.Width - margin - d, y, d, d), "+", background, foreground);
    }

    private static void DrawRoundButton(Graphics g, RectangleF rect, string symbol, Color background, Color foreground)
    {
        using (var fill = new SolidBrush(Color.FromArgb(70, foreground)))
            g.FillEllipse(fill, rect);
        using (var border = new Pen(Color.FromArgb(200, foreground), Math.Max(2f, rect.Width / 16)))
            g.DrawEllipse(border, rect);
        using var font = new Font("Segoe UI Semibold", rect.Height * 0.72f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(foreground);
        // O "−" e o "+" da fonte ficam um pouco abaixo do centro; sobe 6% para centralizar no círculo.
        g.DrawString(symbol, font, brush, rect with { Y = rect.Y - rect.Height * 0.06f }, Centered);
    }

    private static readonly Lazy<Bitmap> LogoMask = new(LoadLogoMask);

    /// <summary>
    /// Símbolo do Discord (branco sobre preto, 512 px) como máscara: o brilho vira transparência, para pintar
    /// em qualquer cor.
    /// </summary>
    private static Bitmap LoadLogoMask()
    {
        using var stream = typeof(ButtonRenderer).Assembly.GetManifestResourceStream("WigiDash.DiscordWidget.Rendering.discord-logo-mask.png")!;
        using var source = new Bitmap(stream);
        var mask = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                mask.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).R, 255, 255, 255));
        return mask;
    }

    /// <summary>
    /// Símbolo grande no canto inferior direito, saindo do tile (variante B escolhida pelo Victor), na cor
    /// <paramref name="color"/>.
    /// </summary>
    private static void DrawLogo(Graphics g, PixelSize size, Color color)
    {
        var side = size.Height * 1.25f;
        var rect = new RectangleF(size.Width - side * 0.64f, size.Height - side * 0.7f, side, side);
        var mask = LogoMask.Value;
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(
        [
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 1, 0],
            [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1],
        ]));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        lock (mask)
            g.DrawImage(mask, Rectangle.Round(rect), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, attributes);
    }

    /// <summary>Pílula com ícones pequenos no canto superior direito, no lugar do contador.</summary>
    private static void DrawCornerGlyphs(Graphics g, PixelSize size, IReadOnlyList<string> glyphs, Color background, Color foreground)
    {
        var height = size.Height < 100 ? 16f : BadgeHeight(size);
        var cell = height * 0.85f;
        var width = cell * glyphs.Count + height * 0.3f;
        var inset = size.Height < 100 ? 5f : 8f;
        var rect = new RectangleF(size.Width - width - inset, inset, width, height);
        using (var path = RoundedRect(Rectangle.Round(rect), (int)(height / 2)))
        using (var fill = new SolidBrush(Color.FromArgb(200, Blend(background, Color.Black, 0.55))))
            g.FillPath(fill, path);
        using var font = new Font(IconFont, height * 0.55f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(foreground);
        for (var i = 0; i < glyphs.Count; i++)
            g.DrawString(glyphs[i], font, brush, new RectangleF(rect.X + height * 0.15f + cell * i, rect.Y, cell, height), Centered);
    }

    /// <summary>Bolinhas centralizadas no topo: a cheia é a opção ativa.</summary>
    private static void DrawDots(Graphics g, PixelSize size, int count, int index, Color foreground)
    {
        var d = size.Height < 100 ? 6f : 8f;
        var gap = d * 0.9f;
        var total = d * count + gap * (count - 1);
        var x = (size.Width - total) / 2;
        var y = size.Height < 100 ? 4f : 7f;
        using var fill = new SolidBrush(foreground);
        using var pen = new Pen(foreground, 1.5f);
        for (var i = 0; i < count; i++)
        {
            var rect = new RectangleF(x + (d + gap) * i, y, d, d);
            if (i == index)
                g.FillEllipse(fill, rect);
            else
                g.DrawEllipse(pen, rect);
        }
    }

    private static float BadgeHeight(PixelSize size) => Math.Clamp(size.Height / 6.5f, 20f, 26f);

    /// <summary>Pílula com o contador no canto superior direito, como o "2" de pessoas da Elgato.</summary>
    /// <summary>Imagem redonda do tamanho do contador, no canto superior esquerdo.</summary>
    private static void DrawCornerImage(Graphics g, PixelSize size, Image image)
    {
        var d = size.Height < 100 ? 16f : BadgeHeight(size);
        var inset = size.Height < 100 ? 5f : 8f;
        var circle = new RectangleF(inset, inset, d, d);
        using var clip = new GraphicsPath();
        clip.AddEllipse(circle);
        var state = g.Save();
        g.SetClip(clip);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        lock (image)
            g.DrawImage(image, circle);
        g.Restore(state);
    }

    private static void DrawBadge(Graphics g, PixelSize size, WidgetTheme theme, int count, string glyph, Color background, Color foreground)
    {
        var height = BadgeHeight(size);
        using var font = new Font(theme.SecondaryFontFamily, height * 0.62f, GraphicsUnit.Pixel);
        using var iconFont = new Font(IconFont, height * 0.55f, GraphicsUnit.Pixel);
        var text = count.ToString();
        var textWidth = g.MeasureString(text, font).Width;
        var width = height * 0.9f + textWidth + height * 0.3f;
        var inset = 8f;
        var rect = new RectangleF(size.Width - width - inset, inset, width, height);
        using (var path = RoundedRect(Rectangle.Round(rect), (int)(height / 2)))
        using (var fill = new SolidBrush(Color.FromArgb(200, Blend(background, Color.Black, 0.55))))
            g.FillPath(fill, path);
        using var brush = new SolidBrush(foreground);
        g.DrawString(glyph, iconFont, brush, new RectangleF(rect.X + height * 0.15f, rect.Y, height * 0.75f, height), Centered);
        g.DrawString(text, font, brush, new RectangleF(rect.X + height * 0.8f, rect.Y, textWidth + height * 0.2f, height), Centered);
    }

    private static readonly StringFormat Centered = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    internal static WidgetFrame ToFrame(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new WidgetFrame(new PixelSize(bitmap.Width, bitmap.Height), FramePixelFormat.Bgra32, pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Clamp(radius * 2, 0, Math.Min(r.Width, r.Height));
        if (d == 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A cor um pouco mais clara (pulso de atenção).</summary>
    public static Color Lighter(Color color) => Blend(color, Color.White, 0.3);

    private static Color ToColor(WidgetColor c) => Color.FromArgb(c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Quanto do tamanho da fonte o desenho do glifo ocupa (a caixa da fonte tem sobra em volta).</summary>
    private const float GlyphFill = 0.8f;

    /// <summary>
    /// Desenha o glifo pelo contorno real, centrado em <paramref name="center"/> e com o maior lado igual a
    /// <paramref name="side"/> (como o SDK recomenda: a caixa da fonte não é centrada no desenho).
    /// </summary>
    private static void DrawGlyph(Graphics g, string glyph, PointF center, float side, Brush brush)
    {
        using var family = new FontFamily(IconFont);
        using var path = new GraphicsPath();
        path.AddString(glyph, family, (int)FontStyle.Regular, 100, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var scale = side / Math.Max(bounds.Width, bounds.Height);
        using var matrix = new Matrix();
        matrix.Translate(center.X, center.Y);
        matrix.Scale(scale, scale);
        matrix.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
        path.Transform(matrix);
        g.FillPath(brush, path);
    }

    private static string PickIconFont()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
