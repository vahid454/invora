using PdfSharp.Fonts;

namespace Invora.Infrastructure.Pdf;

internal static class PdfFonts
{
    // CLR static initialization serializes invoice/label first-use across requests.
    static PdfFonts() => GlobalFontSettings.FontResolver = new InvoiceFontResolver();
    internal static void EnsureInitialized() { }
}
