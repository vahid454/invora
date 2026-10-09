using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Expenses;
public sealed class ExpenseService(RetailOperations r)
{
    public Task<Guid> CreateAsync(ExpenseRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("expense-create",key,request,async()=>{
        await r.BranchAsync(request.BranchId,"expenses.create",ct);RetailOperations.Text(request.Category,"Category",100);RetailOperations.Text(request.Description,"Description");RetailOperations.Money(request.Amount,true);RetailOperations.Check(Enum.IsDefined(typeof(PaymentMethod),request.Method),"Unknown method.");var expense=new Expense{BranchId=request.BranchId,Category=request.Category,Description=request.Description,Amount=request.Amount,Method=(PaymentMethod)request.Method,BusinessDate=request.BusinessDate,ActorId=r.Actor};r.Db.Add(expense);r.Audit("EXPENSE_CREATED",expense.Id,new{expense.Amount,expense.Category});return expense.Id;
    },ct);
    public Task<Guid> ReverseAsync(Guid id,ReasonRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("expense-reverse",key,new{id,request},async()=>{
        var original=await r.Db.Set<Expense>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Expense not found.");await r.BranchAsync(original.BranchId,"expenses.reverse",ct);RetailOperations.Text(request.Reason,"Reason");RetailOperations.Check(original.ReversesExpenseId==null && !await r.Db.Set<Expense>().AnyAsync(x=>x.ReversesExpenseId==id,ct),"Expense already reversed.");var reversal=new Expense{BranchId=original.BranchId,Category=original.Category,Description=request.Reason,Amount=original.Amount,Method=original.Method,BusinessDate=await r.TodayAsync(ct),ReversesExpenseId=id,ActorId=r.Actor};r.Db.Add(reversal);r.Audit("EXPENSE_REVERSED",id,new{ReversalId=reversal.Id,request.Reason});return reversal.Id;
    },ct);
    public async Task<object> ListAsync(Guid branch,int page,int size,CancellationToken ct,string? search=null){await r.BranchAsync(branch,"expenses.view",ct);RetailOperations.Page(page,size);var q=r.Db.Set<Expense>().AsNoTracking().Where(x=>x.BranchId==branch);if(!string.IsNullOrWhiteSpace(search)){var term=search.ToUpperInvariant();q=q.Where(x=>x.Description.ToUpper().Contains(term)||x.Category.ToUpper().Contains(term));}return new{items=await q.OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).Select(x=>new{x.Id,x.Category,x.Description,x.Amount,x.Method,x.BusinessDate,x.ReversesExpenseId,reversed=r.Db.Set<Expense>().Any(y=>y.ReversesExpenseId==x.Id)}).ToArrayAsync(ct),page,pageSize=size,totalItems=await q.LongCountAsync(ct)};}
}
