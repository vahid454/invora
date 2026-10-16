using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Identity;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    private string LabelPath(string kind, params Guid[] ids) => $"/api/v1/inventory/labels?branchId={branch}&kind={kind}&ids={string.Join(',', ids)}";
    private Task<JsonElement> ScanLabel(string code, Guid? atBranch = null) => client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/scan?branchId={atBranch ?? branch}&value={Uri.EscapeDataString(code)}");

    [Fact]
    public async Task DeviceLabelsKeepIdentityThroughCorrectionsTransfersAndSales()
    {
        await Purchase([new(phone,2,1000,Devices:[new("740000000000001"),new("740000000000002")])]);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        var units=await db.Set<StockUnit>().AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync();
        var labels=(await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("unit",units.Select(x=>x.Id).ToArray())))!;
        Assert.Equal(2,labels.Select(x=>x.Code).Distinct().Count());
        var repeat=(await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("unit",units[0].Id)))![0];
        Assert.Equal(labels[0].Code,repeat.Code);Assert.Equal(labels[0].QrPng,repeat.QrPng);
        Assert.Equal(2,await db.Set<InventoryMovement>().CountAsync());Assert.Equal(1,await db.Set<LedgerEntry>().CountAsync());Assert.Empty(await db.Set<Payment>().ToArrayAsync());
        var code=repeat.Code;Assert.Equal(units[0].Id,(await ScanLabel(code)).GetProperty("unit").GetProperty("id").GetGuid());
        var filtered=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&search={Uri.EscapeDataString(code)}");Assert.Single(filtered.GetProperty("items").EnumerateArray());
        await Id($"/inventory/{units[0].Id}/corrections",Correction(await DeviceDetail(units[0].Id),"Blue"));
        var corrected=(await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("unit",units[0].Id)))![0];Assert.Equal(code,corrected.Code);Assert.Contains("Blue",corrected.Details);
        var current=(await ScanLabel(code)).GetProperty("unit");Assert.Contains("Blue",current.GetProperty("description").GetString());var variant=current.GetProperty("productVariantId").GetGuid();
        var catalog=await client.GetFromJsonAsync<JsonElement>($"/api/v1/products?search={Uri.EscapeDataString(code)}");Assert.Equal(variant,catalog.GetProperty("items")[0].GetProperty("id").GetGuid());
        var branchResponse=await Post("/branches",new{code="LABEL",name="Label counter"});branchResponse.EnsureSuccessStatusCode();var destination=(await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var transfer=await Document("/stock-transfers",new TransferRequest(branch,destination,"Label transfer",[new(variant,1,units[0].Id)]));
        Assert.Equal("InTransit",(await ScanLabel(code)).GetProperty("unit").GetProperty("status").GetString());
        (await Post($"/stock-transfers/{transfer.Id}/receive",new TransferReceiptRequest())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/inventory/scan?branchId={branch}&value={code}")).StatusCode);
        var moved=(await ScanLabel(code,destination)).GetProperty("unit");Assert.Equal("InStock",moved.GetProperty("status").GetString());
        var movedLabel=(await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("unit",units[0].Id).Replace(branch.ToString(),destination.ToString())))![0];Assert.Equal(code,movedLabel.Code);
        var draft=await Document("/sales",new SaleRequest(destination,customer,date,null,false,[new(variant,1,3000,units[0].Id)],[new(3000,1)]));
        var sale=await Document($"/sales/{draft.Id}/complete",new{});
        Assert.Equal("Sold",(await ScanLabel(code,destination)).GetProperty("unit").GetProperty("status").GetString());
        // Rendering labels first must not prevent invoice font initialization later.
        (await client.GetAsync(LabelPath("unit",units[0].Id).Replace(branch.ToString(),destination.ToString()).Replace("labels?","labels/pdf?"))).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/v1/invoices/{sale.Id}/pdf")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task QuantityLabelPdfHasPhysicalSizeAndBoundedCopiesWithoutChangingStock()
    {
        await Purchase([new(cable,10,100)]);
        var label=(await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("product",cable)))![0];
        Assert.Empty(label.Identifiers);Assert.Equal("CABLE",label.Sku);Assert.Equal(cable,(await ScanLabel(label.Code)).GetProperty("productVariantId").GetGuid());
        Assert.Equal(cable,(await ScanLabel("  CABLE-BARCODE  ")).GetProperty("productVariantId").GetGuid());
        var balances=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/balances?branchId={branch}&search={Uri.EscapeDataString(label.Code)}");Assert.Single(balances.GetProperty("items").EnumerateArray());Assert.Equal(10,balances.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"));Directory.CreateDirectory(output);
        foreach(var size in new (string Name,int Width,int Height)[]{("50x30",50,30),("60x40",60,40),("70x50",70,50)})
        {
            var pdf=await client.GetByteArrayAsync(LabelPath("product",cable).Replace("labels?","labels/pdf?")+"&copies=3&size="+size.Name);
            using var document=PdfReader.Open(new MemoryStream(pdf),PdfDocumentOpenMode.Import);Assert.Equal(3,document.PageCount);
            Assert.Equal(size.Width,document.Pages[0].Width.Millimeter,3);Assert.Equal(size.Height,document.Pages[0].Height.Millimeter,3);
            await File.WriteAllBytesAsync(Path.Combine(output,"item-label-"+size.Name+".pdf"),pdf);
        }
        await File.WriteAllBytesAsync(Path.Combine(output,"item-label-qr.png"),Convert.FromBase64String(label.QrPng));
        await File.WriteAllTextAsync(Path.Combine(output,"item-label-code.txt"),label.Code);
        foreach(var suffix in new[]{"&copies=0","&copies=201","&copies=1&size=A4"})Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(LabelPath("product",cable).Replace("labels?","labels/pdf?")+suffix)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(LabelPath("product",phone))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(LabelPath("product",cable,cable))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/inventory/scan?branchId={branch}&value=INVORA:P:broken")).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(10,(await db.Set<StockBalance>().SingleAsync()).Quantity);Assert.Single(await db.Set<InventoryMovement>().ToArrayAsync());
    }

    [Fact]
    public async Task ItemLabelsRequireInventoryPermissionAndBranchWithoutExposingMoney()
    {
        await Purchase([new(phone,1,1000,Devices:[new("740000000000003")])]);var unit=await Unit();
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(LabelPath("unit",unit.Id).Replace(branch.ToString(),Guid.NewGuid().ToString()))).StatusCode);
        using var anonymous=factory.CreateClient(new(){HandleCookies=false});Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(LabelPath("unit",unit.Id))).StatusCode);
        var staff=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("label-viewer","Stock label operator","label-test-password-2026",["inventory.view"],[branch]));staff.EnsureSuccessStatusCode();
        using var limited=factory.CreateClient(new(){HandleCookies=false});var login=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("label-viewer","label-test-password-2026"));login.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        var response=await limited.GetAsync(LabelPath("unit",unit.Id));response.EnsureSuccessStatusCode();var json=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json[0].TryGetProperty("cost",out _));Assert.False(json[0].TryGetProperty("sellingPrice",out _));Assert.False(json[0].TryGetProperty("customer",out _));
        var bytes=await limited.GetByteArrayAsync(LabelPath("unit",unit.Id).Replace("labels?","labels/pdf?"));
        var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"));await File.WriteAllBytesAsync(Path.Combine(output,"phone-label-50x30.pdf"),bytes);
        var noAccess=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("label-denied","No stock access","label-test-password-2026",["customers.view"],[branch]));noAccess.EnsureSuccessStatusCode();
        var deniedLogin=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("label-denied","label-test-password-2026"));deniedLogin.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await deniedLogin.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(LabelPath("unit",unit.Id))).StatusCode);
    }

    [Fact]
    public async Task AmbiguousRawBarcodesFailClearlyWhileItemQrSelectsExactlyOneProduct()
    {
        var product=(await client.GetFromJsonAsync<ProductView>($"/api/v1/products/{cable}"))!;
        var other=await Id("/products",new ProductRequest("Other cable","Generic","Cable","8544",product.TaxRateId,"CABLE-BARCODE",null,"","","White",false,250,300));
        var response=await client.GetAsync($"/api/v1/inventory/scan?branchId={branch}&value=CABLE-BARCODE");Assert.False(response.IsSuccessStatusCode);Assert.NotEqual(HttpStatusCode.InternalServerError,response.StatusCode);Assert.Contains("AMBIGUOUS_SCAN",await response.Content.ReadAsStringAsync());
        Assert.Equal(other,(await ScanLabel(new ItemLabelCode("product",other).Value)).GetProperty("productVariantId").GetGuid());
        Assert.Equal(cable,(await ScanLabel(new ItemLabelCode("product",cable).Value)).GetProperty("productVariantId").GetGuid());
    }
}
