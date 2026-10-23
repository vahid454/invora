using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Identity;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using Xunit;
namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    private ExistingDueRequest OldDue(string reference="REGISTER-1",decimal amount=5000)=>new(branch,customer,reference,"Phone down payment","Finance reference only",amount,date.AddDays(-30),date.AddDays(10));
    private async Task<JsonElement> Collections(string query=""){
        var response=await client.GetAsync($"/api/v1/lenden/collections?branchId={branch}&status=all{query}");Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<JsonElement> ImportPreview(string rows){var response=await Post("/lenden/imports/validate",new ExistingDueImportRequest(branch,"name,phone,alternatePhone,reference,description,financeReference,amount,originalDate,dueDate\n"+rows));Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return await response.Content.ReadFromJsonAsync<JsonElement>();}

    [Fact] public async Task ExistingDuesIncreaseReceivablesWithoutSalesStockTaxOrCashAndAreIdempotent()
    {
        var request=OldDue();var key=Guid.NewGuid().ToString();var response=await Post("/lenden/existing-dues",request,key);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());var id=await response.Content.ReadFromJsonAsync<Guid>();
        Assert.Equal(id,await (await Post("/lenden/existing-dues",request,key)).Content.ReadFromJsonAsync<Guid>());
        Assert.False((await Post("/lenden/existing-dues",request)).IsSuccessStatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Single(await db.Set<ExistingDue>().ToArrayAsync());Assert.Empty(await db.Set<Payment>().ToArrayAsync());Assert.Empty(await db.Set<Sale>().ToArrayAsync());Assert.Empty(await db.Set<InventoryMovement>().ToArrayAsync());
        var ledger=Assert.Single(await db.Set<LedgerEntry>().ToArrayAsync());Assert.Equal(5000,ledger.Debit);Assert.Equal(id,ledger.ExistingDueId);Assert.Equal("OpeningDue",ledger.Kind);Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).Date),ledger.BusinessDate);
        var dashboard=await client.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/summary?branchId={branch}");Assert.Equal(0,dashboard.GetProperty("grossSales").Amount());Assert.Equal(0,dashboard.GetProperty("paymentsReceived").Amount());Assert.Equal(5000,dashboard.GetProperty("customerOutstanding").Amount());
        var rows=await Collections();Assert.Equal(5000,rows.GetProperty("totalOutstanding").Amount());Assert.Equal("existing-due",rows.GetProperty("items")[0].GetProperty("kind").GetString());
        var directChange=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE \"ExistingDue\" SET \"Amount\"=1"));Assert.Equal("23514",directChange.SqlState);
    }
    [Fact] public async Task ExistingDuePartialAndExcessReceiptsReverseAndRefundWithoutOverallocating()
    {
        var id=await Id("/lenden/existing-dues",OldDue());
        var partial=await Id("/payments",new PaymentRequest(branch,customer,2000,2,date,"UPI-1","Partial down payment",[],ExistingDueId:id));
        var first=await Collections();Assert.Equal(3000,first.GetProperty("totalOutstanding").Amount());
        var extra=await Id("/payments",new PaymentRequest(branch,customer,4000,1,date,"","Remaining plus credit",[],ExistingDueId:id));
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");Assert.Equal(-1000,account.GetProperty("tradeBalance").Amount());
        await Id("/payments",new PaymentRequest(branch,customer,1000,1,date,"","Return excess",[],Refund:true));
        Assert.False((await Post("/payments",new PaymentRequest(branch,customer,1,1,date,"","Too much",[],Refund:true))).IsSuccessStatusCode);
        await Id($"/payments/{extra}/reverse",new ReasonRequest("Receipt was incorrect"));
        var after=await Collections();Assert.Equal(3000,after.GetProperty("totalOutstanding").Amount());
        Assert.False((await Post($"/lenden/existing-dues/{id}/cancel",new ReasonRequest("Cannot hide payment"))).IsSuccessStatusCode);
        await Id($"/payments/{partial}/reverse",new ReasonRequest("Wrong opening account"));
        await Id($"/lenden/existing-dues/{id}/cancel",new ReasonRequest("Incorrect register entry"));
        var cancelled=await Collections();Assert.True(cancelled.GetProperty("items")[0].GetProperty("cancelled").GetBoolean());Assert.Equal(0,cancelled.GetProperty("totalOutstanding").Amount());
        Assert.False((await Post("/payments",new PaymentRequest(branch,customer,1,1,date,"","Cancelled",[],ExistingDueId:id))).IsSuccessStatusCode);
    }
    [Fact] public async Task AutomaticAllocationUsesOldestRegisterAndInvoiceDuesAndCanApplyAnExistingAdvance()
    {
        var old=await Id("/lenden/existing-dues",OldDue(amount:100));await Purchase([new(cable,1,100)]);var sale=await Sale(null);
        await Id("/payments",new PaymentRequest(branch,customer,150,1,date,"","Combined receipt",[],AutoAllocate:true));
        var report=await Collections();var rows=report.GetProperty("items").EnumerateArray().ToArray();Assert.Equal(0,rows.Single(x=>x.GetProperty("id").GetGuid()==old).GetProperty("outstanding").Amount());Assert.Equal(150,rows.Single(x=>x.GetProperty("id").GetGuid()==sale.Id).GetProperty("outstanding").Amount());
        await Id("/payments",new PaymentRequest(branch,customer,50,2,date,"","On account",[]));
        Assert.False((await client.GetAsync($"/api/v1/lenden/collections/{customer}/reminder?branchId={branch}")).IsSuccessStatusCode);
        var applied=await Post("/payments/allocate-account-credit",new AllocateCustomerCreditRequest(branch,customer));Assert.True(applied.IsSuccessStatusCode,await applied.Content.ReadAsStringAsync());Assert.Equal(50,(await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applied").Amount());
        Assert.Equal(100,(await Collections()).GetProperty("totalOutstanding").Amount());
        var draft=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/collections/{customer}/reminder?branchId={branch}");Assert.Equal("Draft only",draft.GetProperty("deliveryStatus").GetString());Assert.Equal(100,draft.GetProperty("amount").Amount());Assert.Contains("Smart Plaza",draft.GetProperty("message").GetString());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<Payment>().CountAsync());Assert.Empty(await db.Set<PaymentFollowUp>().ToArrayAsync());
        Assert.False((await Post("/lenden/existing-dues",OldDue(sale.Number))).IsSuccessStatusCode);
    }
    [Fact] public async Task ReviewedImportMatchesPhoneCreatesCustomersAndCommitsOnceWithoutMoneyMovements()
    {
        var preview=await ImportPreview("Customer,+91 9000000002,,LEGACY-A,Old phone,,2500.25,2026-09-01,2026-10-20\nNew register customer,9000000088,9000000089,LEGACY-B,Down payment,Finance note,1000,2026-09-02,2026-11-01\nNew register customer,9000000088,9000000089,LEGACY-C,Other phone,,500,2026-09-03,2026-11-03");
        Assert.Equal("Validated",preview.GetProperty("status").GetString());Assert.Equal(customer,preview.GetProperty("preview")[0].GetProperty("customerId").GetGuid());Assert.Equal(4000.25m,preview.GetProperty("totalAmount").Amount());
        var id=preview.GetProperty("id").GetGuid();var key=Guid.NewGuid().ToString();var response=await Post($"/lenden/imports/{id}/commit",new{},key);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.True((await Post($"/lenden/imports/{id}/commit",new{},key)).IsSuccessStatusCode);Assert.False((await Post($"/lenden/imports/{id}/commit",new{})).IsSuccessStatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(3,await db.Set<ExistingDue>().CountAsync());Assert.Equal(2,await db.Set<Party>().CountAsync(x=>x.Kind==PartyKind.Customer));Assert.Equal("9000000089",(await db.Set<Party>().SingleAsync(x=>x.Phone=="9000000088")).AlternatePhone);Assert.Empty(await db.Set<Payment>().ToArrayAsync());
        var duplicate=await ImportPreview("Customer,9000000002,,LEGACY-A,Again,,2500.25,2026-09-01,2026-10-20");Assert.Equal("Invalid",duplicate.GetProperty("status").GetString());
    }
    [Fact] public async Task ImportPreviewReplayKeepsTheSameJsonContractAndReviewedRows()
    {
        var request=new ExistingDueImportRequest(branch,"name,phone,alternatePhone,reference,description,financeReference,amount,originalDate,dueDate\nCustomer,9000000002,,RETRY-1,Phone,,100.50,2026-09-01,2026-10-20");var key=Guid.NewGuid().ToString();
        var first=await Post("/lenden/imports/validate",request,key);Assert.True(first.IsSuccessStatusCode,await first.Content.ReadAsStringAsync());var retry=await Post("/lenden/imports/validate",request,key);Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());
        Assert.Equal(await first.Content.ReadAsStringAsync(),await retry.Content.ReadAsStringAsync());
        var preview=await retry.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("Validated",preview.GetProperty("status").GetString());Assert.Equal("100.50",preview.GetProperty("totalAmount").GetString());Assert.Equal(customer,preview.GetProperty("preview")[0].GetProperty("customerId").GetGuid());
        key=Guid.NewGuid().ToString();first=await Post($"/lenden/imports/{preview.GetProperty("id").GetGuid()}/commit",new{},key);retry=await Post($"/lenden/imports/{preview.GetProperty("id").GetGuid()}/commit",new{},key);Assert.True(first.IsSuccessStatusCode,await first.Content.ReadAsStringAsync());Assert.Equal(await first.Content.ReadAsStringAsync(),await retry.Content.ReadAsStringAsync());
    }
    [Fact] public async Task ImportRejectsAmbiguousContactsRepeatedReferencesAndStalePreviewAtomically()
    {
        var invalid=await ImportPreview("Wrong person,9000000002,,BAD-1,Phone,,50,2026-09-01,2026-10-20\nCustomer,9000000002,,SAME,Phone,,50,2026-09-01,2026-10-20\nCustomer,9000000002,,SAME,Phone,,50,2026-09-01,2026-10-20");Assert.Equal("Invalid",invalid.GetProperty("status").GetString());Assert.Equal(2,invalid.GetProperty("errors").GetArrayLength());Assert.False((await Post($"/lenden/imports/{invalid.GetProperty("id").GetGuid()}/commit",new{})).IsSuccessStatusCode);
        var preview=await ImportPreview("New customer,9000000077,,FIRST,Phone,,50,2026-09-01,2026-10-20\nCustomer,9000000002,,SECOND,Phone,,50,2026-09-01,2026-10-20");Assert.Equal("Validated",preview.GetProperty("status").GetString());
        await Id("/lenden/existing-dues",OldDue("SECOND",50));
        Assert.False((await Post($"/lenden/imports/{preview.GetProperty("id").GetGuid()}/commit",new{})).IsSuccessStatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Single(await db.Set<ExistingDue>().ToArrayAsync());Assert.False(await db.Set<Party>().AnyAsync(x=>x.Phone=="9000000077"));Assert.Single(await db.Set<LedgerEntry>().ToArrayAsync());
        await Id("/customers",new PartyRequest("Shared number","9000000002"));var ambiguous=await ImportPreview("Customer,9000000002,,THIRD,Phone,,50,2026-09-01,2026-10-20");Assert.Equal("Invalid",ambiguous.GetProperty("status").GetString());
    }
    [Fact] public async Task DocumentPromisesRemainIndependentAndFilteredExportIncludesContactsAndEscapesFormulas()
    {
        var first=await Id("/lenden/existing-dues",OldDue("=REGISTER-1",500));var second=await Id("/lenden/existing-dues",OldDue("REGISTER-2",700));
        await Id("/lenden/followups",new FollowUpRequest(branch,customer,"Promise","First phone",date.AddDays(20),ExistingDueId:first));
        await Id("/lenden/followups",new FollowUpRequest(branch,customer,"Promise","Second phone",date.AddDays(30),ExistingDueId:second));
        await Id("/lenden/followups",new FollowUpRequest(branch,customer,"Promise","Revised first phone",date.AddDays(25),ExistingDueId:first));
        var rows=(await Collections()).GetProperty("items").EnumerateArray().ToArray();Assert.Equal("2026-10-31",rows.Single(x=>x.GetProperty("id").GetGuid()==first).GetProperty("promiseDate").GetString());Assert.Equal("2026-11-05",rows.Single(x=>x.GetProperty("id").GetGuid()==second).GetProperty("promiseDate").GetString());
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}");Assert.Equal(JsonValueKind.Null,account.GetProperty("items")[0].GetProperty("promiseDate").ValueKind);
        var csv=await client.GetStringAsync($"/api/v1/lenden/collections/export?branchId={branch}&status=promised&from=2026-10-30&to=2026-10-31");Assert.Contains("9000000002",csv);Assert.Contains("Second phone",csv);Assert.Contains("'=REGISTER-1",csv);Assert.DoesNotContain("REGISTER-2",csv);Assert.Contains("500.00",csv);
        var history=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/collections/{first}/history?branchId={branch}&kind=existing-due");Assert.Equal(2,history.GetProperty("items").GetArrayLength());
        var other=await Id("/customers",new PartyRequest("Other","9000000090"));Assert.False((await Post("/lenden/followups",new FollowUpRequest(branch,other,"Promise","Wrong account",date.AddDays(20),ExistingDueId:first))).IsSuccessStatusCode);
    }
    [Fact] public async Task CustomerRegisterExportsAllAccountsAndA4CallSheetWithoutDoubleCountingDocuments()
    {
        await Id("/lenden/entries",new LenDenRequest(branch,customer,"Out",3500,1,date,"","Money given"));
        var other=await Id("/customers",new PartyRequest("Collection customer","9000000077",AlternatePhone:"9000000078"));
        await Id("/lenden/existing-dues",new ExistingDueRequest(branch,other,"DOWN-1","Phone down payment","",4500,date,date.AddDays(10)));
        var firstPage=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&filter=owed&pageSize=1");
        Assert.Equal(2,firstPage.GetProperty("totalItems").GetInt32());
        var csv=await client.GetStringAsync($"/api/v1/lenden/accounts/export?branchId={branch}&filter=owed");
        Assert.Contains("Collection customer",csv);Assert.Contains("Customer",csv);Assert.Contains("9000000078",csv);
        Assert.Contains("3500.00",csv);Assert.Contains("4500.00",csv);Assert.Contains("8000.00",csv);
        Assert.Contains("Open document total (reference only; do not add to Lena hai)",csv);
        Assert.Contains("16 Oct 2026",csv);
        var narrowed=await client.GetStringAsync($"/api/v1/lenden/accounts/export?branchId={branch}&filter=owed&search=9000000078");
        Assert.Contains("Collection customer",narrowed);Assert.DoesNotContain("3500.00",narrowed);
        var pdf=await client.GetByteArrayAsync($"/api/v1/lenden/accounts/call-sheet/pdf?branchId={branch}");
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        using var document=PdfReader.Open(new MemoryStream(pdf),PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount>=1);Assert.Equal(210,document.Pages[0].Width.Millimeter,1);Assert.Equal(297,document.Pages[0].Height.Millimeter,1);
        for(var i=0;i<32;i++)
        {
            var person=await Id("/customers",new PartyRequest("Call sheet person "+i,"8000000"+i.ToString("D3")));
            await Id("/lenden/entries",new LenDenRequest(branch,person,"Out",100+i,1,date,"","Follow-up loan"));
        }
        var longPdf=await client.GetByteArrayAsync($"/api/v1/lenden/accounts/call-sheet/pdf?branchId={branch}");
        using var pages=PdfReader.Open(new MemoryStream(longPdf),PdfDocumentOpenMode.Import);
        Assert.True(pages.PageCount>1);foreach(var sheet in pages.Pages){Assert.Equal(210,sheet.Width.Millimeter,1);Assert.Equal(297,sheet.Height.Millimeter,1);}
    }
    [Fact] public async Task RegisterExportShowsCustomerAddressAndOriginalDatesForOpenDebts()
    {
        var person=await Id("/customers",new PartyRequest("Dated register customer","9000000066",Address:"42 Market Road, Sarangpur"));
        await Id("/lenden/entries",new LenDenRequest(branch,person,"Out",1000,1,new DateOnly(2026,9,1),"HAND-1","Phone money given"));
        await Id("/lenden/entries",new LenDenRequest(branch,person,"Out",500,1,new DateOnly(2026,9,15),"HAND-2","Extra money given"));
        await Id("/lenden/entries",new LenDenRequest(branch,person,"In",600,1,date,"REPAY-1","Partial repayment"));
        await Id("/lenden/existing-dues",new ExistingDueRequest(branch,person,"RENO-DP","Reno down payment","Bajaj agreement",1200,new DateOnly(2026,8,20),date.AddDays(7)));
        var direct=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts/{person}?branchId={branch}");
        Assert.Equal(2100,direct.GetProperty("lenaHai").Amount());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/lenden/accounts/{person}?branchId={Guid.NewGuid()}")).StatusCode);
        var csv=await client.GetStringAsync($"/api/v1/lenden/accounts/export?branchId={branch}&search=9000000066");
        Assert.Contains("42 Market Road, Sarangpur",csv);Assert.Contains("20 Aug 2026",csv);Assert.Contains("01 Sep 2026",csv);Assert.Contains("15 Sep 2026",csv);
        Assert.Contains("Reno down payment",csv);Assert.Contains("Phone money given",csv);Assert.Contains("Extra money given",csv);
        Assert.Contains("INR 400.00",csv);Assert.Contains("INR 500.00",csv);Assert.DoesNotContain("REPAY-1",csv);
        var pdf=await client.GetByteArrayAsync($"/api/v1/lenden/accounts/call-sheet/pdf?branchId={branch}&search=9000000066");
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        using var document=PdfReader.Open(new MemoryStream(pdf),PdfDocumentOpenMode.Import);
        Assert.Equal(210,document.Pages[0].Width.Millimeter,1);
    }
    [Fact] public async Task CollectionAccessAndImportsEnforcePermissionsAndBranchScope()
    {
        var old=await Id("/lenden/existing-dues",OldDue());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/lenden/collections?branchId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post("/payments",new PaymentRequest(branch,await Id("/customers",new PartyRequest("Other","9000000080")),10,1,date,"","Wrong party",[],ExistingDueId:old))).StatusCode);
        var staff=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("collection-viewer","Viewer","collection-viewer-password-2026",["customers.credit.view"],[branch]));staff.EnsureSuccessStatusCode();var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("collection-viewer","collection-viewer-password-2026"));client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.True((await client.GetAsync($"/api/v1/lenden/collections?branchId={branch}")).IsSuccessStatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Post("/lenden/existing-dues",OldDue("NO"))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/v1/lenden/collections/export?branchId={branch}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/v1/lenden/accounts/export?branchId={branch}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/v1/lenden/accounts/call-sheet/pdf?branchId={branch}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/payments/allocate-account-credit",new AllocateCustomerCreditRequest(branch,customer))).StatusCode);
    }
    [Fact] public async Task ReviewedDueImportBelongsToItsReviewerAndRejectsChangedCustomerMatching()
    {
        var preview=await ImportPreview("Customer,9000000002,,ACTOR-1,Old phone,,100,2026-09-01,2026-10-20");var id=preview.GetProperty("id").GetGuid();var owner=client.DefaultRequestHeaders.Authorization;
        (await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("register-importer","Importer","register-importer-password-2026",["ledger.adjust","customers.manage","customers.credit.view"],[branch]))).EnsureSuccessStatusCode();
        var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("register-importer","register-importer-password-2026"));client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,(await Post($"/lenden/imports/{id}/commit",new{})).StatusCode);
        client.DefaultRequestHeaders.Authorization=owner;
        var update=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/customers/{customer}"){Content=JsonContent.Create(new PartyRequest("Renamed customer","9000000002"))};update.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());(await client.SendAsync(update)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post($"/lenden/imports/{id}/commit",new{})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Empty(await db.Set<ExistingDue>().ToArrayAsync());Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());
    }
    [Fact] public async Task IndependentLenDenMoneyCannotBeUsedAsAnOpeningDueReceipt()
    {
        var old=await Id("/lenden/existing-dues",OldDue(amount:500));var independent=await Id("/lenden/entries",new LenDenRequest(branch,customer,"In",500,1,date,"","Independent deposit"));
        Assert.False((await Post($"/payments/{independent}/allocations",new AllocationRequest(null,null,500,old))).IsSuccessStatusCode);
        Assert.False((await Post("/payments/allocate-account-credit",new AllocateCustomerCreditRequest(branch,customer))).IsSuccessStatusCode);
        var report=await Collections();Assert.Equal(500,report.GetProperty("totalOutstanding").Amount());Assert.Equal(0,report.GetProperty("unallocatedCredit").Amount());
        var reminder=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/collections/{customer}/reminder?branchId={branch}");Assert.Equal(500,reminder.GetProperty("amount").Amount());
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");Assert.Equal(500,account.GetProperty("tradeBalance").Amount());Assert.Equal(-500,account.GetProperty("independentBalance").Amount());
    }

}
