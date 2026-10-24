using Invora.Recovery;

namespace Invora.Recovery.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new RecoveryWindow());
    }
}

internal sealed class RecoveryWindow : Form
{
    private readonly TextBox selectedFile = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose your .invora recovery file" };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button restore = new() { Text = "Verify and prepare recovery", AutoSize = true };
    private string? source;

    internal RecoveryWindow()
    {
        Text = "Invora · Prepare recovery";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 300);
        Size = new Size(650, 330);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(20) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var introduction = new Label { AutoSize = true, Text = "Open one password-protected shop backup from your Mac, PC or Backblaze.\nThe file will be verified and prepared in a new folder. Existing shop data stays untouched.", Margin = new Padding(0, 0, 0, 15) };
        layout.Controls.Add(introduction, 0, 0);
        layout.SetColumnSpan(introduction, 2);
        layout.Controls.Add(selectedFile, 0, 1);
        var choose = new Button { Text = "Choose file…", AutoSize = true };
        choose.Click += (_, _) => ChooseFile();
        layout.Controls.Add(choose, 1, 1);
        layout.Controls.Add(new Label { Text = "Recovery password", AutoSize = true, Margin = new Padding(0, 15, 0, 4) }, 0, 2);
        layout.Controls.Add(password, 0, 3);
        restore.Click += async (_, _) => await PrepareAsync();
        layout.Controls.Add(restore, 1, 3);
        var note = new Label { AutoSize = true, Text = "Keep the password outside this computer. Invora will not save it here.", Margin = new Padding(0, 14, 0, 0) };
        layout.Controls.Add(note, 0, 4);
        layout.SetColumnSpan(note, 2);
        Controls.Add(layout);
    }

    private void ChooseFile()
    {
        using var picker = new OpenFileDialog { Filter = "Invora recovery (*.invora)|*.invora", Title = "Choose an Invora recovery file", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        source = picker.FileName;
        selectedFile.Text = source;
    }

    private async Task PrepareAsync()
    {
        if (source is null || password.Text.Length < 16)
        {
            MessageBox.Show(this, "Choose a recovery file and enter its full recovery password.", "Invora", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var recoveryRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Invora", "Recovery");
        var destination = Path.Combine(recoveryRoot, "Verified " + DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
        if (Directory.Exists(destination))
        {
            MessageBox.Show(this, "That recovery folder already exists. Choose another location.", "Invora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        restore.Enabled = false;
        var secret = password.Text;
        password.Clear();
        try
        {
            await Task.Run(() => PortableRecovery.Extract(source, destination, secret));
            MessageBox.Show(this, "Recovery file verified and prepared at:\n" + destination + "\n\nRestore into a separate database and check the records before switching your shop to it.", "Invora", MessageBoxButtons.OK, MessageBoxIcon.Information);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(destination) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            MessageBox.Show(this, "Recovery could not be prepared. Existing shop data was not changed.\n\n" + error.Message, "Invora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { restore.Enabled = true; }
    }
}
