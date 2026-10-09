using Invora.Contracts.Common;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Customers;
public sealed class PartyService(RetailOperations r)
{
    private static string NormalizeAlternate(string? value,string primary){var phone=new string((value??"").Where(char.IsAsciiDigit).ToArray());RetailOperations.Check(phone.Length==0||phone.Length is >=7 and <=15,"Second phone must contain 7–15 digits.");RetailOperations.Check(phone.Length==0||(phone.Length==12&&phone.StartsWith("91")?phone[2..]:phone)!=(primary.Length==12&&primary.StartsWith("91")?primary[2..]:primary),"Use a different number for the second contact.");return phone;}
    private static string ViewPermission(PartyKind kind)=>kind==PartyKind.Customer?"customers.view":"supplier.view";
    private static string ManagePermission(PartyKind kind)=>kind==PartyKind.Customer?"customers.manage":"supplier.manage";
    public async Task<PageResponse<Party>> ListAsync(PartyKind kind,int page,int size,string? search,CancellationToken ct)
    {
        await r.PermissionAsync(ViewPermission(kind),ct);RetailOperations.Page(page,size);var q=r.Db.Set<Party>().AsNoTracking().Where(x=>x.Kind==kind && x.IsActive);
        if(!string.IsNullOrWhiteSpace(search)){
            var text=search.Trim().ToUpperInvariant();RetailOperations.Check(text.Length<=300,"Search must be at most 300 characters.");
            if(System.Text.RegularExpressions.Regex.IsMatch(text,@"^[+\d ()-]+$")){var digits=new string(text.Where(char.IsAsciiDigit).ToArray());var local=text.StartsWith("+91",StringComparison.Ordinal)&&digits.StartsWith("91",StringComparison.Ordinal)&&digits.Length>2?digits[2..]:digits.Length>10?digits[^10..]:digits;q=q.Where(x=>(x.Phone.Contains(local)||x.AlternatePhone.Contains(local)));}
            else foreach(var term in System.Text.RegularExpressions.Regex.Split(text,@"\s+").Where(x=>x.Length>0)){q=q.Where(x=>x.Name.ToUpper().Contains(term)||x.Phone.Contains(term)||x.AlternatePhone.Contains(term));}
        }
        var total=await q.LongCountAsync(ct);return new(await q.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct),page,size,total);
    }
    public async Task<object> SupplierAccountsAsync(Guid branch,int page,int size,string? search,string? filter,CancellationToken ct)
    {
        await r.BranchAsync(branch,"supplier.view",ct);RetailOperations.Page(page,size);
        var parties=r.Db.Set<Party>().AsNoTracking().Where(x=>x.Kind==PartyKind.Supplier&&x.IsActive);
        var text=(search??"").Trim().ToUpperInvariant();RetailOperations.Check(text.Length<=300,"Search must be at most 300 characters.");
        if(System.Text.RegularExpressions.Regex.IsMatch(text,@"^[+\d ()-]+$")){var digits=new string(text.Where(char.IsAsciiDigit).ToArray());if(digits.Length>10)digits=digits[^10..];parties=parties.Where(x=>x.Phone.Contains(digits)||x.AlternatePhone.Contains(digits));}
        else foreach(var term in System.Text.RegularExpressions.Regex.Split(text,@"\s+").Where(x=>x.Length>0))parties=parties.Where(x=>x.Name.ToUpper().Contains(term)||x.Phone.Contains(term)||x.AlternatePhone.Contains(term));
        var accounts=parties.Select(p=>new{p.Id,p.Name,p.Phone,p.AlternatePhone,p.Email,p.Address,p.StateCode,p.Gstin,p.CreditLimit,balance=r.Db.Set<LedgerEntry>().Where(e=>e.BranchId==branch&&e.PartyId==p.Id).Sum(e=>(decimal?)(e.Credit-e.Debit))??0});
        var totals=await accounts.Select(x=>x.balance).ToArrayAsync(ct);
        RetailOperations.Check(filter is null or "" or "pay" or "receive" or "settled","Choose a valid supplier balance filter.");
        if(filter=="pay")accounts=accounts.Where(x=>x.balance>0);if(filter=="receive")accounts=accounts.Where(x=>x.balance<0);if(filter=="settled")accounts=accounts.Where(x=>x.balance==0);
        return new{items=await accounts.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct),page,pageSize=size,totalItems=await accounts.LongCountAsync(ct),moneyToPay=totals.Where(x=>x>0).Sum(),moneyToReceive=totals.Where(x=>x<0).Sum(x=>-x)};
    }
    public Task<Guid> CreateAsync(PartyKind kind,PartyRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("party-create-"+kind,key,request,()=>CreateInternalAsync(kind,request,ct),ct);
    public async Task<Guid> CreateInternalAsync(PartyKind kind,PartyRequest request,CancellationToken ct){
        await r.PermissionAsync(ManagePermission(kind),ct);RetailOperations.Text(request.Name,"Name",200);RetailOperations.Text(request.Phone,"Phone",20);
        var phone=new string(request.Phone.Where(char.IsAsciiDigit).ToArray());RetailOperations.Check(phone.Length is >=7 and <=15,"Phone must contain 7–15 digits.");if(request.CreditLimit is not null)RetailOperations.Money(request.CreditLimit.Value);
        var p=new Party{Kind=kind,Name=request.Name.Trim(),Phone=phone,AlternatePhone=NormalizeAlternate(request.AlternatePhone,phone),Email=request.Email??"",Address=request.Address??"",StateCode=GstSupply.ContactState(request.StateCode,request.Gstin),Gstin=(request.Gstin??"").Trim().ToUpperInvariant(),CreditLimit=request.CreditLimit};r.Db.Add(p);r.Audit("PARTY_CREATED",p.Id,request);return p.Id;
    }
    public Task<Guid> UpdateAsync(Guid id,PartyKind kind,PartyRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("party-update",key,new{id,kind,request},async()=>{
        await r.PermissionAsync(ManagePermission(kind),ct);var party=await r.PartyAsync(id,kind,ct);RetailOperations.Text(request.Name,"Name",200);var phone=new string((request.Phone??"").Where(char.IsAsciiDigit).ToArray());RetailOperations.Check(phone.Length is >=7 and <=15,"Phone must contain 7–15 digits.");if(request.CreditLimit is not null)RetailOperations.Money(request.CreditLimit.Value);var before=new{party.Name,party.Phone,party.Email,party.Address,party.Gstin,party.CreditLimit};party.Name=request.Name.Trim();party.Phone=phone;party.AlternatePhone=NormalizeAlternate(request.AlternatePhone,phone);party.Email=request.Email??"";party.Address=request.Address??"";party.StateCode=GstSupply.ContactState(request.StateCode,request.Gstin);party.Gstin=(request.Gstin??"").Trim().ToUpperInvariant();party.CreditLimit=request.CreditLimit;r.Audit("PARTY_UPDATED",id,new{before,after=request});return id;
    },ct);
    public async Task<object> DetailAsync(Guid id,PartyKind kind,Guid branch,CancellationToken ct)
    {
        await r.BranchAsync(branch,ViewPermission(kind),ct);var p=await r.PartyAsync(id,kind,ct);decimal? balance=null;
        if(kind==PartyKind.Supplier || r.Can("customers.credit.view"))balance=await r.BalanceAsync(branch,id,ct)*(kind==PartyKind.Supplier?-1:1);
        var notes=await r.Db.Set<PartyNote>().AsNoTracking().Where(x=>x.PartyId==id && x.BranchId==branch).OrderByDescending(x=>x.CreatedAtUtc).Take(50).ToArrayAsync(ct);
        var trade=balance is null?(decimal?)null:await r.TradingBalanceAsync(branch,id,ct)*(kind==PartyKind.Supplier?-1:1);
        return new{party=p,balance,tradeBalance=trade,independentBalance=balance-trade,notes};
    }
    public Task<Guid> NoteAsync(Guid partyId,PartyKind kind,NoteRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("party-note",key,new{partyId,request},async()=>{
        await r.BranchAsync(request.BranchId,ManagePermission(kind),ct);await r.PartyAsync(partyId,kind,ct);RetailOperations.Text(request.Text,"Note");var note=new PartyNote{PartyId=partyId,BranchId=request.BranchId,Text=request.Text,ActorId=r.Actor};r.Db.Add(note);r.Audit("PARTY_NOTE_ADDED",partyId);return note.Id;
    },ct);
    public async Task<object> LedgerAsync(Guid party,PartyKind kind,Guid branch,int page,int size,CancellationToken ct,string? scope=null)
    {
        await r.BranchAsync(branch,kind==PartyKind.Customer?"customers.credit.view":"supplier.view",ct);await r.PartyAsync(party,kind,ct);RetailOperations.Page(page,size);
        await using var snapshot=await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var q=r.Db.Set<LedgerEntry>().AsNoTracking().Where(x=>x.BranchId==branch && x.PartyId==party);RetailOperations.Check(scope is null or "" or "all" or "trade" or "independent","Choose a valid statement account.");if(scope=="trade")q=q.Where(x=>!x.Kind.StartsWith("LenDen"));if(scope=="independent")q=q.Where(x=>x.Kind.StartsWith("LenDen"));var total=await q.LongCountAsync(ct);
        var entries=await q.OrderBy(x=>x.BusinessDate).ThenBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct);
        var opening=await q.OrderBy(x=>x.BusinessDate).ThenBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Take((page-1)*size).SumAsync(x=>x.Debit-x.Credit,ct);
        return new{items=entries,page,pageSize=size,totalItems=total,totalPages=(total+size-1)/size,openingBalance=opening,balance=await q.SumAsync(x=>x.Debit-x.Credit,ct)};
    }
}
