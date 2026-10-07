using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Builds the controls of every page (500 x 318 white content area, Segoe UI) and refreshes their
/// texts when the language changes. Holds no logic.
/// </summary>
internal sealed class WizardPages
{
    // Shared fonts: WinForms does not dispose a Font assigned to a control, so one instance per style.
    private static readonly Font HeaderFont = new("Segoe UI", 15f, FontStyle.Bold);
    private static readonly Font DescriptionFont = new("Segoe UI", 9.5f);
    private static readonly Font LabelFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    private static readonly Font ComboFont = new("Segoe UI", 10.5f);
    private static readonly Font BodyFont = new("Segoe UI", 9f);
    private static readonly Font InputFont = new("Segoe UI", 9.5f);
    private static readonly Font SmallFont = new("Segoe UI", 8.5f);
    private static readonly Font LaunchFont = new("Segoe UI", 10f);
    private static readonly Font LogFont = new("Consolas", 8f);
    private static readonly Color HeaderColor = Color.FromArgb(0, 51, 102);
    private static readonly Color DescriptionColor = Color.FromArgb(60, 60, 60);
    private static readonly Color HintColor = Color.FromArgb(100, 100, 100);

    private readonly SetupConfig _config;
    private readonly string? _licensePath;
    private readonly Dictionary<WizardPage, Panel> _panels = new();
    private readonly List<(Label Label, System.Func<WizardText, string> Text)> _texts = new();

    public WizardPages(Control content, SetupConfig config, string? licensePath)
    {
        _config = config;
        _licensePath = licensePath;
        BuildLanguage(Page(content, WizardPage.Language));
        BuildLicense(Page(content, WizardPage.License));
        BuildDirectory(Page(content, WizardPage.Directory));
        BuildProgress(Page(content, WizardPage.Progress));
        BuildComplete(Page(content, WizardPage.Complete));
        BuildMaintenance(Page(content, WizardPage.Maintenance));
    }

    public ComboBox LanguageBox { get; private set; } = null!;
    public RichTextBox LicenseBox { get; private set; } = null!;
    public CheckBox AcceptBox { get; private set; } = null!;
    public TextBox FolderBox { get; private set; } = null!;
    public Button BrowseButton { get; private set; } = null!;
    public List<(SetupOption Option, CheckBox Box)> OptionBoxes { get; } = new();
    public ProgressBar Bar { get; private set; } = null!;
    public Label Status { get; private set; } = null!;
    public TextBox Log { get; private set; } = null!;
    public Label ProgressHeader { get; private set; } = null!;
    public Label CompleteHeader { get; private set; } = null!;
    public Label CompleteInfo { get; private set; } = null!;
    public CheckBox LaunchBox { get; private set; } = null!;
    public LinkLabel LogLink { get; private set; } = null!;
    public Label MaintenanceHeader { get; private set; } = null!;
    public RadioButton RepairChoice { get; private set; } = null!;
    public Label RepairInfo { get; private set; } = null!;
    public RadioButton UninstallChoice { get; private set; } = null!;

    public bool HasLicense => _licensePath is not null;

    public void Show(WizardPage page)
    {
        foreach (KeyValuePair<WizardPage, Panel> entry in _panels)
        {
            entry.Value.Visible = entry.Key == page;
        }
    }

    /// <summary>Refreshes every fixed text; maintenance texts that depend on versions are set by the form.</summary>
    public void ApplyText(WizardText text)
    {
        foreach ((Label label, System.Func<WizardText, string> value) in _texts)
        {
            label.Text = value(text);
        }

        AcceptBox.Text = text.Accept;
        BrowseButton.Text = text.Browse;
        LaunchBox.Text = text.Launch;
        LogLink.Text = text.ViewLog;
        UninstallChoice.Text = text.Uninstall;
        foreach ((SetupOption option, CheckBox box) in OptionBoxes)
        {
            box.Text = text.French ? option.LabelFr : option.LabelEn;
        }

        LoadLicense(text);
    }

