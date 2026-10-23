using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Invora.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Uri? origin;
        if (args.Length == 2 && args[0] == "--url" && NavigationPolicy.TryLocalOrigin(args[1], out origin))
        {
            Application.Run(new ShopWindow(origin!));
            return;
        }
        if (args.Length == 2 && args[0] == "--hosted-url" && NavigationPolicy.TryHostedOrigin(args[1], out origin))
        {
            Application.Run(new ShopWindow(origin!));
            return;
        }
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--configure-hosted"))
        {
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Invora Desktop", "hosted-url.txt");
            var configured = File.Exists(settings) ? File.ReadAllText(settings).Trim() : "";
            if (args.Length == 1 || !NavigationPolicy.TryHostedOrigin(configured, out origin))
            {
                configured = PromptForHostedOrigin(configured);
                if (!NavigationPolicy.TryHostedOrigin(configured, out origin)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
                File.WriteAllText(settings, origin!.AbsoluteUri);
            }
            Application.Run(new ShopWindow(origin!));
            return;
        }
        MessageBox.Show("Use the desktop app for a configured HTTPS shop, or open Start Invora for a local shop.", "Invora", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string PromptForHostedOrigin(string current)
    {
        using var dialog = new Form { Text = "Connect to your Invora shop", Width = 530, Height = 190, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var label = new Label { Left = 20, Top = 18, Width = 475, Height = 40, Text = "Enter the HTTPS address supplied for your shop. Your records remain on that shop server." };
        var input = new TextBox { Left = 20, Top = 65, Width = 475, Text = current, PlaceholderText = "https://your-shop.example.com/" };
        var connect = new Button { Text = "Connect", Left = 360, Top = 105, Width = 135, DialogResult = DialogResult.OK };
        dialog.Controls.AddRange([label, input, connect]); dialog.AcceptButton = connect;
        while (dialog.ShowDialog() == DialogResult.OK)
        {
            var value = input.Text.Trim();
            if (NavigationPolicy.TryHostedOrigin(value, out _)) return value;
            MessageBox.Show(dialog, "Enter an HTTPS shop address with no extra path, username, or query.", "Invora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return "";
    }
}

internal sealed class ShopWindow : Form
{
    private readonly Uri origin;
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel status = new("Opening your shop…");
    private bool ready;

    internal ShopWindow(Uri origin)
    {
        this.origin = origin;
        Text = "Invora · Your shop";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 850);
        MinimumSize = new Size(800, 600);
        BackColor = Color.FromArgb(246, 246, 239);
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 4, 8, 4) };
        AddButton(toolbar, "Back", () => { if (ready && web.CanGoBack) web.GoBack(); });
        AddButton(toolbar, "Reload", () => { if (ready) web.Reload(); });
        AddButton(toolbar, "Open in browser", () => OpenExternal(origin));
        AddButton(toolbar, "Downloads", () => { if (ready) web.CoreWebView2.OpenDefaultDownloadDialog(); });
        var footer = new StatusStrip(); footer.Items.Add(status);
        Controls.Add(web); Controls.Add(toolbar); Controls.Add(footer);
        Shown += async (_, _) => await InitializeAsync();
    }

    private static void AddButton(ToolStrip strip, string title, Action action)
    {
        var button = new ToolStripButton(title); button.Click += (_, _) => action(); strip.Items.Add(button);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Invora Desktop", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            web.CoreWebView2.Settings.IsWebMessageEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (NavigationPolicy.IsInternal(origin, e.Uri)) return;
                e.Cancel = true;
                if (e.IsUserInitiated && NavigationPolicy.IsExternal(e.Uri)) OpenExternal(new Uri(e.Uri));
            };
            web.CoreWebView2.FrameNavigationStarting += (_, e) =>
            {
                if (!NavigationPolicy.IsInternal(origin, e.Uri) && e.Uri != "about:blank") e.Cancel = true;
            };
            web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!e.IsUserInitiated) return;
                if (NavigationPolicy.IsInternal(origin, e.Uri)) web.CoreWebView2.Navigate(e.Uri);
                else if (NavigationPolicy.IsExternal(e.Uri)) OpenExternal(new Uri(e.Uri));
            };
            web.CoreWebView2.DownloadStarting += (_, e) =>
            {
                if (!NavigationPolicy.IsInternal(origin, e.DownloadOperation.Uri)) { e.Cancel = true; return; }
                using var dialog = new SaveFileDialog { FileName = Path.GetFileName(e.ResultFilePath), OverwritePrompt = true, Title = "Save from Invora" };
                if (dialog.ShowDialog(this) != DialogResult.OK) { e.Cancel = true; return; }
                var destination = dialog.FileName;
                e.ResultFilePath = destination;
                status.Text = "Downloading " + Path.GetFileName(destination) + "…";
                e.DownloadOperation.StateChanged += (_, _) =>
                {
                    status.Text = e.DownloadOperation.State switch
                    {
                        CoreWebView2DownloadState.Completed => "Saved " + Path.GetFileName(destination),
                        CoreWebView2DownloadState.Interrupted => "Download interrupted. Please try again.",
                        _ => "Downloading…"
                    };
                };
            };
            web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                status.Text = e.IsSuccess ?
                    (origin.Scheme == "https" ? "Your shop · Records stored on your shop server" : "Your shop · Records stored on this computer") :
                    (origin.Scheme == "https" ? "Shop unavailable. Check your internet connection and shop address, then choose Reload." : "Shop unavailable. Start Docker Desktop and Start Invora, then choose Reload.");
            };
            web.CoreWebView2.ProcessFailed += (_, _) => status.Text = "The app window needs to reload. Your saved records are retained. Choose Reload or reopen Start Invora.";
            ready = true;
            web.CoreWebView2.Navigate(origin.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            status.Text = "Microsoft WebView2 Runtime is required for the desktop window.";
            var result = MessageBox.Show(this, "Install Microsoft's free Evergreen WebView2 Runtime, then open Start Invora again.\n\nOpen Microsoft's download page now? You can also choose Open in browser to use your shop immediately.", "Invora desktop window", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes) OpenExternal(new Uri("https://developer.microsoft.com/microsoft-edge/webview2/"));
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            status.Text = "The desktop window could not start. Use Open in browser, or close Invora and try again.";
            MessageBox.Show(this, status.Text, "Invora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenExternal(Uri address)
    {
        try { Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, "No application could open this link. Use your browser or configured phone/message application.", "Invora"); }
    }
}
