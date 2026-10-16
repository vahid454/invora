using Invora.Domain.Common;
using Invora.Infrastructure.Modules.Inventory;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;

namespace Invora.Infrastructure.Pdf;

public static class ItemLabelPdf
{
    public static string QrPng(string code)
    {
        using var data = QRCodeGenerator.GenerateQrCode(code, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        return Convert.ToBase64String(qr.GetGraphic(6));
    }

    public static byte[] Render(ItemLabelView[] labels, string size, int copies)
    {
        if (copies < 1 || copies > 100 || labels.Length == 0 || (long)copies * labels.Length > 200)
            throw new DomainException("VALIDATION_FAILED", "Print 1–100 copies per item, up to 200 labels per download.");
        var (width, height) = size switch
        {
            "50x30" => (50d, 30d), "60x40" => (60d, 40d), "70x50" => (70d, 50d),
            _ => throw new DomainException("VALIDATION_FAILED", "Choose a supported thermal label size.")
        };
        PdfFonts.EnsureInitialized();
        using var document = new PdfDocument();
        document.Info.Title = "Invora item labels";
        document.Info.Creator = "Invora";
        foreach (var label in labels)
        for (var copy = 0; copy < copies; copy++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(width); page.Height = XUnit.FromMillimeter(height);
            using var g = XGraphics.FromPdfPage(page);
            var margin = Mm(1.5); var contentWidth = Mm(width - 3);
            var titleFont = new XFont("Noto Sans", 7.5, XFontStyleEx.Bold);
            var bodyFont = new XFont("Noto Sans", width == 50 ? 6 : 7);
            var title = Wrap(g, label.Model, titleFont, contentWidth);
            // Model names may be abbreviated; identifiers are always printed in full.
            var titleLines = title.Take(2).ToArray();
            if (title.Count > 2) titleLines[1] = Ellipsis(g, titleLines[1], titleFont, contentWidth);
            var y = margin;
            foreach (var line in titleLines) { Draw(g, line, titleFont, margin, y); y += 9; }
            y += Mm(0.5);
            var qrSize = Math.Min(Mm(width == 50 ? 20 : 25), Mm(height) - margin - y);
            DrawQr(g, label.Code, margin, y, qrSize);
            var textX = margin + qrSize + Mm(1);
            var textWidth = Mm(width) - margin - textX;
            var lines = new List<string>();
            if (label.Details.Length > 0) lines.Add(label.Details);
            if (label.Condition.Length > 0) lines.Add(label.Condition);
            lines.AddRange(label.Identifiers.Length > 0 ? label.Identifiers : ["SKU: " + label.Sku]);
            if (label.Kind == "product") lines.Add("QUANTITY ITEM");
            var wrapped = lines.SelectMany(line => Wrap(g, line, bodyFont, textWidth)).ToArray();
            var lineHeight = bodyFont.Size + 1.5;
            if (y + wrapped.Length * lineHeight > Mm(height) - margin)
                throw new DomainException("VALIDATION_FAILED", "These details need a larger sticker. Choose 60 × 40 or 70 × 50 mm so every identity remains readable.");
            foreach (var line in wrapped) { Draw(g, line, bodyFont, textX, y); y += lineHeight; }
        }
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static double Mm(double value) => XUnit.FromMillimeter(value).Point;
    private static void Draw(XGraphics g, string text, XFont font, double x, double y) =>
        g.DrawString(text, font, XBrushes.Black, new XPoint(x, y + font.Size));

    private static void DrawQr(XGraphics g, string code, double x, double y, double size)
    {
        using var data = QRCodeGenerator.GenerateQrCode(code, QRCodeGenerator.ECCLevel.M);
        // QRCoder includes a four-module quiet zone. Keep it white and unscaled by the printer.
        // Snap the module grid to whole dots at common 203 dpi thermal resolution.
        // Vector output still scales cleanly on higher-resolution printers.
        const double dot = 72d / 203;
        var step = Math.Floor(size / data.ModuleMatrix.Count / dot) * dot;
        x = Math.Round((x + (size - step * data.ModuleMatrix.Count) / 2) / dot) * dot;
        y = Math.Round(y / dot) * dot;
        g.DrawRectangle(XBrushes.White, x, y, size, size);
        for (var row = 0; row < data.ModuleMatrix.Count; row++)
        for (var col = 0; col < data.ModuleMatrix.Count; col++)
            if (data.ModuleMatrix[row][col]) g.DrawRectangle(XBrushes.Black, x + col * step, y + row * step, step, step);
    }

    private static List<string> Wrap(XGraphics g, string text, XFont font, double width)
    {
        var lines = new List<string>(); var line = "";
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (g.MeasureString(line.Length == 0 ? word : line + " " + word, font).Width <= width)
            { line = line.Length == 0 ? word : line + " " + word; continue; }
            if (line.Length > 0) { lines.Add(line); line = ""; }
            foreach (var ch in word)
            {
                if (line.Length > 0 && g.MeasureString(line + ch, font).Width > width) { lines.Add(line); line = ""; }
                line += ch;
            }
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
    private static string Ellipsis(XGraphics g, string text, XFont font, double width)
    {
        while (text.Length > 0 && g.MeasureString(text + "…", font).Width > width) text = text[..^1];
        return text + "…";
    }
}
