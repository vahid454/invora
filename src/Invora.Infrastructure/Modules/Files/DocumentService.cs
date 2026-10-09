using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Invora.Infrastructure.Modules.Files;
public sealed class DocumentService(RetailOperations r,IConfiguration configuration)
{
    private string Root=>Path.GetFullPath(configuration["Files:Root"]??Path.Combine(AppContext.BaseDirectory,"private-files"));
    private async Task<Guid> AuthorizeAsync(Guid? purchase,Guid? expense,bool write,CancellationToken ct)
    {
        RetailOperations.Check((purchase==null)!=(expense==null),"Choose exactly one parent document.");Guid branch;
        if(purchase is Guid p){branch=await r.Db.Set<Purchase>().Where(x=>x.Id==p).Select(x=>(Guid?)x.BranchId).SingleOrDefaultAsync(ct)??throw new DomainException("NOT_FOUND","Purchase not found.");await r.BranchAsync(branch,write?"purchase.create":"purchase.view",ct);await r.PermissionAsync("inventory.cost.view",ct);}
        else{branch=await r.Db.Set<Expense>().Where(x=>x.Id==expense).Select(x=>(Guid?)x.BranchId).SingleOrDefaultAsync(ct)??throw new DomainException("NOT_FOUND","Expense not found.");await r.BranchAsync(branch,write?"expenses.create":"expenses.view",ct);}return branch;
    }
    public async Task<object> UploadAsync(FileUploadRequest request,string key,CancellationToken ct)
    {
        string? written=null;
        try{return await r.ExecuteAsync<object>("document-upload",key,request,async()=>{
            var branch=await AuthorizeAsync(request.PurchaseId,request.ExpenseId,true,ct);RetailOperations.Text(request.Name,"File name",200);RetailOperations.Check(request.Base64.Length<=7000000,"File exceeds 5 MB.");byte[] bytes;try{bytes=Convert.FromBase64String(request.Base64);}catch(FormatException){throw new DomainException("VALIDATION_FAILED","File encoding is invalid.");}RetailOperations.Check(bytes.Length is >0 and <=5242880,"File exceeds 5 MB.");
            var mime=bytes.AsSpan().StartsWith("%PDF-"u8)?"application/pdf":bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})?"image/png":bytes.Length>=3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255?"image/jpeg":"";RetailOperations.Check(mime!="","Only PDF, PNG and JPEG documents are accepted.");
            var file=new DocumentFile{BranchId=branch,PurchaseId=request.PurchaseId,ExpenseId=request.ExpenseId,Name=Path.GetFileName(request.Name),MimeType=mime,Size=bytes.Length,StorageKey=Guid.NewGuid().ToString("N")};Directory.CreateDirectory(Root);written=Path.Combine(Root,file.StorageKey);await File.WriteAllBytesAsync(written,bytes,ct);r.Db.Add(file);r.Audit("DOCUMENT_ATTACHED",file.Id,new{file.PurchaseId,file.ExpenseId,file.Name,file.Size});return new{file.Id,file.Name,file.MimeType,file.Size};
        },ct);}catch{if(written is not null)File.Delete(written);throw;}
    }
    public async Task<object> ListAsync(Guid? purchase,Guid? expense,CancellationToken ct){await AuthorizeAsync(purchase,expense,false,ct);return await r.Db.Set<DocumentFile>().AsNoTracking().Where(x=>purchase!=null?x.PurchaseId==purchase:x.ExpenseId==expense).OrderBy(x=>x.CreatedAtUtc).Select(x=>new{x.Id,x.Name,x.MimeType,x.Size,x.CreatedAtUtc}).ToArrayAsync(ct);}
    public async Task<(byte[] Bytes,string Mime,string Name)> DownloadAsync(Guid id,CancellationToken ct){var file=await r.Db.Set<DocumentFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Document not found.");await AuthorizeAsync(file.PurchaseId,file.ExpenseId,false,ct);var path=Path.Combine(Root,file.StorageKey);if(!File.Exists(path))throw new DomainException("NOT_FOUND","Document content is unavailable.");return(await File.ReadAllBytesAsync(path,ct),file.MimeType,file.Name);}
}
