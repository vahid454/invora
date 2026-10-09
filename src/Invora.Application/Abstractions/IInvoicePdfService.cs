namespace Invora.Application.Abstractions;

public interface IInvoicePdfService
{
    Task<byte[]> RenderAsync(Guid invoiceId, CancellationToken cancellationToken);
}
