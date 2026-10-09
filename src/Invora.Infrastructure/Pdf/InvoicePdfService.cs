using System.Globalization;
using Invora.Application.Abstractions;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Invoices;
using Invora.Infrastructure.Modules.Sales;
using Invora.Infrastructure.Modules.Retail;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
namespace Invora.Infrastructure.Pdf;
public sealed class InvoicePdfService(SalesService sales) : IInvoicePdfService
{
    static InvoicePdfService(){GlobalFontSettings.FontResolver=new InvoiceFontResolver();}
    public async Task<byte[]> RenderAsync(Guid invoiceId,CancellationToken cancellationToken)=>Render(await sales.InvoiceAsync(invoiceId,cancellationToken));
    public byte[] RenderReceipt(PaymentReceipt receipt)
    {
        var doc=new Document();doc.Info.Title="Payment receipt "+receipt.Number;doc.Styles[StyleNames.Normal]!.Font.Name="Noto Sans";doc.Styles[StyleNames.Normal]!.Font.Size=11;var s=doc.AddSection();s.PageSetup.PageFormat=PageFormat.A4;
        var title=s.AddParagraph(receipt.Direction=="In"?"Payment Received":"Payment Paid");title.Format.Font.Bold=true;title.Format.Font.Size=20;s.AddParagraph(receipt.SellerName).Format.Font.Bold=true;s.AddParagraph(receipt.SellerAddress);s.AddParagraph("Receipt: "+receipt.Number+"    Date: "+receipt.Date.ToString("dd-MMM-yyyy",CultureInfo.InvariantCulture));s.AddParagraph("Account: "+receipt.PartyName+" · "+receipt.PartyPhone);s.AddParagraph("Amount: "+Money(receipt.Amount)).Format.Font.Size=18;s.AddParagraph(IndianAmountWords.Format(receipt.Amount));s.AddParagraph("Method: "+receipt.Method);s.AddParagraph("Reference: "+receipt.Reference);s.AddParagraph(receipt.Note);s.AddParagraph("This receipt records a payment. Invoice allocations are maintained separately in the account statement.");var renderer=new PdfDocumentRenderer{Document=doc};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
    public byte[] RenderStatement(CustomerStatement statement)
    {
        var doc=new Document();doc.Info.Title="Account statement · "+statement.CustomerName;doc.Styles[StyleNames.Normal]!.Font.Name="Noto Sans";doc.Styles[StyleNames.Normal]!.Font.Size=9;var section=doc.AddSection();section.PageSetup.PageFormat=PageFormat.A4;section.PageSetup.LeftMargin=Unit.FromCentimeter(1.5);section.PageSetup.RightMargin=Unit.FromCentimeter(1.5);
        section.AddParagraph(statement.ShopName).Format.Font.Size=18;section.AddParagraph(statement.ShopAddress);section.AddParagraph("Customer account statement").Format.Font.Bold=true;section.AddParagraph(statement.CustomerName+" · "+statement.Phone+(statement.AlternatePhone.Length>0?" / "+statement.AlternatePhone:""));section.AddParagraph(statement.From.ToString("dd MMM yyyy")+" to "+statement.To.ToString("dd MMM yyyy"));section.AddParagraph("Opening: "+Balance(statement.OpeningBalance));
        var table=section.AddTable();table.Borders.Width=0.25;foreach(var width in new[]{2.2,6.6,3.0,3.0,3.2})table.AddColumn(Unit.FromCentimeter(width));var header=table.AddRow();header.HeadingFormat=true;var labels=new[]{"Date","Entry / details","Added to dues","Reduced dues","Who owes?"};for(var i=0;i<5;i++){header.Cells[i].AddParagraph(labels[i]);header.Cells[i].Format.Font.Bold=true;}
        foreach(var entry in statement.Rows){var row=table.AddRow();row.Cells[0].AddParagraph(entry.Date.ToString("dd MMM yy"));row.Cells[1].AddParagraph(entry.Kind);if(entry.Note.Length>0)row.Cells[1].AddParagraph(entry.Note);row.Cells[2].AddParagraph(Money(entry.Debit));row.Cells[3].AddParagraph(Money(entry.Credit));row.Cells[4].AddParagraph(Balance(entry.RunningBalance));for(var i=2;i<5;i++)row.Cells[i].Format.Alignment=ParagraphAlignment.Right;}
        section.AddParagraph("Net closing: "+Balance(statement.ClosingBalance)).Format.Font.Bold=true;section.AddParagraph("Sales / advances: "+Balance(statement.TradeBalance)+" · Independent LenDen: "+Balance(statement.IndependentBalance));section.AddParagraph("Lena hai: customer pays the shop. Dena hai: shop owes / holds customer credit. The net statement does not settle the separate sales and LenDen accounts.");var footer=section.Footers.Primary.AddParagraph("Page ");footer.AddPageField();footer.AddText(" of ");footer.AddNumPagesField();var renderer=new PdfDocumentRenderer{Document=doc};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
    public byte[] Render(InvoiceSnapshot invoice)
    {
        var doc=new Document();doc.Info.Title=invoice.DocumentTitle+" "+invoice.Number;doc.Info.Author=invoice.SellerName;
        var normal=doc.Styles[StyleNames.Normal]!;normal.Font.Name="Noto Sans";normal.Font.Size=8;normal.ParagraphFormat.SpaceAfter=1;
        var section=doc.AddSection();section.PageSetup.PageFormat=PageFormat.A4;section.PageSetup.TopMargin=Unit.FromCentimeter(1.2);section.PageSetup.BottomMargin=Unit.FromCentimeter(1.5);section.PageSetup.LeftMargin=Unit.FromCentimeter(1.2);section.PageSetup.RightMargin=Unit.FromCentimeter(1.2);
        var title=section.AddParagraph(invoice.DocumentTitle);title.Format.Font.Size=15;title.Format.Font.Bold=true;title.Format.Alignment=ParagraphAlignment.Center;title.Format.SpaceAfter=6;
        var header=section.AddTable();header.Borders.Width=0.5;header.AddColumn(Unit.FromCentimeter(9.3));header.AddColumn(Unit.FromCentimeter(9.3));
        var first=header.AddRow();first.Cells[0].AddParagraph(invoice.SellerName).Format.Font.Bold=true;if(!string.IsNullOrWhiteSpace(invoice.Seller.LegalName)&&!invoice.Seller.LegalName.Equals(invoice.SellerName,StringComparison.OrdinalIgnoreCase))first.Cells[0].AddParagraph(invoice.Seller.LegalName);first.Cells[0].AddParagraph(invoice.Seller.Address);first.Cells[0].AddParagraph("GSTIN: "+(invoice.Seller.GstRegistered?invoice.Seller.Gstin:"Not registered"));first.Cells[0].AddParagraph("State: "+GstSupply.Label(invoice.Seller.StateCode)+(string.IsNullOrWhiteSpace(invoice.Seller.Pan)?"":"   PAN: "+invoice.Seller.Pan));if(!string.IsNullOrWhiteSpace(invoice.Seller.Phone+invoice.Seller.Email))first.Cells[0].AddParagraph(invoice.Seller.Phone+"  "+invoice.Seller.Email);
        first.Cells[1].AddParagraph((invoice.DocumentTitle=="CREDIT NOTE"?"Credit Note No: ":"Invoice No: ")+invoice.Number).Format.Font.Bold=true;
        if(!string.IsNullOrEmpty(invoice.Reference))first.Cells[1].AddParagraph("Shop reference: "+invoice.Reference);
        first.Cells[1].AddParagraph("Dated: "+invoice.Date.ToString("dd-MMM-yyyy",CultureInfo.InvariantCulture));
        if(invoice.IssuedAt is DateTimeOffset issued)first.Cells[1].AddParagraph("Issued: "+issued.ToString("dd-MMM-yyyy HH:mm:ss zzz",CultureInfo.InvariantCulture));
        first.Cells[1].AddParagraph("Due date: "+(invoice.DueDate?.ToString("dd-MMM-yyyy",CultureInfo.InvariantCulture)??"Not on credit"));
        first.Cells[1].AddParagraph("Place of supply: "+GstSupply.Label(string.IsNullOrEmpty(invoice.SupplyStateCode)?(invoice.Interstate?invoice.CustomerStateCode:invoice.Seller.StateCode):invoice.SupplyStateCode));
        first.Cells[1].AddParagraph(invoice.Interstate?"Inter-state · IGST":"Intra-state · CGST + SGST / UTGST");
        if(!string.IsNullOrEmpty(invoice.OriginalInvoiceNumber))first.Cells[1].AddParagraph("Original invoice: "+invoice.OriginalInvoiceNumber+" · "+invoice.OriginalInvoiceDate?.ToString("dd-MMM-yyyy",CultureInfo.InvariantCulture));
        var buyer=header.AddRow();buyer.Cells[0].AddParagraph("Buyer (Bill to)").Format.Font.Bold=true;buyer.Cells[0].AddParagraph(invoice.CustomerName);if(!string.IsNullOrWhiteSpace(invoice.CustomerAddress))buyer.Cells[0].AddParagraph(invoice.CustomerAddress);buyer.Cells[0].AddParagraph("GSTIN: "+(string.IsNullOrEmpty(invoice.CustomerGstin)?"Unregistered customer":invoice.CustomerGstin));buyer.Cells[0].AddParagraph("Contact: "+invoice.CustomerPhone+(string.IsNullOrEmpty(invoice.CustomerAlternatePhone)?"":" / "+invoice.CustomerAlternatePhone));buyer.Cells[0].AddParagraph("State: "+GstSupply.Label(string.IsNullOrEmpty(invoice.CustomerStateCode)?invoice.SupplyStateCode:invoice.CustomerStateCode));buyer.Cells[1].AddParagraph("Delivery / Terms").Format.Font.Bold=true;buyer.Cells[1].AddParagraph(invoice.Seller.ReturnPolicy);buyer.Cells[1].AddParagraph(invoice.Seller.WarrantyTerms);
        section.AddParagraph();var table=section.AddTable();table.Borders.Width=0.5;
        foreach(var width in new[]{0.6,8.0,1.5,1.0,2.7,0.8,4.0})table.AddColumn(Unit.FromCentimeter(width));
        var headings=table.AddRow();headings.HeadingFormat=true;headings.Shading.Color=Colors.LightGray;
        var labels=new[]{"Sl","Description of goods / identities","HSN / SAC","Qty","Net rate","Per","Taxable amount"};for(var i=0;i<labels.Length;i++){headings.Cells[i].AddParagraph(labels[i]);headings.Cells[i].Format.Font.Bold=true;}
        var index=1;foreach(var line in DisplayLines(invoice.Lines)){var row=table.AddRow();row.Cells[0].AddParagraph((index++).ToString(CultureInfo.InvariantCulture));row.Cells[1].AddParagraph(line.Description).Format.Font.Bold=true;foreach(var id in line.Identifiers)row.Cells[1].AddParagraph(id);if(!string.IsNullOrEmpty(line.WarrantyText))row.Cells[1].AddParagraph(line.WarrantyText);else if(line.WarrantyMonths>0)row.Cells[1].AddParagraph("Warranty: "+line.WarrantyMonths+" months");if(line.Discount>0)row.Cells[1].AddParagraph("Discount applied: INR "+Money(line.Discount));row.Cells[2].AddParagraph(line.Hsn);row.Cells[3].AddParagraph(line.Quantity.ToString(CultureInfo.InvariantCulture));row.Cells[4].AddParagraph(Money(line.Quantity>0?line.Taxable/line.Quantity:0));row.Cells[5].AddParagraph("PCS");row.Cells[6].AddParagraph(Money(line.Taxable));for(var c=3;c<7;c++)row.Cells[c].Format.Alignment=ParagraphAlignment.Right;}
        void AmountRow(string label,decimal value,bool bold=false){var row=table.AddRow();row.Cells[0].MergeRight=5;row.Cells[0].AddParagraph(label);row.Cells[0].Format.Alignment=ParagraphAlignment.Right;row.Cells[6].AddParagraph(Money(value));row.Cells[6].Format.Alignment=ParagraphAlignment.Right;row.Format.Font.Bold=bold;}
        AmountRow("Taxable subtotal",invoice.Taxable,true);
        if(invoice.Interstate)AmountRow("IGST",invoice.Igst);else{AmountRow("CGST",invoice.Cgst);AmountRow("SGST / UTGST",invoice.Sgst);}
        if(invoice.Cess!=0)AmountRow("Cess",invoice.Cess);
        var total=table.AddRow();total.Cells[0].MergeRight=2;total.Cells[0].AddParagraph("Invoice total");total.Cells[3].AddParagraph(invoice.Lines.Sum(x=>x.Quantity).ToString(CultureInfo.InvariantCulture));total.Cells[6].AddParagraph("INR "+Money(invoice.Total));total.Cells[6].Format.Alignment=ParagraphAlignment.Right;total.Format.Font.Bold=true;
        section.AddParagraph("Net rates and item amounts exclude GST and reflect discounts. Tax and total amounts use the posted two-decimal values.").Format.Font.Size=7;
        var chargeWords=section.AddParagraph("Amount chargeable (in words): "+IndianAmountWords.Format(invoice.Total));chargeWords.Format.Font.Bold=true;chargeWords.Format.SpaceBefore=8;chargeWords.Format.SpaceAfter=8;chargeWords.Format.KeepWithNext=true;
        var taxes=section.AddTable();taxes.Borders.Width=0.5;
        var hasCess=invoice.Cess!=0;var widths=hasCess?new[]{2.1,2.8,0.8,2.3,0.8,2.3,0.8,2.3,4.4}:new[]{3.0,3.0,1.6,2.8,1.6,2.8,3.8};foreach(var width in widths)taxes.AddColumn(Unit.FromCentimeter(width));
        var th=taxes.AddRow();th.HeadingFormat=true;th.Shading.Color=Colors.LightGray;
        var taxLabels=new List<string>{"HSN / SAC","Taxable value","CGST\n%","CGST amount",invoice.Interstate?"IGST\n%":"SGST/UT\n%",invoice.Interstate?"IGST amount":"SGST / UT amount"};if(hasCess)taxLabels.AddRange(["Cess %","Cess amount"]);taxLabels.Add("Total tax");for(var i=0;i<taxLabels.Count;i++){th.Cells[i].AddParagraph(taxLabels[i]);th.Cells[i].Format.Font.Bold=true;}
        foreach(var g in invoice.Lines.GroupBy(x=>new{x.Hsn,x.Rate,x.CessRate})){var row=taxes.AddRow();var amounts=new List<string>{g.Key.Hsn,Money(g.Sum(x=>x.Taxable)),(invoice.Interstate?0:g.Key.Rate/2).ToString("0.##",CultureInfo.InvariantCulture),Money(g.Sum(x=>x.Cgst)),(invoice.Interstate?g.Key.Rate:g.Key.Rate/2).ToString("0.##",CultureInfo.InvariantCulture),Money(g.Sum(x=>invoice.Interstate?x.Igst:x.Sgst))};if(hasCess)amounts.AddRange([g.Key.CessRate.ToString("0.##",CultureInfo.InvariantCulture),Money(g.Sum(x=>x.Cess))]);amounts.Add(Money(g.Sum(x=>x.Cgst+x.Sgst+x.Igst+x.Cess)));for(var i=0;i<amounts.Count;i++){row.Cells[i].AddParagraph(amounts[i]);if(i>0)row.Cells[i].Format.Alignment=ParagraphAlignment.Right;}}
        var taxTotal=taxes.AddRow();taxTotal.Format.Font.Bold=true;taxTotal.Cells[0].AddParagraph("Total");taxTotal.Cells[1].AddParagraph(Money(invoice.Taxable));taxTotal.Cells[3].AddParagraph(Money(invoice.Cgst));taxTotal.Cells[5].AddParagraph(Money(invoice.Interstate?invoice.Igst:invoice.Sgst));if(hasCess)taxTotal.Cells[7].AddParagraph(Money(invoice.Cess));taxTotal.Cells[taxLabels.Count-1].AddParagraph(Money(invoice.Cgst+invoice.Sgst+invoice.Igst+invoice.Cess));
        var taxWords=section.AddParagraph("Tax amount (in words): "+IndianAmountWords.Format(invoice.Cgst+invoice.Sgst+invoice.Igst+invoice.Cess));taxWords.Format.SpaceBefore=6;taxWords.Format.SpaceAfter=10;taxWords.Format.KeepWithNext=true;
        var footer=section.AddTable();footer.Borders.Width=0.5;footer.AddColumn(Unit.FromCentimeter(9.3));footer.AddColumn(Unit.FromCentimeter(9.3));var f=footer.AddRow();f.Cells[0].AddParagraph("Declaration").Format.Font.Bold=true;f.Cells[0].AddParagraph(invoice.Seller.Declaration);f.Cells[1].AddParagraph("Company bank details").Format.Font.Bold=true;f.Cells[1].AddParagraph(invoice.Seller.BankName);f.Cells[1].AddParagraph("Account holder: "+invoice.Seller.AccountHolder);f.Cells[1].AddParagraph("A/c: "+invoice.Seller.AccountNumber);f.Cells[1].AddParagraph("IFSC: "+invoice.Seller.Ifsc+"   "+invoice.Seller.BankBranch);f.Cells[1].AddParagraph("UPI: "+invoice.Seller.UpiId);f.Cells[1].AddParagraph("For "+invoice.SellerName+"\n\nAuthorized signatory").Format.Alignment=ParagraphAlignment.Right;
        section.AddParagraph(invoice.Seller.Jurisdiction).Format.Alignment=ParagraphAlignment.Center;section.AddParagraph("This is a computer generated invoice").Format.Alignment=ParagraphAlignment.Center;
        var page=section.Footers.Primary.AddParagraph();page.Format.Alignment=ParagraphAlignment.Right;page.AddText("Page ");page.AddPageField();page.AddText(" of ");page.AddNumPagesField();
        var renderer=new PdfDocumentRenderer{Document=doc};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
    private static IEnumerable<InvoiceLine> DisplayLines(InvoiceLine[] lines)
    {
        // Combine identical physical phones for printing only; retain every identity and every posted amount.
        var groups=lines.Select((line,index)=>new{line,index}).GroupBy(x=>x.line.Identifiers.Length>0&&x.line.Quantity==1
            ?x.line.Description+"|"+x.line.Hsn+"|"+x.line.Taxable+"|"+x.line.Rate+"|"+x.line.CessRate+"|"+x.line.Discount+"|"+x.line.WarrantyMonths+"|"+x.line.Condition+"|"+x.line.WarrantyText
            :"ROW-"+x.index);
        foreach(var group in groups)foreach(var chunk in group.Select(x=>x.line).Chunk(5)){
            var first=chunk[0];yield return first with{Quantity=chunk.Sum(x=>x.Quantity),Identifiers=chunk.SelectMany(x=>x.Identifiers).ToArray(),Discount=chunk.Sum(x=>x.Discount),Taxable=chunk.Sum(x=>x.Taxable),Cgst=chunk.Sum(x=>x.Cgst),Sgst=chunk.Sum(x=>x.Sgst),Igst=chunk.Sum(x=>x.Igst),Cess=chunk.Sum(x=>x.Cess),Total=chunk.Sum(x=>x.Total)};
        }
    }
    private static string Balance(decimal value)=>"INR "+Money(Math.Abs(value))+" · "+(value>0?"Lena hai":value<0?"Dena hai":"Settled");
    private static string Money(decimal value)=>value.ToString("N2",CultureInfo.GetCultureInfo("en-IN"));
}
internal sealed class InvoiceFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic)=>new(bold?"bold":"regular");
    public byte[] GetFont(string faceName)
    {
        var assembly=typeof(InvoiceFontResolver).Assembly;var suffix=faceName=="bold"?"NotoSans-Bold.ttf":"NotoSans-Regular.ttf";var resource=assembly.GetManifestResourceNames().Single(x=>x.EndsWith(suffix,StringComparison.Ordinal));using var stream=assembly.GetManifestResourceStream(resource)!;using var buffer=new MemoryStream();stream.CopyTo(buffer);return buffer.ToArray();
    }
}
