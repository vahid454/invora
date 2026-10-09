using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

try
{
    var verify=args.Length>0&&args[0]=="--verify-package";
    using var package=verify&&args.Length==2?File.OpenRead(args[1]):Assembly.GetExecutingAssembly().GetManifestResourceStream("Invora.Release.zip")??throw new IOException("Installer release resource is missing.");
    using var archive=new ZipArchive(package,ZipArchiveMode.Read);
    var files=Validate(archive);
    if(verify){Console.WriteLine("Verified release paths and SHA-256 hashes: "+files.Count+" files.");return 0;}
    if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Run InvoraSetup.exe on Windows. --verify-package can be used on other platforms.");
    Console.WriteLine("Invora · inventory, billing and shop accounts\n");
    Console.WriteLine("Install and start Docker Desktop with Linux containers first. This installer does not reset your database or document volumes.");
    var destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Invora");
    if(args.Length==0&&OperatingSystem.IsWindows())
    {
        using var remembered=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Invora");
        if(remembered?.GetValue("InstallDirectory") is string previous&&File.Exists(Path.Combine(previous,".env")))destination=Path.GetFullPath(previous);
        else if(File.Exists(@"C:\Invora\.env"))destination=@"C:\Invora";
    }
    if(args.Length==2&&args[0]=="--install-dir")destination=Path.GetFullPath(args[1]);else if(args.Length!=0)throw new ArgumentException("Use --install-dir C:\\Invora to update an existing installation.");
    // Stopped containers also retain the original folder; daily shutdown must not lose it.
    if(args.Length==0)
    {
        var dirs=ReadDockerOutput("ps","--all","--filter","label=com.docker.compose.project=invora","--format","{{.Label \"com.docker.compose.project.working_dir\"}}")
            .Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(dirs.Length>1)throw new IOException("Multiple Invora directories were found. Choose the existing directory with --install-dir.");
        if(dirs.Length==1&&File.Exists(Path.Combine(dirs[0],".env")))destination=Path.GetFullPath(dirs[0]);
    }
    var data=ReadDockerOutput("volume","ls","--filter","label=com.docker.compose.project=invora","--format","{{.Name}}");
    if(!string.IsNullOrWhiteSpace(data)&&!File.Exists(Path.Combine(destination,".env")))throw new IOException("Existing Invora data was found. Use --install-dir with the original application folder and its private .env; do not create new credentials for existing volumes.");
    Console.WriteLine("Application folder: "+destination);
    if(File.Exists(Path.Combine(destination,".env")))Console.WriteLine("Existing private configuration will be preserved. Take a database and documents backup before updating.");
    Console.Write("Press Enter to install/update, or close this window to cancel: ");
    if(Console.ReadLine() is null)throw new IOException("Installation cancelled before any application files were changed.");
    foreach(var entry in files)
    {
        var target=Path.GetFullPath(Path.Combine(destination,entry.FullName));if(!target.StartsWith(Path.GetFullPath(destination)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe release path.");
        var parent=Path.GetDirectoryName(target)!;for(var current=new DirectoryInfo(parent);current is not null;current=current.Parent)if(current.Exists&&current.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Install into a real private folder, not a linked folder.");
        Directory.CreateDirectory(parent);var temporary=target+".invora-installing";using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){using var input=entry.Open();input.CopyTo(output);}File.Move(temporary,target,true);
    }
    Console.WriteLine("Release files verified and installed. Creating your desktop shortcuts…");
    if(OperatingSystem.IsWindows()){using var remembered=Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Invora");remembered.SetValue("InstallDirectory",destination);}
    var shortcuts=RunPowerShell(destination,"Create-InvoraShortcuts.ps1",false);if(shortcuts!=0)Console.WriteLine("Shortcuts were not created. You can use Start-Invora.cmd from the application folder.");
    Console.WriteLine("Starting the shop application. First build can take several minutes. Keep this window open.");
    var startup=RunPowerShell(destination,"Start-Invora.ps1",true);
    if(startup!=0)
    {
        Console.Error.WriteLine("Invora could not finish starting. Read the error above, then run Check-Invora.cmd in your application folder. Keep .env and the existing Docker volumes.");
        WaitToClose();
    }
    return startup;
}
catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or PlatformNotSupportedException or System.ComponentModel.Win32Exception or CryptographicException)
{
    Console.Error.WriteLine("Installation stopped: "+error.Message);
    if(args.Length==0||args[0]!="--verify-package")WaitToClose();
    return 1;
}

static void WaitToClose()
{
    if(OperatingSystem.IsWindows()&&!Console.IsInputRedirected){Console.WriteLine("Press Enter to close.");Console.ReadLine();}
}

static string ReadDockerOutput(params string[] arguments)
{
    var start=new ProcessStartInfo("docker"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
    foreach(var argument in arguments)start.ArgumentList.Add(argument);
    Process docker;
    try{docker=Process.Start(start)??throw new IOException("Docker could not start.");}
    catch(System.ComponentModel.Win32Exception){throw new IOException("Install and start Docker Desktop with Linux containers, then run the installer again.");}
    using(docker)
    {
        var output=docker.StandardOutput.ReadToEndAsync();
        var errors=docker.StandardError.ReadToEndAsync();
        if(!docker.WaitForExit(30000))
        {
            try{docker.Kill(entireProcessTree:true);}catch(InvalidOperationException){}
            throw new IOException("Docker did not respond within 30 seconds. Start Docker Desktop, wait for it to finish starting, then retry.");
        }
        // Drain both streams without echoing Docker configuration or machine-specific errors.
        Task.WhenAll(output,errors).GetAwaiter().GetResult();
        if(docker.ExitCode!=0)throw new IOException("Start Docker Desktop, then run the installer again.");
        return output.GetAwaiter().GetResult();
    }
}

static List<ZipArchiveEntry> Validate(ZipArchive archive)
{
    if(archive.Entries.Count>2000)throw new InvalidDataException("Release contains too many files.");
    var manifest=archive.GetEntry("release-sha256.txt")??throw new InvalidDataException("Release manifest is missing.");
    if(manifest.Length>500000)throw new InvalidDataException("Release manifest is too large.");
    using var reader=new StreamReader(manifest.Open());var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    while(reader.ReadLine() is string line){var parts=line.Split("  ",2,StringSplitOptions.None);if(parts.Length!=2||parts[0].Length!=64||!parts[0].All(Uri.IsHexDigit)||!expected.TryAdd(parts[1],parts[0]))throw new InvalidDataException("Invalid release manifest.");}
    var files=new List<ZipArchiveEntry>();long total=0;
    foreach(var entry in archive.Entries.Where(x=>x.FullName!="release-sha256.txt"))
    {
        var path=entry.FullName;total+=entry.Length;
        var maxFileSize=path=="desktop/Invora.Desktop.exe"?160000000:32000000;
        if(path.Contains('\\')||path.Contains(':')||path.StartsWith('/')||path.Split('/').Any(part=>part is "" or "." or "..")||path.Split('/').Any(part=>part.Equals(".vendor-private",StringComparison.OrdinalIgnoreCase)||part.Equals(".private-backups",StringComparison.OrdinalIgnoreCase))||(Path.GetFileName(path).StartsWith(".env",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(path).Equals(".env.example",StringComparison.OrdinalIgnoreCase))||Path.GetExtension(path).Equals(".license",StringComparison.OrdinalIgnoreCase)||entry.Length>maxFileSize||total>512000000||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Unsafe release entry.");
        if(!expected.Remove(path,out var digest))throw new InvalidDataException("Unexpected or duplicate release file.");
        using var stream=entry.Open();if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Release file hash differs: "+path);files.Add(entry);
    }
    if(expected.Count!=0||!files.Any(x=>x.FullName=="Start-Invora.cmd")||!files.Any(x=>x.FullName=="scripts/Start-Invora.ps1"))throw new InvalidDataException("Release files are missing.");return files;
}
static int RunPowerShell(string destination,string script,bool update)
{
    var start=new ProcessStartInfo("powershell.exe"){WorkingDirectory=destination,UseShellExecute=false};foreach(var option in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(destination,"scripts",script)})start.ArgumentList.Add(option);
    if(script=="Create-InvoraShortcuts.ps1"){start.ArgumentList.Add("-InstallDirectory");start.ArgumentList.Add(destination);}else if(update)start.ArgumentList.Add("-Update");
    using var child=Process.Start(start)??throw new IOException("PowerShell could not start.");child.WaitForExit();return child.ExitCode;
}