    private Panel Page(Control content, WizardPage page)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.White };
        content.Controls.Add(panel);
        _panels[page] = panel;
        return panel;
    }

    private void BuildLanguage(Panel page)
    {
        AddHeader(page, text => text.LangHeader);
        AddDescription(page, text => text.LangInfo, 68);
        AddLabel(page, text => text.LangLabel, 130);
        LanguageBox = new ComboBox { Font = ComboFont, Location = new Point(32, 160), Size = new Size(220, 30), DropDownStyle = ComboBoxStyle.DropDownList };
        LanguageBox.Items.AddRange(new object[] { "Français", "English" });
        LanguageBox.SelectedIndex = 0;
        page.Controls.Add(LanguageBox);
    }

    private void BuildLicense(Panel page)
    {
        AddHeader(page, text => text.LicenseHeader);
        AddDescription(page, text => text.LicenseInfo, 68);
        LicenseBox = new RichTextBox { Location = new Point(24, 100), Size = new Size(450, 160), ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Font = BodyFont, BackColor = Color.White };
        page.Controls.Add(LicenseBox);
        AcceptBox = new CheckBox { Font = BodyFont, Location = new Point(24, 272), AutoSize = true };
        page.Controls.Add(AcceptBox);
    }

    private void BuildDirectory(Panel page)
    {
        AddHeader(page, text => text.DirHeader);
        AddDescription(page, text => text.DirInfo, 68);
        AddLabel(page, text => text.DirLabel, 130);
        FolderBox = new TextBox { Font = InputFont, Location = new Point(24, 158), Size = new Size(350, 26) };
        page.Controls.Add(FolderBox);
        BrowseButton = new Button { Font = SmallFont, Size = new Size(90, 27), Location = new Point(382, 157), FlatStyle = FlatStyle.System };
        page.Controls.Add(BrowseButton);
        int y = 200;
        foreach (SetupOption option in _config.Options)
        {
            var box = new CheckBox { Font = InputFont, Location = new Point(24, y), AutoSize = true, Checked = option.DefaultChecked };
            page.Controls.Add(box);
            OptionBoxes.Add((option, box));
            y += 28;
        }
    }

    private void BuildProgress(Panel page)
    {
        ProgressHeader = AddHeader(page, text => text.ProgressHeader);
        AddDescription(page, text => text.ProgressInfo, 68);
        Bar = new ProgressBar { Location = new Point(24, 130), Size = new Size(450, 22), Style = ProgressBarStyle.Continuous };
        page.Controls.Add(Bar);
        Status = new Label { Location = new Point(24, 162), Size = new Size(450, 20), Font = SmallFont, ForeColor = HintColor };
        page.Controls.Add(Status);
        Log = new TextBox
        {
            Location = new Point(24, 190), Size = new Size(450, 110), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = LogFont, BackColor = Color.FromArgb(245, 245, 245), ForeColor = Color.FromArgb(80, 80, 80), BorderStyle = BorderStyle.FixedSingle,
        };
        page.Controls.Add(Log);
    }

    private void BuildComplete(Panel page)
    {
        CompleteHeader = AddHeader(page, text => text.CompleteHeader);
        CompleteInfo = AddDescription(page, text => text.CompleteInfo, 68);
        LaunchBox = new CheckBox { Font = LaunchFont, Location = new Point(32, 130), AutoSize = true, Checked = true };
        page.Controls.Add(LaunchBox);
        LogLink = new LinkLabel { Font = SmallFont, Location = new Point(32, 168), AutoSize = true };
        page.Controls.Add(LogLink);
    }

    private void BuildMaintenance(Panel page)
    {
        MaintenanceHeader = AddHeader(page, text => text.MaintenanceHeader);
        AddDescription(page, text => text.MaintenanceInfo, 68);
        RepairChoice = new RadioButton { Font = InputFont, Location = new Point(24, 120), AutoSize = true, Checked = true };
        page.Controls.Add(RepairChoice);
        RepairInfo = new Label { Font = BodyFont, ForeColor = HintColor, Location = new Point(44, 148), Size = new Size(430, 34) };
        page.Controls.Add(RepairInfo);
        UninstallChoice = new RadioButton { Font = InputFont, Location = new Point(24, 190), AutoSize = true };
        page.Controls.Add(UninstallChoice);
        AddLabel(page, text => text.UninstallInfo, 218, BodyFont, HintColor, left: 44);
    }

    private Label AddHeader(Panel page, System.Func<WizardText, string> text) =>
        Track(page, new Label { Font = HeaderFont, ForeColor = HeaderColor, Location = new Point(24, 20), AutoSize = true }, text);

    private Label AddDescription(Panel page, System.Func<WizardText, string> text, int y) =>
        Track(page, new Label { Font = DescriptionFont, ForeColor = DescriptionColor, Location = new Point(24, y), Size = new Size(450, 40) }, text);

    private Label AddLabel(Panel page, System.Func<WizardText, string> text, int y, Font? font = null, Color? color = null, int left = 24) =>
        Track(page, new Label { Font = font ?? LabelFont, ForeColor = color ?? SystemColors.ControlText, Location = new Point(left, y), AutoSize = true }, text);

    private Label Track(Panel page, Label label, System.Func<WizardText, string> text)
    {
        page.Controls.Add(label);
        _texts.Add((label, text));
        return label;
    }

    private void LoadLicense(WizardText text)
    {
        if (_licensePath is null || !File.Exists(_licensePath))
        {
            LicenseBox.Text = text.LicenseMissing;
            return;
        }

        LicenseBox.LoadFile(_licensePath, RichTextBoxStreamType.RichText);
        if (text.French && _config.LicenseNoticeFr.Length > 0)
        {
            LicenseBox.Text = _config.LicenseNoticeFr + "\n\n" + LicenseBox.Text;
        }
    }
}
