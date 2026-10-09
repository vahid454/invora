using Invora.Domain.Common;
namespace Invora.Domain.Modules.Retail;

public sealed class DeviceCorrection : Entity
{
    public Guid StockUnitId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ActorId { get; set; }
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}
