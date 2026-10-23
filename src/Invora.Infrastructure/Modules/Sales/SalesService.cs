using Invora.Infrastructure.Modules.UsedDevices;
using Invora.Contracts.Common;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Modules.Taxes;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Customers;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Payments;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Sales;
public sealed class SalesService(RetailOperations r,CatalogService catalog,StockService stock,PaymentService payments,UsedDeviceService used)
{
    private static SaleLineRequest[] PricedLines(SaleRequest request)
    {
        RetailOperations.Money(request.CheckoutDiscount);
        if(request.CheckoutDiscount==0)return request.Items;
        RetailOperations.Check(request.Items.All(x=>x.TaxInclusive),"Checkout discount requires tax-inclusive item prices. For tax-exclusive prices, use each item's discount field.");
        var available=request.Items.Select(x=>{
            RetailOperations.Money(x.UnitPrice);RetailOperations.Money(x.Discount);
            RetailOperations.Check(x.Quantity>0,"Item quantity must be positive.");
            var gross=InvoiceCalculator.Round(x.Quantity*x.UnitPrice);
            RetailOperations.Check(x.Discount<=gross,"Item discount exceeds its price.");
            return gross-x.Discount;
        }).ToArray();
        var capacity=available.Sum();
        RetailOperations.Check(request.CheckoutDiscount<=capacity,"Checkout discount exceeds the remaining item value.");
        var left=request.CheckoutDiscount;var priced=new SaleLineRequest[request.Items.Length];
        for(var i=0;i<priced.Length;i++){
            var share=i==priced.Length-1?left:capacity==0?0:Math.Min(available[i],InvoiceCalculator.Round(left*available[i]/capacity));
            priced[i]=request.Items[i] with{Discount=request.Items[i].Discount+share};
            left-=share;capacity-=available[i];
        }
        RetailOperations.Check(left==0,"Checkout discount did not reconcile.");
        return priced;
    }
    private static decimal ResolveFinance(FinanceDetails? finance,decimal total,decimal paidOrExchanged,decimal checkoutDiscount)
    {
        if(finance is null)return 0;
        RetailOperations.Check(FinanceProviderNames.Canonical(finance.Provider) is not null,"Choose a supported finance provider.");
        RetailOperations.Check((finance.Reference??"").Trim().Length<=100,"Finance application reference must fit within 100 characters.");
        RetailOperations.Check(finance.ExpectedAmount>=0 && finance.ExpectedAmount<=99999999999999m && decimal.Round(finance.ExpectedAmount,2)==finance.ExpectedAmount,
            "Expected finance amount must be zero or a nonnegative amount with up to two decimal places.");
        if(finance.CustomerDownPayment is decimal agreed){RetailOperations.Money(agreed);RetailOperations.Check(agreed-checkoutDiscount>=paidOrExchanged && agreed-checkoutDiscount<=total,"Agreed down payment after checkout discount must cover money received now and cannot exceed the final invoice total.");}
        var assigned=finance.CustomerDownPayment is decimal downPayment?total-(downPayment-checkoutDiscount):finance.ExpectedAmount==0?total-paidOrExchanged:finance.ExpectedAmount;
        RetailOperations.Check(assigned<=total-paidOrExchanged,"Expected finance amount exceeds the unpaid invoice balance. It is not money received.");
        if(finance.CustomerDownPayment is not null && finance.ExpectedAmount>0)RetailOperations.Check(finance.ExpectedAmount==assigned,"Finance amount and agreed customer down payment must add up to the invoice total.");
        return assigned;
    }
    public async Task<object> QuoteAsync(SaleRequest request,CancellationToken ct){await r.BranchAsync(request.BranchId,"sales.create",ct);var party=await r.PartyAsync(request.CustomerId,PartyKind.Customer,ct);await CustomerSafetyService.EnsureCanDealAsync(r,party.Id,ct);RetailOperations.Check(request.Items is {Length:>0 and <=200},"Items are required.");if(request.CheckoutDiscount>0)await r.PermissionAsync("sales.discount",ct);var priced=PricedLines(request);var settings=await r.SettingsAsync(ct);var supply=GstSupply.Resolve(request,party,settings);if(request.Exchange is not null){RetailOperations.Check(request.Exchange.BranchId==request.BranchId&&request.Exchange.CustomerId==request.CustomerId,"Exchange must use this customer and branch.");await used.ValidateAsync(request.Exchange,ct);}foreach(var payment in request.Payments){RetailOperations.Money(payment.Amount,true);RetailOperations.Check(Enum.IsDefined(typeof(PaymentMethod),payment.Method),"Choose a valid payment method.");}var lines=new List<Invora.Domain.Modules.Taxes.RetailTaxTotal>();foreach(var line in priced){RetailOperations.Money(line.UnitPrice);RetailOperations.Money(line.Discount);var product=await catalog.ProductAsync(line.ProductVariantId,ct);lines.Add(RetailTaxCalculator.Calculate(line.Quantity,line.UnitPrice,line.Discount,settings.GstRegistered&&!settings.CompositionDealer?product.Tax.Rate:0,settings.GstRegistered&&!settings.CompositionDealer?product.Tax.CessRate:0,line.TaxInclusive,supply.Interstate));}RetailOperations.Check(request.Payments.Sum(x=>x.Amount)+(request.Exchange?.Amount??0)<=lines.Sum(x=>x.Total),"Payments and exchange credit exceed the bill total.");var total=lines.Sum(x=>x.Total);var received=request.Payments.Sum(x=>x.Amount);var exchange=request.Exchange?.Amount??0;var finance=ResolveFinance(request.Finance,total,received+exchange,request.CheckoutDiscount);return new{supplyStateCode=supply.State,supplyStateName=GstSupply.Label(supply.State),interstate=supply.Interstate,taxable=lines.Sum(x=>x.Taxable),cgst=lines.Sum(x=>x.Cgst),sgst=lines.Sum(x=>x.Sgst),igst=lines.Sum(x=>x.Igst),cess=lines.Sum(x=>x.Cess),beforeDiscountTotal=total+request.CheckoutDiscount,checkoutDiscount=request.CheckoutDiscount,total,received,exchange,financePending=finance,customerDownPayment=total-finance,outstanding=total-received-exchange-finance};}
    public Task<DocumentView> DraftAsync(SaleRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("sale-draft",key,request,async()=>{
        await r.BranchAsync(request.BranchId,"sales.create",ct);var party=await r.PartyAsync(request.CustomerId,PartyKind.Customer,ct);await CustomerSafetyService.EnsureCanDealAsync(r,party.Id,ct);var supply=GstSupply.Resolve(request,party,await r.SettingsAsync(ct));request=request with{SupplyStateCode=supply.State,Interstate=supply.Interstate};RetailOperations.Check(request.Items is {Length:>0 and <=200} && request.Payments is {Length:<=20},"Sale requires 1–200 items and payment components.");
        var sale=new Sale{BranchId=request.BranchId,CustomerId=request.CustomerId,ActorId=r.Actor,BusinessDate=request.BusinessDate,DueDate=request.DueDate,RequestJson=RetailOperations.Json(request)};r.Db.Add(sale);r.Audit("SALE_DRAFT",sale.Id);return new DocumentView(sale.Id,"",sale.Status,0);
    },ct);
    public Task<DocumentView> CompleteAsync(Guid id,string key,CancellationToken ct)=>r.ExecuteAsync("sale-complete",key,new{id},async()=>{
        var sale=await r.Db.Set<Sale>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Sale not found.");await r.BranchAsync(sale.BranchId,"sales.create",ct);if(sale.Status=="Completed")return new DocumentView(id,sale.Number,sale.Status,sale.Total,Reference:sale.Reference);RetailOperations.Check(sale.Status=="Draft","Sale cannot be completed.");
        var req=RetailOperations.Read<SaleRequest>(sale.RequestJson);var customer=await r.PartyAsync(sale.CustomerId,PartyKind.Customer,ct);await CustomerSafetyService.EnsureCanDealAsync(r,customer.Id,ct);var seller=await r.SettingsAsync(ct);var business=await r.Db.Businesses.SingleAsync(ct);
        var supply=GstSupply.Resolve(req,customer,seller);var interstate=supply.Interstate;
        var invoiceLines=new List<InvoiceLine>();sale.Number=await r.NumberAsync(sale.BranchId,"INV",sale.BusinessDate,ct);sale.Reference=await r.InvoiceReferenceAsync(sale.BranchId,"INV",sale.BusinessDate,sale.Number,ct);
        if(req.CheckoutDiscount>0)await r.PermissionAsync("sales.discount",ct);
        foreach(var line in PricedLines(req))
        {
            RetailOperations.Money(line.UnitPrice);RetailOperations.Money(line.Discount);if(line.Discount>0)await r.PermissionAsync("sales.discount",ct);
            var p=await catalog.ProductAsync(line.ProductVariantId,ct);var rate=seller.GstRegistered && !seller.CompositionDealer?p.Tax.Rate:0;var cess=seller.GstRegistered && !seller.CompositionDealer?p.Tax.CessRate:0;var total=RetailTaxCalculator.Calculate(line.Quantity,line.UnitPrice,line.Discount,rate,cess,line.TaxInclusive,interstate);
            if(total.Total<p.Variant.SellingPrice*line.Quantity)await r.PermissionAsync("sales.discount",ct);
            var item=new SaleItem{SaleId=sale.Id,ProductVariantId=p.Variant.Id,StockUnitId=line.StockUnitId,Description=CatalogService.Description(p.Model,p.Variant),Hsn=p.Model.Hsn,Quantity=line.Quantity,UnitPrice=line.UnitPrice,Discount=line.Discount,Rate=rate,CessRate=cess,Taxable=total.Taxable,Cgst=total.Cgst,Sgst=total.Sgst,Igst=total.Igst,Cess=total.Cess,Total=total.Total};r.Db.Add(item);string[] identities=[];var condition="New";
            if(p.Variant.Serialized)
            {
                RetailOperations.Check(line.Quantity==1 && line.StockUnitId is not null,"Serialized lines require exactly one specific unit.");var unit=await r.Db.Set<StockUnit>().SingleOrDefaultAsync(x=>x.Id==line.StockUnitId && x.BranchId==sale.BranchId && x.ProductVariantId==p.Variant.Id,ct)??throw new DomainException("NOT_FOUND","Device not found.");RetailOperations.Check(unit.Status==DeviceStatus.InStock,"Device is already sold or unavailable.","INVENTORY_UNAVAILABLE");condition=unit.Condition;unit.Status=DeviceStatus.Sold;unit.ActiveSaleItemId=item.Id;item.Cost=unit.Cost;identities=await r.Db.Set<UnitIdentifier>().Where(x=>x.StockUnitId==unit.Id).OrderBy(x=>x.Slot).Select(x=>x.Slot+": "+x.Value).ToArrayAsync(ct);r.Movement(sale.BranchId,p.Variant.Id,-1,"Sale",unit.Id,sale:sale.Id);
            }
            else
            {
                RetailOperations.Check(line.StockUnitId is null,"Quantity item cannot reference a device.");var used=await stock.ConsumeAsync(sale.BranchId,p.Variant.Id,line.Quantity,ct);foreach(var layer in used){r.Db.Add(new SaleCostAllocation{SaleItemId=item.Id,StockCostLayerId=layer.Layer.Id,Quantity=layer.Quantity});item.Cost+=layer.Quantity*layer.Layer.UnitCost;}r.Movement(sale.BranchId,p.Variant.Id,-line.Quantity,"Sale",sale:sale.Id);
            }
            item.IdentifiersJson=RetailOperations.Json(identities);sale.Total+=item.Total;sale.Taxable+=item.Taxable;sale.Cgst+=item.Cgst;sale.Sgst+=item.Sgst;sale.Igst+=item.Igst;sale.Cess+=item.Cess;sale.Cost+=item.Cost;
            invoiceLines.Add(new(item.Description,item.Hsn,identities,item.Quantity,item.UnitPrice,item.Discount,item.Rate,item.CessRate,item.Taxable,item.Cgst,item.Sgst,item.Igst,item.Cess,item.Total,condition=="New"?p.Model.WarrantyMonths:0,condition,WarrantyPolicy.ForDevice(condition,p.Model.WarrantyMonths)));
        }
        RetailOperations.Money(sale.Total,true);foreach(var component in req.Payments)RetailOperations.Money(component.Amount,true);var initial=req.Payments.Sum(x=>x.Amount);var exchange=req.Exchange?.Amount??0;RetailOperations.Money(exchange);RetailOperations.Check(initial+exchange<=sale.Total,"Initial payments exceed invoice total.");var financeAmount=ResolveFinance(req.Finance,sale.Total,initial+exchange,req.CheckoutDiscount);var customerDue=sale.Total-initial-exchange-financeAmount;
        if(customerDue>0){await r.PermissionAsync("customers.credit.view",ct);RetailOperations.Check(sale.DueDate is not null,"Credit sales require a due date.");if(customer.CreditLimit is decimal limit){var outstanding=await r.Db.Set<LedgerEntry>().Where(x=>x.PartyId==customer.Id).SumAsync(x=>x.Debit-x.Credit,ct);RetailOperations.Check(outstanding+customerDue<=limit,"Customer credit limit exceeded.","CREDIT_LIMIT_EXCEEDED");}}
        sale.InvoiceJson=RetailOperations.Json(new InvoiceSnapshot(sale.Number,sale.BusinessDate,sale.DueDate,business.TradeName,seller,customer.Name,customer.Phone,customer.Address,customer.Gstin,customer.StateCode,invoiceLines.ToArray(),sale.Taxable,sale.Cgst,sale.Sgst,sale.Igst,sale.Cess,sale.Total,interstate,seller.GstRegistered?(seller.CompositionDealer?"BILL OF SUPPLY":"TAX INVOICE"):"RETAIL INVOICE",sale.Reference,supply.State,TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone)),customer.AlternatePhone,Finance:req.Finance is null?null:req.Finance with{Provider=FinanceProviderNames.Canonical(req.Finance.Provider)!,Reference=(req.Finance.Reference??"").Trim(),ExpectedAmount=financeAmount,CustomerDownPayment=sale.Total-financeAmount}));sale.Status="Completed";
        r.Ledger(sale.BranchId,sale.CustomerId,sale.BusinessDate,"Sale",sale.Total,0,sale:sale.Id);if(financeAmount>0)r.Ledger(sale.BranchId,sale.CustomerId,sale.BusinessDate,"FinanceAssigned",0,financeAmount,sale:sale.Id,note:FinanceProviderNames.Canonical(req.Finance!.Provider)!);await r.Db.SaveChangesAsync(ct);
        foreach(var pay in req.Payments)await payments.ApplyAsync(new(sale.BranchId,sale.CustomerId,pay.Amount,pay.Method,sale.BusinessDate,pay.Reference,pay.Note,[new(sale.Id,null,pay.Amount)]),ct);
        if(req.Exchange is not null){RetailOperations.Check(req.Exchange.BranchId==sale.BranchId && req.Exchange.CustomerId==sale.CustomerId,"Exchange must use the sale's branch and customer.");await used.AcquireInternalAsync(req.Exchange with{ExchangeSaleId=sale.Id},ct);}
        r.Audit("SALE_COMPLETED",sale.Id,new{sale.Number,sale.Total});return new DocumentView(sale.Id,sale.Number,sale.Status,sale.Total,Reference:sale.Reference);
    },ct);
    private static string Provider(Sale sale)
    {
        if(sale.Status=="Draft"||sale.InvoiceJson=="{}")return "";
        var finance=RetailOperations.Read<InvoiceSnapshot>(sale.InvoiceJson).Finance;
        return finance is null?"":FinanceProviderNames.Canonical(finance.Provider)??finance.Provider;
    }
    public async Task<PageResponse<DocumentView>> ListAsync(Guid branch,int page,int size,CancellationToken ct,string? search=null,DateOnly? from=null,DateOnly? to=null,string? finance=null)
    {
        await r.BranchAsync(branch,"sales.view",ct);RetailOperations.Page(page,size);
        RetailOperations.Check(from is null||to is null||from<=to,"From date must be on or before To date.");
        var q=from sale in r.Db.Set<Sale>().AsNoTracking() join party in r.Db.Set<Party>() on sale.CustomerId equals party.Id where sale.BranchId==branch select new{sale,party};
        if(from is not null)q=q.Where(x=>x.sale.BusinessDate>=from.Value);
        if(to is not null)q=q.Where(x=>x.sale.BusinessDate<=to.Value);
        var text=(search??"").Trim().ToUpperInvariant();RetailOperations.Check(text.Length<=300,"Search must be at most 300 characters.");
        if(text.Length>0){
            if(System.Text.RegularExpressions.Regex.IsMatch(text,@"^[+\d ()-]+$")){var digits=new string(text.Where(char.IsAsciiDigit).ToArray());if(digits.Length>10)digits=digits[^10..];q=q.Where(x=>x.sale.Number.Contains(text)||x.sale.Reference.ToUpper().Contains(text)||x.party.Phone.Contains(digits)||x.party.AlternatePhone.Contains(digits));}
            else foreach(var term in System.Text.RegularExpressions.Regex.Split(text,@"\s+").Where(x=>x.Length>0))q=q.Where(x=>x.sale.Number.ToUpper().Contains(term)||x.sale.Reference.ToUpper().Contains(term)||x.party.Name.ToUpper().Contains(term)||x.party.Phone.Contains(term)||x.party.AlternatePhone.Contains(term));
        }
        var provider=(finance??"").Trim();RetailOperations.Check(provider==""||provider.Equals("none",StringComparison.OrdinalIgnoreCase)||FinanceProviderNames.Canonical(provider) is not null,"Choose a supported finance filter.");
        var ordered=q.OrderByDescending(x=>x.sale.CreatedAtUtc).ThenBy(x=>x.sale.Id);
        var candidates=provider.Length==0?await ordered.Skip((page-1)*size).Take(size).Select(x=>new{x.sale,x.party.Name,x.party.Phone,x.party.AlternatePhone}).ToArrayAsync(ct):await ordered.Take(20001).Select(x=>new{x.sale,x.party.Name,x.party.Phone,x.party.AlternatePhone}).ToArrayAsync(ct);
        if(provider.Length>0)RetailOperations.Check(candidates.Length<=20000,"Finance filter exceeds 20,000 invoices. Narrow the date or customer search.");
        var selected=provider.Length==0?candidates:candidates.Where(x=>provider.Equals("none",StringComparison.OrdinalIgnoreCase)?Provider(x.sale)=="":Provider(x.sale)==FinanceProviderNames.Canonical(provider)).Skip((page-1)*size).Take(size).ToArray();
        var rows=new List<DocumentView>(selected.Length);
        foreach(var x in selected)rows.Add(new DocumentView(x.sale.Id,x.sale.Number,x.sale.Status,x.sale.Total,x.sale.CustomerId,x.sale.Reference,x.Name,x.Phone,x.AlternatePhone,x.sale.BusinessDate,Provider(x.sale),await r.FinanceAssignedAsync(x.sale.Id,ct)));
        var count=provider.Length==0?await q.LongCountAsync(ct):candidates.LongCount(x=>provider.Equals("none",StringComparison.OrdinalIgnoreCase)?Provider(x.sale)=="":Provider(x.sale)==FinanceProviderNames.Canonical(provider));
        return new(rows.ToArray(),page,size,count);
    }
    public async Task<Sale> AuthorizedAsync(Guid id,string permission,CancellationToken ct)
    {
        var sale=await r.Db.Set<Sale>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Sale not found.");await r.BranchAsync(sale.BranchId,permission,ct);return sale;
    }
    public async Task<InvoiceSnapshot> InvoiceAsync(Guid id,CancellationToken ct){var sale=await AuthorizedAsync(id,"sales.view",ct);RetailOperations.Check(sale.Status!="Draft","Draft has no invoice.");return RetailOperations.Read<InvoiceSnapshot>(sale.InvoiceJson);}
    public async Task<object> DetailAsync(Guid id,CancellationToken ct)
    {
        var sale=await AuthorizedAsync(id,"sales.view",ct);var canProfit=r.Can("profit.view");var items=await r.Db.Set<SaleItem>().AsNoTracking().Where(x=>x.SaleId==id).Select(x=>new{x.Id,x.ProductVariantId,x.StockUnitId,x.Description,x.Hsn,x.IdentifiersJson,x.Quantity,returnedQuantity=r.Db.Set<SaleReturnItem>().Where(y=>y.SaleItemId==x.Id).Sum(y=>(int?)y.Quantity)??0,x.UnitPrice,x.Discount,x.Taxable,x.Cgst,x.Sgst,x.Igst,x.Cess,x.Total,cost=canProfit?(decimal?)x.Cost:null}).ToArrayAsync(ct);var assigned=await r.FinanceAssignedAsync(id,ct);var finance=sale.Status=="Draft"?null:RetailOperations.Read<InvoiceSnapshot>(sale.InvoiceJson).Finance;return new{sale.Id,sale.BranchId,sale.CustomerId,sale.Number,sale.Reference,sale.Status,sale.BusinessDate,sale.DueDate,sale.Total,sale.Taxable,sale.Cgst,sale.Sgst,sale.Igst,sale.Cess,cost=r.Can("profit.view")?(decimal?)sale.Cost:null,items,outstanding=await r.SaleDueAsync(sale,ct),paid=await r.AllocatedAsync(id,null,ct),financeProvider=finance is null?"":FinanceProviderNames.Canonical(finance.Provider)??finance.Provider,financeAssigned=assigned,financeReference=finance?.Reference??"",returns=await r.Db.Set<SaleReturn>().AsNoTracking().Where(x=>x.SaleId==id).ToArrayAsync(ct)};
    }
}
