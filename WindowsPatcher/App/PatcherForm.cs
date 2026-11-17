using MelonPatcher.Core;

namespace MelonPatcher.App;

internal sealed class PatcherForm : Form
{
    private readonly TextBox input = new(), output = new(), payload = new(), unity = new();
    private readonly TextBox java = new(), dexPatcher = new(), buildTools = new(), keystore = new();
    private readonly TextBox alias = new(), storePassword = new(), keyPassword = new(), log = new();
    private readonly Button patch = new() { Text = "Patch, align, sign and export", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private CancellationTokenSource? cancellation;

    public PatcherForm()
    {
        Text = "MelonLoader Legacy APK Patcher (experimental 0.4.1)";
        Width = 950;
        Height = 800;
        MinimumSize = new Size(750, 650);
        StartPosition = FormStartPosition.CenterScreen;
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 3,
            RowCount = 0, Padding = new Padding(12) };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        Controls.Add(main);
        var warning = new Label { Text = "EXPERIMENTAL LEGACY 0.4.1 — NOT MELONLOADER 0.7.3. " +
            "Requires a separately built compatible Android payload. Re-signing changes the app signature and may require uninstalling the original app, which can erase saves. Back up first.",
            AutoSize = true, MaximumSize = new Size(870, 0), ForeColor = Color.DarkRed, Margin = new Padding(0, 0, 0, 12) };
        AddWide(main, warning);
        AddPath(main, "Original APK", input, false, "Android APK|*.apk");
        AddOutput(main);
        AddPath(main, "Legacy payload folder", payload, true);
        AddPath(main, "Matching Unity managed DLLs", unity, true);
        AddPath(main, "Java executable", java, false, "Java executable|java.exe|Programs|*.exe");
        AddPath(main, "DexPatcher JAR", dexPatcher, false, "Java archive|*.jar");
        AddPath(main, "Android build-tools folder", buildTools, true);
        AddPath(main, "Signing keystore", keystore, false, "Keystore|*.jks;*.keystore;*.p12;*.pfx|All files|*.*");
        AddText(main, "Key alias", alias);
        storePassword.UseSystemPasswordChar = true;
        keyPassword.UseSystemPasswordChar = true;
        AddText(main, "Keystore password", storePassword);
        AddText(main, "Key password (if different)", keyPassword);
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome)) java.Text = Path.Combine(javaHome, "bin", "java.exe");
        alias.Text = "androiddebugkey";
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
        buttons.Controls.Add(patch);
        buttons.Controls.Add(cancel);
        AddWide(main, buttons);
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.Dock = DockStyle.Fill;
        log.Height = 110;
        AddWide(main, log);
        patch.Click += PatchClicked;
        cancel.Click += (_, _) => cancellation?.Cancel();
        FormClosing += (_, e) => { if (cancellation != null) { cancellation.Cancel(); e.Cancel = true; } };
    }

    private static void AddWide(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 3);
    }

    private static void AddText(TableLayoutPanel table, string title, TextBox field)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        field.Dock = DockStyle.Fill;
        table.Controls.Add(field, 1, row);
    }

    private void AddPath(TableLayoutPanel table, string title, TextBox field, bool folder, string filter = "All files|*.*")
    {
        int row = table.RowCount;
        AddText(table, title, field);
        var button = new Button { Text = "Browse…", Dock = DockStyle.Fill, AutoSize = true };
        table.Controls.Add(button, 2, row);
        button.Click += (_, _) => {
            if (folder)
            {
                using var dialog = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath;
            }
            else
            {
                using var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName;
            }
        };
    }

    private void AddOutput(TableLayoutPanel table)
    {
        int row = table.RowCount;
        AddText(table, "Export APK (new file)", output);
        var button = new Button { Text = "Browse…", Dock = DockStyle.Fill, AutoSize = true };
        table.Controls.Add(button, 2, row);
        button.Click += (_, _) => {
            using var dialog = new SaveFileDialog { Title = "Export patched APK", Filter = "Android APK|*.apk", OverwritePrompt = false };
            if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
        };
    }

    private async void PatchClicked(object? sender, EventArgs e)
    {
        patch.Enabled = false;
        cancel.Enabled = true;
        log.Clear();
        cancellation = new CancellationTokenSource();
        var progress = new Progress<string>(message => log.AppendText(message + Environment.NewLine));
        try
        {
            var options = new PatchOptions(input.Text.Trim(), output.Text.Trim(), payload.Text.Trim(), unity.Text.Trim(),
                java.Text.Trim(), dexPatcher.Text.Trim(), buildTools.Text.Trim(), keystore.Text.Trim(), alias.Text.Trim(),
                storePassword.Text, string.IsNullOrEmpty(keyPassword.Text) ? null : keyPassword.Text);
            await Task.Run(() => new ApkPatcher(new ToolRunner()).PatchAsync(options, progress, cancellation.Token));
            MessageBox.Show(this, "Signed APK exported. Signature verification does not prove the game or loader will run.",
                "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { log.AppendText("Cancelled. No APK was exported." + Environment.NewLine); }
        catch (Exception ex)
        {
            log.AppendText(ex.Message + Environment.NewLine);
            MessageBox.Show(this, ex.Message, "Patch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            storePassword.Clear();
            keyPassword.Clear();
            cancellation.Dispose();
            cancellation = null;
            patch.Enabled = true;
            cancel.Enabled = false;
        }
    }
}
