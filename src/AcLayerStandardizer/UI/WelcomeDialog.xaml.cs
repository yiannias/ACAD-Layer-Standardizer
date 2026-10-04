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
        SourceInitialized += (_, _) => WindowTheming.EnableDarkTitleBar(this);
        _config = PluginConfig.Load();
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        SetPathLabel(TemplateLabel, _config.TemplateDwgPath, "No reference file set", markMissing: true);
        SetPathLabel(MemoryLabel, _config.MemoryFilePath, "No memory file set");

        var hasTemplate = !string.IsNullOrEmpty(_config.TemplateDwgPath) && File.Exists(_config.TemplateDwgPath);
        StatusLine.Text = hasTemplate
            ? "Ready. Open the mappings editor to connect your layers."
            : "Reference file not set or unavailable. Browse to reconnect it, or open the editor and choose one later.";
    }

    private static void SetPathLabel(System.Windows.Controls.TextBlock label, string? fullPath, string fallback, bool markMissing = false)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            label.Text = fallback;
            label.FontStyle = FontStyles.Italic;
            label.Foreground = System.Windows.Media.Brushes.Gray;
            ToolTipService.SetToolTip(label, null);
            return;
        }

        var exists = !markMissing || File.Exists(fullPath);
        label.Text = exists ? Path.GetFileName(fullPath) : $"Unavailable: {Path.GetFileName(fullPath)}";
        label.FontStyle = FontStyles.Normal;
        // Gainsboro, not Black: this dialog is dark-themed now.
        label.Foreground = exists
            ? System.Windows.Media.Brushes.Gainsboro
            : System.Windows.Media.Brushes.DarkOrange;
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

        var initialDirectory = GetAvailableInitialDirectory(_config.TemplateDwgPath);
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        try
        {
            if (dialog.ShowDialog() == true)
            {
                _config.TemplateDwgPath = dialog.FileName;
                _config.Save();
                RefreshDisplay();
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this,
                $"The reference file picker could not open. Choose a local folder or reconnect the network drive, then try again.\n\n{ex.Message}",
                "Reference File Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var initialDirectory = GetAvailableInitialDirectory(_config.MemoryFilePath);
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        try
        {
            if (dialog.ShowDialog() == true)
            {
                _config.MemoryFilePath = dialog.FileName;
                _config.Save();
                RefreshDisplay();
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this,
                $"The memory file picker could not open. Choose a local folder or reconnect the network drive, then try again.\n\n{ex.Message}",
                "Memory File Location Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? GetAvailableInitialDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var directory = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory)
                ? directory
                : null;
        }
        catch (System.Exception)
        {
            // An old path can contain an unavailable network share or invalid
            // directory. Let the picker start in its normal location instead.
            return null;
        }
    }

    private void OpenMappingsEditor_Click(object sender, RoutedEventArgs e)
    {
        OpenMappings = true;
        DialogResult = true;
        Close();
    }
}
