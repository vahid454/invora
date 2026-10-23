using System.Globalization;
using Invora.Infrastructure.Modules.Exports;
using Invora.Infrastructure.Pdf;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Invora.Infrastructure.Modules.Reports;

public sealed class CustomerRegisterExportService(ReportService reports)
{
    private static readonly CultureInfo Indian=CultureInfo.GetCultureInfo("en-IN");
    private static string Money(decimal amount)=>amount.ToString("N2",Indian);
    private static string CsvAmount(decimal amount)=>amount.ToString("0.00",CultureInfo.InvariantCulture);
    private static string Date(DateOnly? date)=>date?.ToString("dd MMM yyyy",CultureInfo.InvariantCulture)??"";
    private static string Origin(RegisterOrigin source)=>Date(source.Date)+" · "+source.Source+(string.IsNullOrWhiteSpace(source.Reference)?"":" · "+source.Reference)+(string.IsNullOrWhiteSpace(source.Description)?"":" · "+source.Description)+" · INR "+Money(source.Outstanding);

    public async Task<byte[]> CsvAsync(Guid branch,string? search,string? filter,CancellationToken ct)
    {
        var report=await reports.CustomerRegisterReportAsync(branch,search,filter,ct);
        var rows=report.Rows.Select(x=>new[]{
            Date(report.AsOf),report.BranchName,x.Name,x.SafetyStatus=="Clear"?x.Phone:"",x.SafetyStatus=="Clear"?x.AlternatePhone:"",
            CsvAmount(Math.Max(0,x.TradeBalance)),CsvAmount(Math.Max(0,x.IndependentBalance)),CsvAmount(x.LenaHai),
            CsvAmount(Math.Max(0,-x.TradeBalance)),CsvAmount(Math.Max(0,-x.IndependentBalance)),CsvAmount(x.DenaHai),
            x.OpenDocumentCount.ToString(CultureInfo.InvariantCulture),CsvAmount(x.OpenDocumentTotal),Date(x.NextDueOrPromise),Date(x.NextFollowUp),
            x.SafetyStatus=="Clear"?"Contact allowed":"DO NOT CONTACT - "+x.SafetyStatus,
            x.SafetyStatus=="Clear"?x.Address:"",Date(x.FirstOriginalDate),string.Join(" | ",x.Origins.Select(Origin))
        }).ToList();
        rows.Add([Date(report.AsOf),report.BranchName,"TOTAL (matching customers)","","",
            CsvAmount(report.Rows.Sum(x=>Math.Max(0,x.TradeBalance))),CsvAmount(report.Rows.Sum(x=>Math.Max(0,x.IndependentBalance))),CsvAmount(report.Rows.Sum(x=>x.LenaHai)),
            CsvAmount(report.Rows.Sum(x=>Math.Max(0,-x.TradeBalance))),CsvAmount(report.Rows.Sum(x=>Math.Max(0,-x.IndependentBalance))),CsvAmount(report.Rows.Sum(x=>x.DenaHai)),
            report.Rows.Sum(x=>x.OpenDocumentCount).ToString(CultureInfo.InvariantCulture),CsvAmount(report.Rows.Sum(x=>x.OpenDocumentTotal)),"","","Internal register; flagged contacts hidden","","",""]);
        return ExportService.Rows(["As of","Branch","Customer","Phone","Second phone","Sales / old dues - Lena hai","Independent LenDen - Lena hai","Total Lena hai","Sales advance / credit - Dena hai","Independent LenDen held - Dena hai","Total Dena hai","Open invoice / old-due entries","Open document total (reference only; do not add to Lena hai)","Next due / promise","Next follow-up","Contact rule","Customer address","First original date","Original dates and sources (open balance; FIFO for LenDen)"],rows);
    }

