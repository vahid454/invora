using System.Security.Cryptography;
using System.Text.Json;
using Invora.Domain.Modules.Licensing;

try
{
    if(args.Length<1)throw new ArgumentException("Use keygen or issue. See docs/commercial-release.md.");
    var values=new Dictionary<string,string>(StringComparer.Ordinal);
    for(var i=1;i<args.Length;i+=2){if(i+1>=args.Length||!args[i].StartsWith("--",StringComparison.Ordinal)||!values.TryAdd(args[i],args[i+1]))throw new ArgumentException("Options require unique --name value pairs.");}
    string Required(string name)=>values.TryGetValue(name,out var value)&&!string.IsNullOrWhiteSpace(value)?value:throw new ArgumentException(name+" is required.");
    var privatePath=Path.GetFullPath(Required("--private"));
    if(args[0]=="keygen")
    {
        var policy=Path.GetFullPath(Required("--policy"));if(File.Exists(privatePath)||File.Exists(policy))throw new IOException("Refusing to replace existing signing keys or verification policy.");
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);Directory.CreateDirectory(Path.GetDirectoryName(policy)!);
        using var rsa=RSA.Create(3072);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(Path.GetDirectoryName(privatePath)!,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var privateOptions=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write};if(!OperatingSystem.IsWindows())privateOptions.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using(var stream=new FileStream(privatePath,privateOptions))using(var writer=new StreamWriter(stream))writer.Write(rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(policy,JsonSerializer.Serialize(new{required=true,publicKey=Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Created vendor signing key and public release policy. Keep the private key offline; never include it in a customer release.");
    }
    else if(args[0]=="issue")
    {
        using var rsa=RSA.Create();rsa.ImportFromPem(File.ReadAllText(privatePath));if(rsa.KeySize<3072)throw new ArgumentException("Use a 3072-bit or stronger signing key.");
        var starts=DateOnly.ParseExact(values.GetValueOrDefault("--starts",DateTime.UtcNow.ToString("yyyy-MM-dd")),"yyyy-MM-dd");var expires=DateOnly.ParseExact(Required("--expires"),"yyyy-MM-dd");
        var claims=new ShopLicenseClaims(1,Guid.NewGuid(),Guid.Parse(Required("--shop-id")),Required("--customer"),"Retail",starts,expires,int.Parse(values.GetValueOrDefault("--grace-days","7")));
        var output=Path.GetFullPath(Required("--output"));Directory.CreateDirectory(Path.GetDirectoryName(output)!);using var file=new StreamWriter(new FileStream(output,FileMode.CreateNew,FileAccess.Write));file.WriteLine(ShopLicenseCodec.Sign(claims,rsa));
        Console.WriteLine("Issued Retail licence through "+expires.ToString("yyyy-MM-dd")+". Deliver the .license file to this shop owner after payment; no vendor secret is in it.");
    }
    else throw new ArgumentException("Unknown command. Use keygen or issue.");
    return 0;
}
catch(Exception error) when(error is ArgumentException or IOException or FormatException or CryptographicException)
{Console.Error.WriteLine(error.Message);return 1;}
