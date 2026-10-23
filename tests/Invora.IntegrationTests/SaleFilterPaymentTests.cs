using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Common;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    [Fact]
    public async Task FinanceCheckoutDiscountClearsCustomerDownPaymentWithoutInventingAReceipt()
    {
        await Purchase([new(cable,1,100)]);
        var request=new SaleRequest(branch,customer,date,null,false,[new(cable,1,24500m,TaxInclusive:true)],
            [new(2100m,1)],Finance:new FinanceDetails("BAJAJ","BAJAJ-DISCOUNT-CASE",CustomerDownPayment:2400m),CheckoutDiscount:300m);
        var quoteResponse=await Post("/sales/quote",request);quoteResponse.EnsureSuccessStatusCode();
        var quote=await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(24500m,quote.GetProperty("beforeDiscountTotal").Amount());
        Assert.Equal(300m,quote.GetProperty("checkoutDiscount").Amount());
        Assert.Equal(24200m,quote.GetProperty("total").Amount());
        Assert.Equal(2100m,quote.GetProperty("received").Amount());
        Assert.Equal(22100m,quote.GetProperty("financePending").Amount());
        Assert.Equal(0m,quote.GetProperty("outstanding").Amount());
        var draft=await Document("/sales",request);var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}");
        Assert.Equal(24200m,detail.GetProperty("total").Amount());
        Assert.Equal(2100m,detail.GetProperty("paid").Amount());
        Assert.Equal(22100m,detail.GetProperty("financeAssigned").Amount());
        Assert.Equal(0m,detail.GetProperty("outstanding").Amount());
        var invoice=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;
        Assert.Equal(300m,Assert.Single(invoice.Lines).Discount);
        Assert.Equal(24200m,invoice.Total);
        Assert.Equal(22100m,invoice.Finance?.ExpectedAmount);
        Assert.Equal(2100m,invoice.Finance?.CustomerDownPayment);
        Assert.Equal(0m,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}")).GetProperty("tradeBalance").Amount());
        Assert.Equal(0m,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/collections?branchId={branch}")).GetProperty("totalOutstanding").Amount());
        var pdf=await client.GetByteArrayAsync($"/api/v1/invoices/{sale.Id}/pdf");Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Equal(2100m,Assert.Single(await db.Set<Payment>().ToArrayAsync()).Amount);
        Assert.Equal(2100m,await db.Set<PaymentAllocation>().Where(x=>x.SaleId==sale.Id).SumAsync(x=>x.Amount));
        Assert.Equal(22100m,await db.Set<LedgerEntry>().Where(x=>x.SaleId==sale.Id&&x.Kind=="FinanceAssigned").SumAsync(x=>x.Credit));
    }

    [Fact]
    public async Task CheckoutDiscountRejectsInvalidAmountsAndTaxExclusiveLines()
    {
        await Purchase([new(cable,1,100)]);
        var request=new SaleRequest(branch,customer,date,null,false,[new(cable,1,1000m,TaxInclusive:true)],[],CheckoutDiscount:100m);
        foreach(var discount in new[]{-1m,1000.01m,0.001m})
            Assert.Equal(HttpStatusCode.BadRequest,(await Post("/sales/quote",request with{CheckoutDiscount=discount})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/sales/quote",request with{Items=[new(cable,1,1000m,TaxInclusive:false)]})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/sales/quote",request with{Finance=new FinanceDetails("BAJAJ","REF",CustomerDownPayment:200m),Payments=[new(150m,1)]})).StatusCode);
    }

    [Fact]
    public async Task TaxInclusiveCheckoutDiscountRecalculatesGstWithoutChangingActualCash()
    {
        await ConfigureGst();
        var tax=await Id("/tax-rates",new TaxRequest("Discount GST 18",18));
        var item=await Id("/products",new ProductRequest("Discount GST item","Generic","Electronics","8518",tax,"DISC-GST-18",null,"","","",false,1180,1180));
        await Purchase([new(item,1,500)]);
        var request=new SaleRequest(branch,customer,date,null,false,[new(item,1,1180m,TaxInclusive:true)],[new(1000m,2)],CheckoutDiscount:180m);
        var response=await Post("/sales/quote",request);response.EnsureSuccessStatusCode();
        var quote=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1180m,quote.GetProperty("beforeDiscountTotal").Amount());
        Assert.Equal(1000m,quote.GetProperty("total").Amount());
        Assert.Equal(1000m,quote.GetProperty("taxable").Amount()+quote.GetProperty("cgst").Amount()+quote.GetProperty("sgst").Amount());
        var draft=await Document("/sales",request);var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var invoice=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;
        Assert.Equal(180m,Assert.Single(invoice.Lines).Discount);
        Assert.Equal(1000m,invoice.Total);
        Assert.Equal(0m,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}")).GetProperty("outstanding").Amount());
        await using var scope=factory.Services.CreateAsyncScope();
        Assert.Equal(1000m,Assert.Single(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<Payment>().ToArrayAsync()).Amount);
    }
    [Fact]
    public async Task InvoiceBusinessDateFiltersIncludeBoundariesAndMatchSearchPaginationAndExport()
    {
        await Purchase([new(cable,4,100)]);
        var person=await Id("/customers",new PartyRequest("Date Filter Customer","9000000061",AlternatePhone:"9000000062"));
        var invoices=new List<DocumentView>();
        foreach(var day in new[]{date.AddDays(-1),date,date.AddDays(1),date.AddDays(2)})
        {
            var draft=await Document("/sales",new SaleRequest(branch,person,day,null,false,[new(cable,1,200)],[new(200,1)]));
            invoices.Add(await Document($"/sales/{draft.Id}/complete",new{}));
        }
        var query=$"branchId={branch}&from={date:yyyy-MM-dd}&to={date.AddDays(1):yyyy-MM-dd}&search=9000000062";
        var first=(await client.GetFromJsonAsync<PageResponse<DocumentView>>("/api/v1/sales?"+query+"&pageSize=1"))!;
        var second=(await client.GetFromJsonAsync<PageResponse<DocumentView>>("/api/v1/sales?"+query+"&pageSize=1&page=2"))!;
        Assert.Equal(2,first.TotalItems);Assert.Equal(2,second.TotalItems);
        Assert.Equal(new[]{invoices[1].Id,invoices[2].Id}.Order(),first.Items.Concat(second.Items).Select(x=>x.Id).Order());
        var csv=await client.GetStringAsync("/api/v1/exports/sales?"+query);
        Assert.Contains(invoices[1].Number,csv);Assert.Contains(invoices[2].Number,csv);
        Assert.DoesNotContain(invoices[0].Number,csv);Assert.DoesNotContain(invoices[3].Number,csv);
        var single=(await client.GetFromJsonAsync<PageResponse<DocumentView>>($"/api/v1/sales?branchId={branch}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"))!;
        Assert.Equal(invoices[1].Id,Assert.Single(single.Items).Id);
        var openEnd=(await client.GetFromJsonAsync<PageResponse<DocumentView>>($"/api/v1/sales?branchId={branch}&to={date:yyyy-MM-dd}"))!;Assert.Equal(2,openEnd.TotalItems);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/sales?branchId={branch}&from=2026-10-20&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/exports/sales?branchId={branch}&from=2026-10-20&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/sales?branchId={branch}&from=not-a-date")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/sales?branchId={Guid.NewGuid()}&from={date:yyyy-MM-dd}")).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SplitCashUpiCardPaymentsMatchQuoteInvoiceLedgerAndRetryWithoutDuplicates(bool full)
    {
        await Purchase([new(cable,1,100)]);
        PaymentComponent[] components=full?[new(50.10m,1),new(70.20m,2),new(80.15m,3)]:[new(20.10m,1),new(30.20m,2),new(40.15m,3)];
        var received=components.Sum(x=>x.Amount);var due=200.45m-received;
        var request=new SaleRequest(branch,customer,date,full?null:date.AddDays(7),false,[new(cable,1,200.45m)],components);
        var response=await Post("/sales/quote",request);response.EnsureSuccessStatusCode();var quote=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(received,quote.GetProperty("received").Amount());Assert.Equal(due,quote.GetProperty("outstanding").Amount());
        var draft=await Document("/sales",request);var key=Guid.NewGuid().ToString();var sale=await Document($"/sales/{draft.Id}/complete",new{},key);
        Assert.Equal(sale,await Document($"/sales/{draft.Id}/complete",new{},key));
        var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}");Assert.Equal(received,detail.GetProperty("paid").Amount());Assert.Equal(due,detail.GetProperty("outstanding").Amount());
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");Assert.Equal(due,account.GetProperty("tradeBalance").Amount());Assert.Equal(0,account.GetProperty("independentBalance").Amount());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        var payments=await db.Set<Payment>().OrderBy(x=>x.Method).ToArrayAsync();Assert.Equal(3,payments.Length);
        for(var i=0;i<components.Length;i++){Assert.Equal(components[i].Amount,payments[i].Amount);Assert.Equal(components[i].Method,(int)payments[i].Method);Assert.Equal("In",payments[i].Direction);}
        Assert.Equal(received,await db.Set<PaymentAllocation>().Where(x=>x.SaleId==sale.Id).SumAsync(x=>x.Amount));
        Assert.Equal(1,await db.Set<InventoryMovement>().CountAsync(x=>x.SaleId==sale.Id));
    }
    [Fact]
    public async Task FinanceArrangementAppearsOnInvoiceButDoesNotCountAsReceivedMoney()
    {
        await Purchase([new(cable,1,100)]);
        var request=new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(cable,1,200.45m)],
            [new(20.10m,1),new(30.20m,2),new(40.15m,3)],Finance:new FinanceDetails("Bajaj","BAJAJ-APPLICATION-123",100));
        var quoteResponse=await Post("/sales/quote",request);quoteResponse.EnsureSuccessStatusCode();
        var quote=(await quoteResponse.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(90.45m,quote.GetProperty("received").Amount());Assert.Equal(100m,quote.GetProperty("financePending").Amount());Assert.Equal(10m,quote.GetProperty("outstanding").Amount());
        var excessive=await Post("/sales/quote",request with{Finance=new FinanceDetails("Bajaj","BAJAJ-APPLICATION-123",111)});
        Assert.False(excessive.IsSuccessStatusCode);
        var draft=await Document("/sales",request);var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var invoice=await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}");
        Assert.Equal("BAJAJ",invoice!.Finance?.Provider);Assert.Equal("BAJAJ-APPLICATION-123",invoice.Finance?.Reference);
        Assert.Equal(100,invoice.Finance?.ExpectedAmount);
        var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}");
        Assert.Equal(10m,detail.GetProperty("outstanding").Amount());Assert.Equal(100m,detail.GetProperty("financeAssigned").Amount());Assert.Equal(90.45m,detail.GetProperty("paid").Amount());
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");Assert.Equal(10m,account.GetProperty("tradeBalance").Amount());
        var filtered=await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales?branchId={branch}&finance=BAJAJ");Assert.Equal(1,filtered.GetProperty("totalItems").GetInt64());Assert.Equal("BAJAJ",filtered.GetProperty("items")[0].GetProperty("financeProvider").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/sales?branchId={branch}&finance=unknown")).StatusCode);
        var pdf=await client.GetByteArrayAsync($"/api/v1/invoices/{sale.Id}/pdf");Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Equal(3,await db.Set<Payment>().CountAsync());
    }
    [Fact]
    public async Task FinanceProviderDefaultsToUnpaidBalanceWithoutCreatingCashOrCustomerDebt()
    {
        await Purchase([new(cable,1,100)]);
        var request=new SaleRequest(branch,customer,date,null,false,[new(cable,1,200.45m)],
            [new(50.45m,1)],Finance:new FinanceDetails("BAJAJ","APPLICATION-PENDING"));
        var quoteResponse=await Post("/sales/quote",request);
        quoteResponse.EnsureSuccessStatusCode();
        var quote=await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m,quote.GetProperty("outstanding").Amount());Assert.Equal(150m,quote.GetProperty("financePending").Amount());
        var draft=await Document("/sales",request);
        var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var invoice=await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}");
        Assert.Equal("BAJAJ",invoice!.Finance?.Provider);
        Assert.Equal(150m,invoice.Finance?.ExpectedAmount);
        var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}");
        Assert.Equal(0m,detail.GetProperty("outstanding").Amount());Assert.Equal(150m,detail.GetProperty("financeAssigned").Amount());
        Assert.Equal(50.45m,detail.GetProperty("paid").Amount());
        var pdf=await client.GetByteArrayAsync($"/api/v1/invoices/{sale.Id}/pdf");
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
    }

    [Fact]
    public async Task FinancedSaleKeepsOnlyUnpaidDownPaymentWithCustomerAndReversesAssignmentOnReturn()
    {
        await Purchase([new(cable,2,100)]);
        var request=new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(cable,2,5000)],
            [new(1000m,1)],Finance:new FinanceDetails("BAJAJ","AGREEMENT-2000",CustomerDownPayment:2000m));
        Assert.False((await Post("/sales/quote",request with{Finance=request.Finance! with{CustomerDownPayment=500m}})).IsSuccessStatusCode);
        Assert.False((await Post("/sales/quote",request with{Finance=request.Finance! with{CustomerDownPayment=10001m}})).IsSuccessStatusCode);
        var quoteResponse=await Post("/sales/quote",request);quoteResponse.EnsureSuccessStatusCode();
        var quote=await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8000m,quote.GetProperty("financePending").Amount());Assert.Equal(1000m,quote.GetProperty("outstanding").Amount());
        var draft=await Document("/sales",request);var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var invoice=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;
        Assert.Equal(8000m,invoice.Finance?.ExpectedAmount);Assert.Equal(2000m,invoice.Finance?.CustomerDownPayment);
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");
        Assert.Equal(1000m,account.GetProperty("tradeBalance").Amount());
        var register=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&filter=owed");
        Assert.Equal(1000m,register.GetProperty("items")[0].GetProperty("lenaHai").Amount());
        Assert.Equal(1000m,register.GetProperty("items")[0].GetProperty("tradeBalance").Amount());
        var registerCsv=await client.GetStringAsync($"/api/v1/lenden/accounts/export?branchId={branch}&filter=owed");
        Assert.Contains("1000.00",registerCsv);
        var collections=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/collections?branchId={branch}");
        Assert.Equal(1000m,collections.GetProperty("totalOutstanding").Amount());
        var csv=await client.GetStringAsync($"/api/v1/exports/sales?branchId={branch}&finance=BAJAJ");
        Assert.Contains(sale.Number,csv);Assert.Contains("BAJAJ",csv);Assert.Contains("8000",csv);
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
            Assert.Single(await db.Set<Payment>().ToArrayAsync());
            Assert.Equal(8000m,await db.Set<LedgerEntry>().Where(x=>x.SaleId==sale.Id&&x.Kind=="FinanceAssigned").SumAsync(x=>x.Credit));
        }
        Guid itemId;
        await using(var scope=factory.Services.CreateAsyncScope())
            itemId=(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<SaleItem>().SingleAsync(x=>x.SaleId==sale.Id)).Id;
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("One item returned",date,[new(itemId,1,"Restock")]));
        var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}");
        Assert.Equal(4000m,detail.GetProperty("financeAssigned").Amount());Assert.Equal(0m,detail.GetProperty("outstanding").Amount());
        account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");
        Assert.Equal(0m,account.GetProperty("tradeBalance").Amount());
    }
}
