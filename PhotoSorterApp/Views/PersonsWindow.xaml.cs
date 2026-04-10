#nullable enable

using PhotoSorterApp.Models;
using PhotoSorterApp.Resources;
using PhotoSorterApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoSorterApp.Views;

public partial class PersonsWindow : Window, INotifyPropertyChanged
{
    private readonly IFaceCatalogService _catalog;
    private readonly FacePersonManagementService _personManagement;
    private readonly List<FacePerson> _allPersons = new();
    private readonly List<DetectedFace> _allSelectedPersonFaces = new();
    private const int PreviewPageSize = 50;

    public ObservableCollection<FacePerson> Persons { get; } = new();
    public ObservableCollection<FacePreviewItem> SelectedFacePreviews { get; } = new();

    private string _personSearchText = string.Empty;
    public string PersonSearchText
    {
        get => _personSearchText;
        set
        {
            if (_personSearchText == value)
                return;
            _personSearchText = value;
            OnPropertyChanged();
            ApplyPersonFilterAndSort();
        }
    }

    private bool _isLoadingPreviews;
    public bool IsLoadingPreviews
    {
        get => _isLoadingPreviews;
        private set
        {
            if (_isLoadingPreviews == value)
                return;
            _isLoadingPreviews = value;
            OnPropertyChanged();
        }
    }

    private int _previewLoadVersion;

    private string _previewLoadingText = UiStrings.PersonsWindow_LoadingFaces;
    public string PreviewLoadingText
    {
        get => _previewLoadingText;
        private set
        {
            if (_previewLoadingText == value)
                return;
            _previewLoadingText = value;
            OnPropertyChanged();
        }
    }