    public async Task<byte[]> CallSheetPdfAsync(Guid branch,string? search,CancellationToken ct)
    {
        var report=await reports.CustomerRegisterReportAsync(branch,search,"owed",ct);
        var callRows=report.Rows.Where(x=>x.LenaHai>0&&x.SafetyStatus=="Clear").OrderBy(x=>x.NextDueOrPromise??DateOnly.MaxValue).ThenBy(x=>x.Name).ThenBy(x=>x.Id).ToArray();
        var flagged=report.Rows.Where(x=>x.LenaHai>0&&x.SafetyStatus!="Clear").ToArray();
        PdfFonts.EnsureInitialized();
        var document=new Document();document.Info.Title="Customer collection call sheet";document.Info.Author=report.ShopName;
        var normal=document.Styles[StyleNames.Normal]!;normal.Font.Name="Noto Sans";normal.Font.Size=8;normal.ParagraphFormat.SpaceAfter=2;
        var section=document.AddSection();section.PageSetup.PageFormat=PageFormat.A4;section.PageSetup.LeftMargin=Unit.FromCentimeter(1.25);section.PageSetup.RightMargin=Unit.FromCentimeter(1.25);section.PageSetup.TopMargin=Unit.FromCentimeter(1.2);section.PageSetup.BottomMargin=Unit.FromCentimeter(1.4);
        var shop=section.AddParagraph(report.ShopName);shop.Format.Font.Bold=true;shop.Format.Font.Size=14;
        section.AddParagraph(report.BranchName+" · Customer collection call sheet").Format.Font.Bold=true;
        section.AddParagraph("As of "+Date(report.AsOf)+" · "+callRows.Length+" customers to contact · Total Lena hai: INR "+Money(callRows.Sum(x=>x.LenaHai))).Format.Font.Bold=true;
        section.AddParagraph("Sales / old dues and independent LenDen are separate balances. Open invoice amounts are already reflected in the sales account; do not add them again.").Format.Font.Size=7;
        if(flagged.Length>0){var warning=section.AddParagraph(flagged.Length+" internally flagged customer(s), INR "+Money(flagged.Sum(x=>x.LenaHai))+", excluded from this call sheet. Review privately with the owner; do not contact them.");warning.Format.Font.Bold=true;warning.Format.Font.Size=7;}
        section.AddParagraph().Format.SpaceAfter=2;
        var table=section.AddTable();table.Borders.Width=0.35;table.Format.Font.Size=8;
        foreach(var width in new[]{0.6,4.0,4.0,1.9,1.9,1.9,1.8,2.4})table.AddColumn(Unit.FromCentimeter(width));
        var headings=table.AddRow();headings.HeadingFormat=true;headings.Shading.Color=Colors.LightGray;headings.Format.Font.Bold=true;
        var labels=new[]{"#","Customer / contact / address","Original date / source","Sales + old dues\nINR","LenDen given\nINR","Total Lena hai\nINR","Due / call","Call notes"};
        for(var i=0;i<labels.Length;i++)headings.Cells[i].AddParagraph(labels[i]);
        for(var i=0;i<callRows.Length;i++)
        {
            var account=callRows[i];var row=table.AddRow();row.HeightRule=RowHeightRule.AtLeast;row.Height=Unit.FromCentimeter(1.45);
            row.Cells[0].AddParagraph((i+1).ToString(CultureInfo.InvariantCulture));
            row.Cells[1].AddParagraph(account.Name).Format.Font.Bold=true;
            row.Cells[1].AddParagraph(account.Phone+(account.AlternatePhone.Length>0?" / "+account.AlternatePhone:""));
            if(!string.IsNullOrWhiteSpace(account.Address))row.Cells[1].AddParagraph(account.Address);
            foreach(var source in account.Origins.Take(3))row.Cells[2].AddParagraph(Origin(source));
            if(account.Origins.Length>3)row.Cells[2].AddParagraph("+ "+(account.Origins.Length-3)+" more; see CSV / statement");
            row.Cells[3].AddParagraph(Money(Math.Max(0,account.TradeBalance)));
            row.Cells[4].AddParagraph(Money(Math.Max(0,account.IndependentBalance)));
            row.Cells[5].AddParagraph(Money(account.LenaHai)).Format.Font.Bold=true;
            row.Cells[6].AddParagraph(Date(account.NextDueOrPromise).Length>0?Date(account.NextDueOrPromise):"No date set");
            if(account.NextFollowUp is not null)row.Cells[6].AddParagraph("Call: "+Date(account.NextFollowUp));
            row.Cells[7].AddParagraph("____________");
            foreach(var c in new[]{3,4,5})row.Cells[c].Format.Alignment=ParagraphAlignment.Right;
        }
        if(callRows.Length==0)section.AddParagraph("No eligible customer balances match this search.");
        var note=section.AddParagraph("Original dates identify the open invoice, old due or money-given entry. LenDen receipts offset the oldest money given first. Export CSV for every source when a customer has more than three. Record actual payments in Invora before using this sheet again. This is an internal worklist, not a customer statement or proof that a reminder was sent.");note.Format.Font.Size=7;note.Format.SpaceBefore=7;
        var footer=section.Footers.Primary.AddParagraph();footer.Format.Alignment=ParagraphAlignment.Right;footer.Format.Font.Size=7;footer.AddText("Internal collection worklist · Page ");footer.AddPageField();footer.AddText(" of ");footer.AddNumPagesField();
        var renderer=new PdfDocumentRenderer{Document=document};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
}
