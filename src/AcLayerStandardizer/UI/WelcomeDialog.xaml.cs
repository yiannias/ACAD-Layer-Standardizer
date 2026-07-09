using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.UI;

public partial class WelcomeDialog : Window
{
    private readonly PluginConfig _config;

    public bool OpenMappings { get; private set; }

    public WelcomeDialog()
    {
        InitializeComponent();
        _config = PluginConfig.Load();
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        SetPathLabel(TemplateLabel, _config.TemplateDwgPath, "No reference file set");
        SetPathLabel(MemoryLabel, _config.MemoryFilePath, "No memory file set");

        var hasTemplate = !string.IsNullOrEmpty(_config.TemplateDwgPath) && File.Exists(_config.TemplateDwgPath);
        StatusLine.Text = hasTemplate
            ? "Ready. Open the mappings editor to connect your layers."
            : "Tip: set a reference file first, then open the editor.";
    }

    private static void SetPathLabel(System.Windows.Controls.TextBlock label, string? fullPath, string fallback)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            label.Text = fallback;
            label.FontStyle = FontStyles.Italic;
            label.Foreground = System.Windows.Media.Brushes.Gray;
            ToolTipService.SetToolTip(label, null);
            return;
        }

        label.Text = Path.GetFileName(fullPath);
        label.FontStyle = FontStyles.Normal;
        label.Foreground = System.Windows.Media.Brushes.Black;
        ToolTipService.SetToolTip(label, fullPath);
    }

    private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Reference File / Standards File",
            Filter = "Drawing Files (*.dwg;*.dws)|*.dwg;*.dws|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrEmpty(_config.TemplateDwgPath))
        {
            var dir = Path.GetDirectoryName(_config.TemplateDwgPath);
            if (dir is not null) dialog.InitialDirectory = dir;
        }

        if (dialog.ShowDialog() == true)
        {
            _config.TemplateDwgPath = dialog.FileName;
            _config.Save();
            RefreshDisplay();
        }
    }

    private void BrowseMemory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Select Memory File Location",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = "standards_memory.json"
        };

        if (!string.IsNullOrEmpty(_config.MemoryFilePath))
        {
            var dir = Path.GetDirectoryName(_config.MemoryFilePath);
            if (dir is not null) dialog.InitialDirectory = dir;
        }

        if (dialog.ShowDialog() == true)
        {
            _config.MemoryFilePath = dialog.FileName;
            _config.Save();
            RefreshDisplay();
        }
    }

    private void OpenMappingsEditor_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.TemplateDwgPath) || !File.Exists(_config.TemplateDwgPath))
        {
            MessageBox.Show(this,
                "Please set a valid reference file before opening the mappings editor.",
                "Reference Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenMappings = true;
        DialogResult = true;
        Close();
    }
}
