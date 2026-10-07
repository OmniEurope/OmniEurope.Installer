using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// The installation wizard: Language, License, Directory, Progress, Complete for a first installation;
/// Maintenance (update, repair or uninstall) when the product is already there. The operation runs on
/// a background thread; this form only follows its progress.
/// </summary>
internal sealed class WizardForm : Form
{
    private static readonly Font ButtonFont = new("Segoe UI", 9f);

    private readonly SetupSession _session;
    private readonly SetupCommandLine _commandLine;
    private readonly WizardText _text;
    private readonly WizardPages _pages;
    private readonly ProgressAnimator _animator;
    private readonly InstallChoices _choices;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _cancel;
    private readonly Icon? _icon;
    private WizardPage _page;
    private SetupAction _action = SetupAction.Install;
    private bool _busy;

    public WizardForm(SetupSession session, SetupCommandLine commandLine)
    {
        _session = session;
        _commandLine = commandLine;
        _text = new WizardText(session.Config.ProductName);
        _choices = session.InitialChoices();

        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Control;
        _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Icon = _icon;

        var content = new Panel { Location = Point.Empty, Size = new Size(ClientSize.Width, 318), BackColor = Color.White };
        Controls.Add(content);
        Controls.Add(new Label { Location = new Point(0, 318), Size = new Size(ClientSize.Width, 1), BackColor = Color.FromArgb(200, 200, 200) });
        _cancel = AddButton(ClientSize.Width - 100, OnCancel);
        _next = AddButton(ClientSize.Width - 210, OnNext);
        _back = AddButton(ClientSize.Width - 320, OnBack);

        _pages = new WizardPages(content, session.Config, session.LicensePath);
        _animator = new ProgressAnimator(RenderProgress, () => ShowPage(WizardPage.Complete));
        WireControls();
        LoadChoices();
        ApplyLanguage();
        ShowPage(session.Mode == SetupMode.Install ? WizardPage.Language : WizardPage.Maintenance);
    }

    /// <summary>Exit code of the setup program: the Windows Installer result, 1602 when cancelled.</summary>
    public int ExitCode { get; private set; } = (int)MsiApi.UserExit;

