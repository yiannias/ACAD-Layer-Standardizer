using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AcLayerStandardizer.UI;

public class PreviewItem : INotifyPropertyChanged
{
    private bool _isChecked = true;

    public string SourceLayer { get; set; } = "";
    public string TargetLayer { get; set; } = "";
    public string Confidence { get; set; } = "";

    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
