#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PhotoSorterApp.Views;

public partial class SelectOrTypePersonDialog : Window, INotifyPropertyChanged
{
    private readonly List<string> _allNames;

    public string Prompt { get; }
    public string? ResultName { get; private set; }

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set
        {
            _inputText = value;
            OnPropertyChanged();
            RefreshFilter();
            // Clear list selection when user types
            if (!string.IsNullOrWhiteSpace(value))
                SelectedExisting = null;
        }
    }

    public ObservableCollection<string> FilteredNames { get; } = new();

    private string? _selectedExisting;
    public string? SelectedExisting
    {
        get => _selectedExisting;
        set
        {
            _selectedExisting = value;
            OnPropertyChanged();
            // Fill input when item picked from list
            if (value is not null)
                InputText = value;
        }
    }

    public SelectOrTypePersonDialog(string prompt, IEnumerable<string> existingNames)
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
        var f = _inputText.Trim();
        foreach (var n in _allNames.Where(n =>
                     string.IsNullOrEmpty(f) || n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            FilteredNames.Add(n);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = InputText.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Введите или выберите имя.", "Назначить лицо",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ResultName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
