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
}
