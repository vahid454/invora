namespace Invora.Infrastructure.Identity;

public sealed class AuthOptions
{
    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "Invora";
    public string Audience { get; set; } = "Invora.Web";
    public string BrowserOrigin { get; set; } = "";
    public string BootstrapKey { get; set; } = "";
}