    private FacePerson? _selectedPerson;
    public FacePerson? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            _selectedPerson = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMarkedFaces));
            _ = LoadFacePreviewsAsync(value);
        }
    }

    private int _currentPreviewPage = 1;
    public int CurrentPreviewPage
    {
        get => _currentPreviewPage;
        private set
        {
            if (_currentPreviewPage == value)
                return;
            _currentPreviewPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageStatusText));
            OnPropertyChanged(nameof(CanGoPrevPage));
            OnPropertyChanged(nameof(CanGoNextPage));
        }
    }

    private int _totalPreviewPages = 1;
    public int TotalPreviewPages
    {
        get => _totalPreviewPages;
        private set
        {
            if (_totalPreviewPages == value)
                return;
            _totalPreviewPages = Math.Max(1, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageStatusText));
            OnPropertyChanged(nameof(CanGoPrevPage));
            OnPropertyChanged(nameof(CanGoNextPage));
        }
    }

    public string PageStatusText => string.Format(UiStrings.PersonsWindow_PageStatusFormat, CurrentPreviewPage, TotalPreviewPages);
    public bool CanGoPrevPage => CurrentPreviewPage > 1;
    public bool CanGoNextPage => CurrentPreviewPage < TotalPreviewPages;
    public bool HasMarkedFaces => SelectedFacePreviews.Any(x => x.IsMarked);

    public PersonsWindow(IFaceCatalogService catalog, FacePersonManagementService personManagement)
    {
        InitializeComponent();
        DataContext = this;
        _catalog = catalog;
        _personManagement = personManagement;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var saved = SelectedPerson?.Id;
        _allPersons.Clear();
        _allPersons.AddRange(await _catalog.GetPersonsAsync());

        ApplyPersonFilterAndSort();

        if (saved is int id)
            SelectedPerson = Persons.FirstOrDefault(p => p.Id == id);
        else if (Persons.Count > 0)
            SelectedPerson = Persons[0];
    }

    private void ApplyPersonFilterAndSort()
    {
        IEnumerable<FacePerson> query = _allPersons;

        var filter = PersonSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(filter))
            query = query.Where(p => p.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));

        query = query.OrderByDescending(p => p.FaceCount).ThenBy(p => p.DisplayName);

        var currentSelectedId = SelectedPerson?.Id;

        Persons.Clear();
        foreach (var person in query)
            Persons.Add(person);

        if (currentSelectedId is int id)
            SelectedPerson = Persons.FirstOrDefault(p => p.Id == id);
    }

    private async Task LoadFacePreviewsAsync(FacePerson? person)
    {
        foreach (var preview in SelectedFacePreviews)
            preview.PropertyChanged -= FacePreviewItem_PropertyChanged;

        SelectedFacePreviews.Clear();
        _allSelectedPersonFaces.Clear();
        CurrentPreviewPage = 1;
        TotalPreviewPages = 1;
        OnPropertyChanged(nameof(HasMarkedFaces));

        if (person is null)
            return;

        var myVersion = ++_previewLoadVersion;

        IsLoadingPreviews = true;
        PreviewLoadingText = UiStrings.PersonsWindow_LoadingFaces;
        try
        {
            var faces = await _catalog.GetFacesByPersonAsync(person.Id, take: 2000);
            _allSelectedPersonFaces.AddRange(faces);
            TotalPreviewPages = (int)Math.Ceiling(_allSelectedPersonFaces.Count / (double)PreviewPageSize);

            await LoadPreviewPageAsync(myVersion);
        }
        finally
        {
            if (myVersion == _previewLoadVersion)
                IsLoadingPreviews = false;
        }
    }

    private async Task LoadPreviewPageAsync(int myVersion)
    {
        foreach (var preview in SelectedFacePreviews)
            preview.PropertyChanged -= FacePreviewItem_PropertyChanged;

        SelectedFacePreviews.Clear();
        OnPropertyChanged(nameof(HasMarkedFaces));

        var pageFaces = _allSelectedPersonFaces
            .Skip((CurrentPreviewPage - 1) * PreviewPageSize)
            .Take(PreviewPageSize)
            .ToList();

        var total = pageFaces.Count;
        var loaded = 0;

        foreach (var face in pageFaces)
        {
            if (myVersion != _previewLoadVersion)
                return;

            var item = new FacePreviewItem(face);
            item.PropertyChanged += FacePreviewItem_PropertyChanged;
            SelectedFacePreviews.Add(item);
            await item.LoadAsync();

            loaded++;
            PreviewLoadingText = string.Format(UiStrings.PersonsWindow_LoadingFacesProgress, loaded, total);
        }
    }

    private void FacePreviewItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FacePreviewItem.IsMarked))
            OnPropertyChanged(nameof(HasMarkedFaces));
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (!CanGoPrevPage)
            return;

        CurrentPreviewPage--;
        var myVersion = ++_previewLoadVersion;
        IsLoadingPreviews = true;
        try
        {
            await LoadPreviewPageAsync(myVersion);
        }
        finally
        {
            if (myVersion == _previewLoadVersion)
                IsLoadingPreviews = false;
        }
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (!CanGoNextPage)
            return;

        CurrentPreviewPage++;
        var myVersion = ++_previewLoadVersion;
        IsLoadingPreviews = true;
        try
        {
            await LoadPreviewPageAsync(myVersion);
        }
        finally
        {
            if (myVersion == _previewLoadVersion)
                IsLoadingPreviews = false;
        }
    }

    private void ToggleFaceMark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FacePreviewItem item)
            item.IsMarked = !item.IsMarked;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPerson is null)
        {
            MessageBox.Show(UiStrings.PersonsWindow_SelectPersonMessage, UiStrings.PersonsWindow_InfoTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = new InputDialog(UiStrings.PersonsWindow_RenameDialogTitle, SelectedPerson.DisplayName) { Owner = this };
        if (input.ShowDialog() != true || string.IsNullOrWhiteSpace(input.Input))
            return;

        await _personManagement.RenamePersonAsync(SelectedPerson.Id, input.Input.Trim());
        await RefreshAsync();
    }

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPerson is null)
        {
            MessageBox.Show(UiStrings.PersonsWindow_SelectSourcePersonMessage, UiStrings.PersonsWindow_InfoTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var names = Persons
            .Where(p => p.Id != SelectedPerson.Id)
            .Select(p => p.DisplayName)
            .ToList();

        var dialog = new SelectPersonDialog(
            string.Format(UiStrings.PersonsWindow_MergeDialogPrompt, SelectedPerson.DisplayName),
            names) { Owner = this };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedName))
            return;

        await _personManagement.MergePersonIntoNameAsync(SelectedPerson.Id, dialog.SelectedName);
        await RefreshAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPerson is null)
        {
            MessageBox.Show(UiStrings.PersonsWindow_SelectPersonMessage, UiStrings.PersonsWindow_InfoTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(UiStrings.PersonsWindow_DeletePersonConfirm, SelectedPerson.DisplayName),
            UiStrings.PersonsWindow_DeleteConfirmTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        await _catalog.DeletePersonAsync(SelectedPerson.Id);
        await RefreshAsync();
    }

    private async void DeleteMarkedFaces_Click(object sender, RoutedEventArgs e)
    {
        var marked = SelectedFacePreviews.Where(x => x.IsMarked).ToList();
        if (marked.Count == 0)
        {
            MessageBox.Show(UiStrings.PersonsWindow_SelectMarkedFacesMessage, UiStrings.PersonsWindow_InfoTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(UiStrings.PersonsWindow_DeleteMarkedFacesConfirm, marked.Count),
            UiStrings.PersonsWindow_DeleteFacesConfirmTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        foreach (var item in marked)
        {
            item.PropertyChanged -= FacePreviewItem_PropertyChanged;
            await _catalog.DeleteDetectedFaceAsync(item.FaceId);
            SelectedFacePreviews.Remove(item);
        }

        OnPropertyChanged(nameof(HasMarkedFaces));

        if (SelectedPerson is not null)
            SelectedPerson.FaceCount = Math.Max(0, SelectedPerson.FaceCount - marked.Count);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class FacePreviewItem : INotifyPropertyChanged
{
    private readonly DetectedFace _face;
    public int FaceId => _face.Id;
    public string FileName => Path.GetFileName(_face.PhotoAsset?.FilePath ?? "");

    private bool _isMarked;
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value)
                return;
            _isMarked = value;
            OnPropertyChanged();
        }
    }

    private ImageSource? _preview;
    public ImageSource? Preview
    {
        get => _preview;
        private set
        {
            _preview = value;
            OnPropertyChanged();
        }
    }

    public FacePreviewItem(DetectedFace face) => _face = face;

    public Task LoadAsync() => Task.Run(() =>
    {
        try
        {
            var path = _face.PhotoAsset?.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            var image = new BitmapImage();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
            }

            var rect = new Int32Rect(
                Math.Max(0, (int)Math.Round(_face.BoundingBoxX)),
                Math.Max(0, (int)Math.Round(_face.BoundingBoxY)),
                Math.Max(1, (int)Math.Round(_face.BoundingBoxWidth)),
                Math.Max(1, (int)Math.Round(_face.BoundingBoxHeight)));

            rect = FitRect(image.PixelWidth, image.PixelHeight, rect);
            var cropped = new CroppedBitmap(image, rect);
            cropped.Freeze();

            Application.Current.Dispatcher.Invoke(() => Preview = cropped);
        }
        catch
        {
        }
    });

    private static Int32Rect FitRect(int imageWidth, int imageHeight, Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, imageHeight - 1));
        var w = Math.Clamp(rect.Width, 1, imageWidth - x);
        var h = Math.Clamp(rect.Height, 1, imageHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    private static Int32Rect ToPixelRect(int pixelWidth, int pixelHeight, double x, double y, double w, double h)
    {
        // Bounding boxes are stored in original image pixel coordinates.

        // Expand a bit for nicer preview
        var padX = w * 0.12;
        var padY = h * 0.18;

        x -= padX;
        y -= padY;
        w += padX * 2;
        h += padY * 2;

        var ix = (int)Math.Round(x);
        var iy = (int)Math.Round(y);
        var iw = (int)Math.Round(w);
        var ih = (int)Math.Round(h);

        ix = Math.Clamp(ix, 0, Math.Max(0, pixelWidth - 1));
        iy = Math.Clamp(iy, 0, Math.Max(0, pixelHeight - 1));
        iw = Math.Clamp(iw, 1, Math.Max(1, pixelWidth - ix));
        ih = Math.Clamp(ih, 1, Math.Max(1, pixelHeight - iy));

        return new Int32Rect(ix, iy, iw, ih);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
