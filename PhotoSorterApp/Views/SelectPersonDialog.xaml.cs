#nullable enable

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PhotoSorterApp.Views;

public partial class SelectPersonDialog : Window, INotifyPropertyChanged
{
    private readonly List<string> _allNames;

    public string Prompt { get; }

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); RefreshFilter(); }
    }

    public ObservableCollection<string> FilteredNames { get; } = new();

    private string? _selectedName;
    public string? SelectedName
    {
        get => _selectedName;
        set { _selectedName = value; OnPropertyChanged(); }
    }

    public SelectPersonDialog(string prompt, IEnumerable<string> existingNames)
    {
        InitializeComponent();
        DataContext = this;
        Prompt = prompt;
        _allNames = existingNames.OrderBy(n => n).ToList();
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        FilteredNames.Clear();
        var filter = FilterText.Trim();
        foreach (var n in _allNames.Where(n => string.IsNullOrEmpty(filter) ||
                     n.Contains(filter, System.StringComparison.OrdinalIgnoreCase)))
            FilteredNames.Add(n);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedName))
        {
            MessageBox.Show("Выберите персону из списка.", "Выбор персоны",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
