using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;

namespace AcLayerStandardizer.UI;

public partial class PreviewDialog : Window
{
    public List<PreviewItem> SelectedItems { get; private set; } = [];

    public PreviewDialog(
        List<PreviewItem> items,
        List<string> unmatched)
    {
        InitializeComponent();

        var vm = new PreviewViewModel(items, unmatched);
        DataContext = vm;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var vm = (PreviewViewModel)DataContext;
        SelectedItems = vm.Items.Where(i => i.IsChecked).ToList();
        DialogResult = true;
        Close();
    }
}

public class PreviewViewModel
{
    public ObservableCollection<PreviewItem> Items { get; }
    public ObservableCollection<string> Unmatched { get; }
    public Visibility UnmatchedVisibility { get; }

    private bool _selectAll = true;
    public bool SelectAll
    {
        get => _selectAll;
        set
        {
            _selectAll = value;
            foreach (var item in Items)
                item.IsChecked = value;
        }
    }

    public PreviewViewModel(List<PreviewItem> items, List<string> unmatched)
    {
        Items = new ObservableCollection<PreviewItem>(items);
        Unmatched = new ObservableCollection<string>(unmatched);
        UnmatchedVisibility = unmatched.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
