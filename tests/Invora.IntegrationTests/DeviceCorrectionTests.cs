using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Identity;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    private Task<JsonElement> DeviceDetail(Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/{id}/details?branchId={branch}");
    private DeviceCorrectionRequest Correction(JsonElement detail, string color = "Blue") => new(branch, detail.GetProperty("revision").GetString()!, "8GB", "128GB", color, detail.GetProperty("current").GetProperty("condition").GetString()!, 88, "Screen and cameras tested; cable included.", "Corrected colour after checking the device.");

    [Fact]
    public async Task DeviceCorrectionChangesOnePhoneWithoutChangingMoneyAndKeepsSupplierReturnLinked()
    {
        var purchase = await Purchase([new(phone, 2, 1000, Devices: [new("730000000000001"), new("730000000000002")])]);
        var unit = await Unit();var detail = await DeviceDetail(unit.Id);var request = Correction(detail);var key = Guid.NewGuid().ToString();
        var response = await Post($"/inventory/{unit.Id}/corrections", request, key);response.EnsureSuccessStatusCode();
        var replay = await Post($"/inventory/{unit.Id}/corrections", request, key);replay.EnsureSuccessStatusCode();Assert.Equal(await response.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/inventory/{unit.Id}/corrections", request with {Color="Red"})).StatusCode);
        var changed = await DeviceDetail(unit.Id);Assert.Equal("Blue",changed.GetProperty("current").GetProperty("color").GetString());Assert.Single(changed.GetProperty("history").EnumerateArray());
        await using var scope = factory.Services.CreateAsyncScope();var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Equal(2, await db.Set<StockUnit>().CountAsync());Assert.Equal(2000, await db.Set<StockUnit>().SumAsync(x=>x.Cost));Assert.Equal(1,await db.Set<LedgerEntry>().CountAsync());Assert.Empty(await db.Set<Payment>().ToArrayAsync());Assert.Equal(2,await db.Set<InventoryMovement>().CountAsync());
        var other = await db.Set<StockUnit>().AsNoTracking().SingleAsync(x=>x.Id!=unit.Id);Assert.Equal(phone,other.ProductVariantId);Assert.Equal("Green",(await db.Set<ProductVariant>().SingleAsync(x=>x.Id==phone)).Color);
        var originalItem = await db.Set<PurchaseItem>().SingleAsync(x=>x.PurchaseId==purchase.Id);Assert.Equal(phone, originalItem.ProductVariantId);
        var identifier=await db.Set<UnitIdentifier>().Where(x=>x.StockUnitId==unit.Id).Select(x=>x.Value).SingleAsync();var scan=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/scan?branchId={branch}&value={identifier}");Assert.Contains("Blue",scan.GetProperty("unit").GetProperty("description").GetString());
        await Id($"/purchases/{purchase.Id}/returns",new PurchaseReturnRequest("Return corrected physical device","CORRECTED-RETURN",date,[new(originalItem.Id,1,unit.Id)]));
        Assert.Equal(DeviceStatus.WrittenOff,(await db.Set<StockUnit>().AsNoTracking().SingleAsync(x=>x.Id==unit.Id)).Status);
        var historyError=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM \"DeviceCorrection\""));Assert.Equal("23514",historyError.SqlState);
        var inspectionError=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE \"DeviceInspection\" SET \"Notes\"='tampered'"));Assert.Equal("23514",inspectionError.SqlState);
    }

    [Fact]
    public async Task UsedPhoneCorrectionsAndNewSalesSnapshotNoWarrantyWhilePastInvoiceRemainsUnchanged()
    {
        await Id("/device-acquisitions",new AcquisitionRequest(branch,customer,phone,new("730000000000003"),1000,1,"Used","Initial inspection",72,30));
        var unit=await Unit();var before=await DeviceDetail(unit.Id);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post($"/inventory/{unit.Id}/corrections",Correction(before) with{BatteryHealth=101})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post($"/inventory/{unit.Id}/corrections",Correction(before) with{Reason=" "})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post($"/inventory/{unit.Id}/corrections",Correction(before) with{Condition="New"})).StatusCode);
        await Id($"/inventory/{unit.Id}/corrections",Correction(before));var current=await DeviceDetail(unit.Id);
        var variant=current.GetProperty("unit").GetProperty("productVariantId").GetGuid();var draft=await Document("/sales",new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(variant,1,3000,unit.Id)],[]));var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var snapshot=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;Assert.Equal("Used",snapshot.Lines[0].Condition);Assert.Equal(0,snapshot.Lines[0].WarrantyMonths);Assert.Contains("No warranty",snapshot.Lines[0].WarrantyText);Assert.Contains("authorized service centre",snapshot.Seller.WarrantyTerms);Assert.Contains("not the shop",snapshot.Seller.WarrantyTerms);
        Assert.Equal(HttpStatusCode.Conflict,(await Post($"/inventory/{unit.Id}/corrections",Correction(current))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var stored=(await db.Set<Sale>().AsNoTracking().SingleAsync()).InvoiceJson;Assert.All(await db.Set<DeviceInspection>().ToArrayAsync(),x=>Assert.Equal(0,x.WarrantyDays));Assert.Equal(2,await db.Set<DeviceInspection>().CountAsync());
        var item=await db.Set<SaleItem>().SingleAsync();var returned=await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Returned for restock",date,[new(item.Id,1,"Restock")]));
        // Credit notes are exposed as PDFs; the persisted snapshot must retain the original condition.
        var note=await db.Set<SaleReturn>().SingleAsync();Assert.Contains("No warranty",Invora.Infrastructure.Modules.Retail.RetailOperations.Read<InvoiceSnapshot>(note.InvoiceJson).Lines[0].WarrantyText);
        var restocked=await DeviceDetail(unit.Id);await Id($"/inventory/{unit.Id}/corrections",Correction(restocked,"Red") with{BatteryHealth=85,Notes="Rechecked after customer return."});
        Assert.Equal(stored,(await db.Set<Sale>().AsNoTracking().SingleAsync()).InvoiceJson);Assert.Equal(1000,(await db.Set<StockUnit>().AsNoTracking().SingleAsync()).Cost);
        var pdf=await client.GetByteArrayAsync($"/api/v1/invoices/{sale.Id}/pdf");var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"));Directory.CreateDirectory(directory);await File.WriteAllBytesAsync(Path.Combine(directory,"invoice-used-warranty-sample.pdf"),pdf);
    }

    [Fact]
    public async Task DeviceCorrectionsRequireBranchAndEditPermissionWithoutExposingCost()
    {
        await Purchase([new(phone,1,1000,Devices:[new("730000000000004")])]);var unit=await Unit();var detail=await DeviceDetail(unit.Id);
        var staff=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("correction-viewer","Correction viewer","correction-pass-2026",["inventory.view"],[branch]));staff.EnsureSuccessStatusCode();
        using var limited=factory.CreateClient(new(){HandleCookies=false});var login=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("correction-viewer","correction-pass-2026"));login.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        var visible=await limited.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/{unit.Id}/details?branchId={branch}");Assert.False(visible.GetProperty("canEdit").GetBoolean());Assert.Equal(JsonValueKind.Null,visible.GetProperty("unit").GetProperty("cost").ValueKind);
        var req=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/inventory/{unit.Id}/corrections"){Content=JsonContent.Create(Correction(detail))};req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.Forbidden,(await limited.SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await limited.GetAsync($"/api/v1/inventory/{unit.Id}/details?branchId={Guid.NewGuid()}")).StatusCode);
        var editor=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("device-editor","Stock employee","correction-pass-2026",["inventory.view","inventory.adjust"],[branch]));editor.EnsureSuccessStatusCode();var editorLogin=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("device-editor","correction-pass-2026"));editorLogin.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await editorLogin.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        var edit=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/inventory/{unit.Id}/corrections"){Content=JsonContent.Create(Correction(detail))};edit.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());(await limited.SendAsync(edit)).EnsureSuccessStatusCode();
        var history=await limited.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/{unit.Id}/details?branchId={branch}");Assert.Equal(JsonValueKind.Null,history.GetProperty("unit").GetProperty("cost").ValueKind);Assert.Equal("Stock employee",history.GetProperty("history")[0].GetProperty("actor").GetString());Assert.DoesNotContain("cost",history.GetProperty("history").GetRawText(),StringComparison.OrdinalIgnoreCase);
    }
}
