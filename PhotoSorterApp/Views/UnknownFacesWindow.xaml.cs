#nullable enable

using PhotoSorterApp.Models;
using PhotoSorterApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Views;

public partial class UnknownFacesWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<UnknownFaceItem> Items { get; } = new();
    public ICollectionView GroupedItems { get; }
    private readonly FaceLabelingService _labelingService;
    private readonly IFaceCatalogService _catalogService;
    public int AssignedCount { get; private set; }

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public UnknownFacesWindow(
        IEnumerable<DetectedFace> unknownFaces,
        FaceLabelingService labelingService,
        FaceClusteringService clusteringService,
        IFaceCatalogService catalogService)
    {
        InitializeComponent();
        DataContext = this;
        _labelingService = labelingService;
        _catalogService = catalogService;

        var faceList = unknownFaces
            .Where(face => face.PhotoAsset != null && !string.IsNullOrWhiteSpace(face.PhotoAsset.FilePath))
            .ToList();

        var clusters = clusteringService.BuildClusters(faceList, 0.62);

        foreach (var face in faceList.OrderBy(f => clusters.TryGetValue(f.Id, out var c) ? c : int.MaxValue).ThenByDescending(f => f.Confidence))
        {
            var clusterId = clusters.TryGetValue(face.Id, out var c) ? c : int.MaxValue;
            Items.Add(new UnknownFaceItem(face, clusterId));
        }

        // Group view by ClusterId
        GroupedItems = CollectionViewSource.GetDefaultView(Items);
        GroupedItems.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UnknownFaceItem.ClusterId)));
        GroupedItems.SortDescriptions.Add(new SortDescription(nameof(UnknownFaceItem.ClusterId), ListSortDirection.Ascending));

        Loaded += UnknownFacesWindow_Loaded;
    }

    private async void UnknownFacesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        IsLoading = true;
        await Task.Run(async () =>
        {
            foreach (var item in Items)
            {
                await item.LoadPreviewAsync();
            }
        });
        IsLoading = false;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
            item.IsSelected = false;
    }

    private void MoveToCluster_Click(object sender, RoutedEventArgs e)
    {
        var selected = Items.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одно лицо для перемещения.", "Переместить в кластер", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Suggest next available cluster id
        var maxCluster = Items.Max(x => x.ClusterId < 100000 ? x.ClusterId : 0);
        var suggestion = (maxCluster + 1).ToString();

        var input = new InputDialog("Введите номер целевого кластера:", suggestion) { Owner = this };
        if (input.ShowDialog() != true || !int.TryParse(input.Input?.Trim(), out var targetCluster) || targetCluster < 1)
        {
            MessageBox.Show("Введите корректный номер кластера (целое число больше 0).", "Переместить в кластер", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var item in selected)
        {
            item.ClusterId = targetCluster;
            item.IsSelected = false;
        }

        GroupedItems.Refresh();
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = Items.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одно лицо для удаления.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Удалить {selected.Count} выбранных лиц из базы?",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        foreach (var item in selected)
        {
            await _catalogService.DeleteDetectedFaceAsync(item.Face.Id, CancellationToken.None);
            Items.Remove(item);
        }

        GroupedItems.Refresh();
        MessageBox.Show($"Удалено: {selected.Count}", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void AssignSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = Items.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одно лицо.", "Назначить имя", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedClusterIds = selected.Select(x => x.ClusterId).Distinct().ToHashSet();
        var autoExpanded = Items.Where(x => selectedClusterIds.Contains(x.ClusterId)).ToList();

        // Load existing persons for quick pick
        var existingPersons = await _catalogService.GetPersonsAsync();
        var existingNames = existingPersons.Select(p => p.DisplayName).ToList();

        string? name = null;

        if (existingNames.Count > 0)
        {
            var dialog = new SelectOrTypePersonDialog("Кто изображён на выбранных фото?", existingNames) { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultName))
                return;
            name = dialog.ResultName.Trim();
        }
        else
        {
            var input = new InputDialog("Кто изображён на выбранных фото?", "") { Owner = this };
            if (input.ShowDialog() != true || string.IsNullOrWhiteSpace(input.Input))
                return;
            name = input.Input.Trim();
        }

        var assigned = await _labelingService.AssignFacesToPersonAsync(autoExpanded.Select(x => x.Face), name);
        AssignedCount += assigned;

        foreach (var item in autoExpanded)
            Items.Remove(item);

        GroupedItems.Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

public class UnknownFaceItem : INotifyPropertyChanged
{
    public DetectedFace Face { get; }
    public string FilePath => Face.PhotoAsset?.FilePath ?? string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public double Confidence => Face.Confidence;

    private int _clusterId;
    public int ClusterId
    {
        get => _clusterId;
        set
        {
            if (_clusterId == value) return;
            _clusterId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClusterLabel));
        }
    }

    public string ClusterLabel => $"Кластер {ClusterId}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private ImageSource? _preview;
    public ImageSource? Preview
    {
        get => _preview;
        private set { _preview = value; OnPropertyChanged(); }
    }

    public ICommand ToggleSelectedCommand { get; }

    public UnknownFaceItem(DetectedFace face, int clusterId)
    {
        Face = face;
        _clusterId = clusterId;
        ToggleSelectedCommand = new RelayCommand(() => IsSelected = !IsSelected);
    }

    public Task LoadPreviewAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                BitmapSource? result = null;

                var image = new BitmapImage();
                using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                }

                var rect = new Int32Rect(
                    Math.Max(0, (int)Math.Round(Face.BoundingBoxX)),
                    Math.Max(0, (int)Math.Round(Face.BoundingBoxY)),
                    Math.Max(1, (int)Math.Round(Face.BoundingBoxWidth)),
                    Math.Max(1, (int)Math.Round(Face.BoundingBoxHeight)));

                rect = FitRect(image.PixelWidth, image.PixelHeight, rect);
                var cropped = new CroppedBitmap(image, rect);
                cropped.Freeze();
                result = cropped;

                // Marshal back to UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() => Preview = result);
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => Preview = null);
            }
        });
    }

    private static Int32Rect FitRect(int imageWidth, int imageHeight, Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, imageHeight - 1));
        var w = Math.Clamp(rect.Width, 1, imageWidth - x);
        var h = Math.Clamp(rect.Height, 1, imageHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