    /// <summary>Writes every page, in French and in English, to PNG files (layout check without clicking).</summary>
    public static void RenderPages(SetupSession session, SetupCommandLine commandLine, string folder)
    {
        Directory.CreateDirectory(folder);
        using var form = new WizardForm(session, commandLine) { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) };
        form.Show();
        foreach (bool french in new[] { true, false })
        {
            form._pages.LanguageBox.SelectedIndex = french ? 0 : 1;
            foreach (WizardPage page in Enum.GetValues(typeof(WizardPage)))
            {
                form.ShowPage(page);
                form.Refresh();
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(folder, $"{(french ? "fr" : "en")}-{(int)page}-{page}.png"), ImageFormat.Png);
            }
        }

        form._busy = false;
        form.Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The operation cannot be abandoned half-way: the window stays until Windows Installer is done.
        e.Cancel = _busy;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animator.Dispose();
            _icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Button AddButton(int x, EventHandler handler)
    {
        var button = new Button { Font = ButtonFont, Size = new Size(100, 30), Location = new Point(x, 335), FlatStyle = FlatStyle.System };
        button.Click += handler;
        Controls.Add(button);
        return button;
    }

    private void WireControls()
    {
        _pages.LanguageBox.SelectedIndexChanged += (_, _) => ApplyLanguage();
        _pages.AcceptBox.CheckedChanged += (_, _) => UpdateButtons();
        _pages.FolderBox.TextChanged += (_, _) => UpdateButtons();
        _pages.RepairChoice.CheckedChanged += (_, _) => UpdateButtons();
        _pages.BrowseButton.Click += OnBrowse;
        _pages.LogLink.LinkClicked += (_, _) => OpenLog();
        _session.ProgressChanged += percent => SafeInvoke(() => _animator.Report(percent));
        _session.Message += message => SafeInvoke(() => _pages.Log.AppendText(message.TrimEnd() + Environment.NewLine));
    }

    private void LoadChoices()
    {
        _pages.LanguageBox.SelectedIndex = _choices.Language == "en" ? 1 : 0;
        _pages.FolderBox.Text = _choices.InstallFolder;
        foreach ((SetupOption option, CheckBox box) in _pages.OptionBoxes)
        {
            box.Checked = _choices.Options.TryGetValue(option.Property, out bool value) ? value : option.DefaultChecked;
        }
    }

    private void SaveChoices()
    {
        _choices.Language = _text.French ? "fr" : "en";
        _choices.InstallFolder = _pages.FolderBox.Text.Trim();
        foreach ((SetupOption option, CheckBox box) in _pages.OptionBoxes)
        {
            _choices.Options[option.Property] = box.Checked;
        }
    }

    private void ApplyLanguage()
    {
        _text.French = _pages.LanguageBox.SelectedIndex == 0;
        Text = _text.Title;
        _pages.ApplyText(_text);
        ApplyMaintenanceText();
        UpdateButtons();
    }

    private void ApplyMaintenanceText()
    {
        string installed = _session.Installed?.Version ?? string.Empty;
        string version = _session.Config.ProductVersion;
        bool upgrade = _session.Mode is SetupMode.Upgrade or SetupMode.SameVersion;
        _pages.MaintenanceHeader.Text = upgrade ? _text.MaintenanceHeaderUpdate(installed) : _text.MaintenanceHeader;
        _pages.RepairChoice.Text = upgrade ? _text.Update : _text.Repair;
        _pages.RepairInfo.Text = _session.Mode switch
        {
            SetupMode.Upgrade => _text.UpdateInfo(version),
            SetupMode.SameVersion => _text.SameVersionInfo(version),
            SetupMode.Downgrade => _text.DowngradeBlocked(version, installed),
            _ => _text.RepairInfo,
        };
        _pages.RepairChoice.Enabled = _session.Mode != SetupMode.Downgrade;
    }

    private void ShowPage(WizardPage page)
    {
        _page = page;
        _pages.Show(page);
        if (page == WizardPage.Complete)
        {
            _pages.LogLink.Visible = File.Exists(_session.LogPath);
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool wizardPage = _page is WizardPage.Language or WizardPage.License or WizardPage.Directory or WizardPage.Maintenance;
        _back.Visible = _page is WizardPage.License or WizardPage.Directory;
        _next.Visible = wizardPage;
        _next.Enabled = _page switch
        {
            WizardPage.License => _pages.AcceptBox.Checked,
            WizardPage.Directory => !string.IsNullOrWhiteSpace(_pages.FolderBox.Text),
            WizardPage.Maintenance => _session.Mode != SetupMode.Downgrade,
            _ => true,
        };
        _next.Text = NextText();
        _back.Text = _text.Back;
        _cancel.Text = _page == WizardPage.Complete ? _text.Close : _text.Cancel;
        _cancel.Enabled = _page != WizardPage.Progress;
    }

    private string NextText() => _page switch
    {
        WizardPage.Directory => _text.Install,
        WizardPage.Maintenance when _pages.UninstallChoice.Checked => _text.Uninstall,
        WizardPage.Maintenance => _pages.RepairChoice.Text,
        _ => _text.Next,
    };

    private void OnNext(object? sender, EventArgs e)
    {
        switch (_page)
        {
            case WizardPage.Language:
                ShowPage(_pages.HasLicense ? WizardPage.License : WizardPage.Directory);
                break;
            case WizardPage.License:
                ShowPage(WizardPage.Directory);
                break;
            case WizardPage.Directory:
                SaveChoices();
                Begin(SetupAction.Install);
                break;
            case WizardPage.Maintenance:
                Begin(_pages.UninstallChoice.Checked ? SetupAction.Uninstall
                    : _session.Mode == SetupMode.Repair ? SetupAction.Repair : SetupAction.Install);
                break;
        }
    }

    private void OnBack(object? sender, EventArgs e)
    {
        if (_page == WizardPage.Directory && _pages.HasLicense)
        {
            ShowPage(WizardPage.License);
        }
        else if (_page is WizardPage.License or WizardPage.Directory)
        {
            ShowPage(WizardPage.Language);
        }
    }

    private void Begin(SetupAction action)
    {
        _action = action;
        _pages.ProgressHeader.Text = action switch
        {
            SetupAction.Uninstall => _text.Uninstalling,
            SetupAction.Repair => _text.Repairing,
            _ when _session.Mode != SetupMode.Install => _text.Updating,
            _ => _text.ProgressHeader,
        };
        _pages.Log.Clear();
        _busy = true;
        ShowPage(WizardPage.Progress);
        _animator.Start();
        Task.Run(() => _session.Run(action, _choices, _commandLine.Properties))
            .ContinueWith(task => SafeInvoke(() => Finish(task)));
    }

    private void Finish(Task<uint> task)
    {
        _busy = false;
        uint result = task.Status == TaskStatus.RanToCompletion ? task.Result : MsiApi.InstallFailure;
        if (task.Exception is not null)
        {
            _pages.Log.AppendText(task.Exception.GetBaseException().Message + Environment.NewLine);
        }

        ExitCode = (int)result;
        if (!MsiCommandLines.IsSuccess(result))
        {
            _animator.Stop();
            MessageBox.Show(this, _text.Failed(result), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetCompleteText(_text.Failed(result), string.Empty, launch: false);
            ShowPage(WizardPage.Complete);
            return;
        }

        (string header, string info) = _action switch
        {
            SetupAction.Uninstall => (_text.UninstallComplete, _text.UninstallCompleteInfo),
            SetupAction.Repair => (_text.RepairComplete, _text.RepairCompleteInfo),
            _ when _session.Mode != SetupMode.Install => (_text.UpdateComplete, _text.UpdateCompleteInfo),
            _ => (_text.CompleteHeader, _text.CompleteInfo),
        };
        bool restart = MsiCommandLines.RestartRequired(result);
        if (restart)
        {
            info += Environment.NewLine + Environment.NewLine + _text.RestartRequired;
        }

        SetCompleteText(header, info, launch: _action != SetupAction.Uninstall && !restart);
        _animator.Finish();
    }

    private void SetCompleteText(string header, string info, bool launch)
    {
        _pages.CompleteHeader.Text = header;
        _pages.CompleteInfo.Text = info;
        _pages.LaunchBox.Visible = launch;
        _pages.LaunchBox.Checked = launch;
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        if (_page == WizardPage.Complete && _pages.LaunchBox.Visible && _pages.LaunchBox.Checked)
        {
            _session.LaunchProduct();
        }

        Close();
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _pages.FolderBox.Text, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pages.FolderBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenLog()
    {
        // Opened by file association: only the .log the setup itself named.
        if (File.Exists(_session.LogPath) && string.Equals(Path.GetExtension(_session.LogPath), ".log", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo(_session.LogPath) { UseShellExecute = true })?.Dispose();
        }
    }

    private void RenderProgress(int? percent)
    {
        _pages.Bar.Style = percent is null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _pages.Bar.Value = percent ?? 0;
        _pages.Status.Text = percent is null ? string.Empty : $"{percent} %";
    }

    private void SafeInvoke(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(action);
        }
    }
}
