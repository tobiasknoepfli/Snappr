using System;
using System.Text.RegularExpressions;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Snappr.Models;
using Snappr.Services;
using Microsoft.Win32;

namespace Snappr.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ImageService _imageService = new ImageService();
        private List<ImageModel> _allImages = new List<ImageModel>();
        private Dictionary<string, ImageModel> _allImagesLookup = new Dictionary<string, ImageModel>(StringComparer.OrdinalIgnoreCase);
        private ObservableCollection<ImageModel> _filteredImages = new ObservableCollection<ImageModel>();

        private ImageModel? _selectedImage;
        private string _searchText = string.Empty;
        private bool _isBusy;
        private string _statusMessage = "Ready";
        
        private string _sourceFolder = "No folder selected";
        private ObservableCollection<FolderNode> _folderTree = new ObservableCollection<FolderNode>();
        private FolderNode? _selectedFolderNode;
        private bool _isFullImageMode;

        public MainViewModel()
        {
            SelectFolderCommand = new RelayCommand(async _ => await SelectFolder());
            ExportCommand = new RelayCommand(async p => await ExportImage(p?.ToString()));
            OpenInExplorerCommand = new RelayCommand(_ => OpenInExplorer());
            RefreshCommand = new RelayCommand(async _ => await Refresh());
            ClosePreviewCommand = new RelayCommand(_ => IsFullImageMode = false);
            OpenPreviewCommand = new RelayCommand(_ => IsFullImageMode = true);
            ClearSearchCommand = new RelayCommand(_ => SearchText = string.Empty);
            SearchCommand = new RelayCommand(_ => ApplyFilter());
            NextImageCommand = new RelayCommand(_ => NextImage());
            PreviousImageCommand = new RelayCommand(_ => PreviousImage());

            // Load last session

            var settings = SettingsService.Load();
            if (!string.IsNullOrEmpty(settings.LastSourceFolder) && Directory.Exists(settings.LastSourceFolder))
            {
                InitializeFolder(settings.LastSourceFolder);
            }
        }


        public ObservableCollection<ImageModel> FilteredImages
        {
            get => _filteredImages;
            set => SetProperty(ref _filteredImages, value);
        }

        public ImageModel? SelectedImage
        {
            get => _selectedImage;
            set => SetProperty(ref _selectedImage, value);
        }

        private System.Threading.CancellationTokenSource? _searchCts;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }


        public string SourceFolder
        {
            get => _sourceFolder;
            set
            {
                if (SetProperty(ref _sourceFolder, value))
                {
                    SettingsService.Save(new AppSettings { LastSourceFolder = value });
                }
            }
        }


        public ObservableCollection<FolderNode> FolderTree
        {
            get => _folderTree;
            set => SetProperty(ref _folderTree, value);
        }

        public FolderNode? SelectedFolderNode
        {
            get => _selectedFolderNode;
            set
            {
                if (SetProperty(ref _selectedFolderNode, value))
                {
                    ApplyFilter();
                }
            }
        }

        public bool IsFullImageMode
        {
            get => _isFullImageMode;
            set => SetProperty(ref _isFullImageMode, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int SelectedCount => _allImages.Count(i => i.IsSelected);
        public bool HasSelection => SelectedCount > 0;

        public ICommand SelectFolderCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand OpenInExplorerCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ClosePreviewCommand { get; }
        public ICommand OpenPreviewCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand NextImageCommand { get; }
        public ICommand PreviousImageCommand { get; }


        private async Task SelectFolder()
        {
            var picker = new Snappr.Views.FolderPickerWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (picker.ShowDialog() == true)
            {
                InitializeFolder(picker.SelectedPath);
            }
        }

        private async void InitializeFolder(string path)
        {
            SourceFolder = path;
            lock (_allImages)
            {
                _allImages.Clear();
                _allImagesLookup.Clear();
                _loadedFolders.Clear();
            }

            // Perform initial fast scan of current folder
            await LoadImages(path, false);
            LoadFolderTree(path);

            // Trigger background recursive scan of EVERYTHING to build index
            _ = ScanEverythingBackground(path);
        }

        private async Task ScanEverythingBackground(string path)
        {
            IsDeepScanning = true;
            StatusMessage = "Indexing subfolders...";
            try
            {
                // Background scan should NOT set IsBusy (which shows the overlay)
                await LoadImages(path, true, showOverlay: false);
                StatusMessage = "Indexing complete.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Indexing error: {ex.Message}";
            }
            finally
            {
                IsDeepScanning = false;
                ApplyFilter(); // Refresh view
            }
        }


        private void LoadFolderTree(string path)
        {
            FolderTree.Clear();
            var root = new FolderNode { Name = Path.GetFileName(path), FullPath = path };
            if (string.IsNullOrEmpty(root.Name)) root.Name = path;
            root.LoadSubFolders();
            FolderTree.Add(root);
        }

        private HashSet<string> _loadedFolders = new HashSet<string>();

        private bool _isDeepScanning = false;
        public bool IsDeepScanning
        {
            get => _isDeepScanning;
            set 
            {
                if (SetProperty(ref _isDeepScanning, value))
                {
                    OnPropertyChanged(nameof(InProgress));
                    OnPropertyChanged(nameof(IsProgressBarIndeterminate));
                }
            }
        }

        private bool _isSearching = false;
        public bool IsSearching
        {
            get => _isSearching;
            set 
            {
                if (SetProperty(ref _isSearching, value))
                {
                    OnPropertyChanged(nameof(InProgress));
                    OnPropertyChanged(nameof(IsProgressBarIndeterminate));
                }
            }
        }

        public bool InProgress => IsDeepScanning || IsSearching;

        public bool IsProgressBarIndeterminate => IsDeepScanning && !IsSearching;

        public int TotalImagesCount => _allImages.Count;

        private int _searchProcessedCount;
        public int SearchProcessedCount
        {
            get => _searchProcessedCount;
            set => SetProperty(ref _searchProcessedCount, value);
        }

        private int _searchTotalCount;
        public int SearchTotalCount
        {
            get => _searchTotalCount;
            set => SetProperty(ref _searchTotalCount, value);
        }

        private Func<ImageModel, bool> CompileSearch(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return _ => true;

            var logicGroups = searchText.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList())
                .Where(g => g.Any())
                .ToList();

            if (!logicGroups.Any()) return _ => true;

            var orPredicates = new List<Func<ImageModel, bool>>();

            foreach (var group in logicGroups)
            {
                var currentGroup = group; 
                var andPredicates = new List<Func<ImageModel, bool>>();
                foreach (var term in currentGroup)
                {
                    var currentTerm = term; 
                    
                    // Handle exact match (quotes)
                    bool isExact = currentTerm.StartsWith("\"") && currentTerm.EndsWith("\"") && currentTerm.Length >= 2;
                    string effectiveTerm = isExact ? currentTerm.Substring(1, currentTerm.Length - 2) : currentTerm;

                    if (effectiveTerm.StartsWith("gps=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = effectiveTerm.Substring(4).Trim();
                        andPredicates.Add(img => img.Location != null && img.Location.Contains(val, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (effectiveTerm.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = effectiveTerm.Substring(5).Trim();
                        if (isExact)
                        {
                            andPredicates.Add(img => string.Equals(img.FileName, val, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var regex = new Regex(val.Contains("*") ? "^" + Regex.Escape(val).Replace("\\*", ".*") + "$" : Regex.Escape(val), RegexOptions.IgnoreCase);
                            andPredicates.Add(img => !string.IsNullOrEmpty(img.FileName) && regex.IsMatch(img.FileName));
                        }
                    }
                    else if (effectiveTerm.StartsWith("folder=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = effectiveTerm.Substring(7).Trim();
                        if (isExact)
                        {
                            andPredicates.Add(img => string.Equals(img.FolderPath, val, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var regex = new Regex(val.Contains("*") ? "^" + Regex.Escape(val).Replace("\\*", ".*") + "$" : Regex.Escape(val), RegexOptions.IgnoreCase);
                            andPredicates.Add(img => !string.IsNullOrEmpty(img.FolderPath) && regex.IsMatch(img.FolderPath));
                        }
                    }
                    else if (effectiveTerm.StartsWith("date=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(effectiveTerm.Substring(5), out var d)) andPredicates.Add(img => img.DateTaken?.Date == d.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else if (effectiveTerm.StartsWith("month=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(effectiveTerm.Substring(6), out var m)) andPredicates.Add(img => img.DateTaken?.Month == m);
                        else andPredicates.Add(_ => false);
                    }
                    else if (effectiveTerm.StartsWith("year=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(effectiveTerm.Substring(5), out var y)) andPredicates.Add(img => img.DateTaken?.Year == y);
                        else andPredicates.Add(_ => false);
                    }
                    else if (effectiveTerm.StartsWith("datebefore=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(effectiveTerm.Substring(11), out var d)) andPredicates.Add(img => img.DateTaken?.Date <= d.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else if (effectiveTerm.StartsWith("dateafter=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(effectiveTerm.Substring(10), out var d)) andPredicates.Add(img => img.DateTaken?.Date >= d.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else if (effectiveTerm.StartsWith("datebetween=", StringComparison.OrdinalIgnoreCase))
                    {
                        var s = effectiveTerm.Substring(12).Trim();
                        DateTime? d1 = null, d2 = null;
                        for (int i = 1; i < s.Length - 1; i++) {
                            if (s[i] == '-' && DateTime.TryParse(s.Substring(0, i).Trim(), out var dt1) && DateTime.TryParse(s.Substring(i + 1).Trim(), out var dt2)) {
                                d1 = dt1; d2 = dt2; break;
                            }
                        }
                        if (d1.HasValue && d2.HasValue) andPredicates.Add(img => img.DateTaken?.Date >= d1.Value.Date && img.DateTaken?.Date <= d2.Value.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else
                    {
                        if (isExact)
                        {
                            andPredicates.Add(img => 
                                (img.FileName != null && string.Equals(img.FileName, effectiveTerm, StringComparison.OrdinalIgnoreCase)) || 
                                (img.Keywords != null && img.Keywords.Any(k => string.Equals(k, effectiveTerm, StringComparison.OrdinalIgnoreCase))) ||
                                (img.Location != null && string.Equals(img.Location, effectiveTerm, StringComparison.OrdinalIgnoreCase)));
                        }
                        else
                        {
                            var regexString = effectiveTerm.Contains("*") ? "^" + Regex.Escape(effectiveTerm).Replace("\\*", ".*") + "$" : Regex.Escape(effectiveTerm);
                            var regex = new Regex(regexString, RegexOptions.IgnoreCase);
                            andPredicates.Add(img => 
                                (img.FileName != null && regex.IsMatch(img.FileName)) || 
                                (img.Keywords != null && img.Keywords.Any(k => k != null && regex.IsMatch(k))) ||
                                (img.Location != null && regex.IsMatch(img.Location)));
                        }
                    }
                }
                var capturedAnds = andPredicates.ToList();
                orPredicates.Add(img => img != null && capturedAnds.All(p => p(img)));
            }

            var capturedOrs = orPredicates.ToList();
            return img => img != null && capturedOrs.Any(p => p(img));
        }





        private async void ApplyFilter()
        {
            var search = SearchText.Trim();
            
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            var token = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(search))
            {
                IsSearching = false;
                IsDeepScanning = false;
                StatusMessage = "Ready";
                
                if (SelectedFolderNode != null)
                {
                    List<ImageModel> folderImages;
                    lock (_allImages)
                    {
                        folderImages = _allImages.Where(i => i.FolderPath == SelectedFolderNode.FullPath).ToList();
                    }
                    FilteredImages = new ObservableCollection<ImageModel>(folderImages);
                }
                else
                {
                    FilteredImages = new ObservableCollection<ImageModel>();
                }
                return;
            }

            // Simple fast in-memory filtering
            var searchContext = CompileSearch(search);
            StatusMessage = "Searching...";
            
            lock (_allImages)
            {
                SearchTotalCount = _allImages.Count;
            }
            SearchProcessedCount = 0;
            IsSearching = true;

            try 
            {
                // Find matches in snapshot off-thread to avoid UI freeze
                var matches = await Task.Run(() => 
                {
                    List<ImageModel> snapshot;
                    lock (_allImages)
                    {
                        snapshot = _allImages.ToList();
                    }

                    var results = new List<ImageModel>();

                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        if (token.IsCancellationRequested) return null;

                        if (searchContext(snapshot[i]))
                        {
                            results.Add(snapshot[i]);
                        }

                        // Update every 10 images to make it look "live" for medium/large libraries
                        if (i % 10 == 0 || i == snapshot.Count - 1)
                        {
                            var processed = i + 1;
                            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
                            {
                                SearchProcessedCount = processed;
                            }, System.Windows.Threading.DispatcherPriority.Background);
                            
                            // If search is too fast, the UI won't have time to render the changes.
                            // Task.Yield() allows the scheduler to process other tasks (like UI updates).
                            if (i % 500 == 0) Task.Yield().GetAwaiter().GetResult();
                        }
                    }
                    return results;
                }, token);

                if (token.IsCancellationRequested || matches == null) return;

                // Update UI once with the new collection to avoid multiple notifications
                FilteredImages = new ObservableCollection<ImageModel>(matches);
                StatusMessage = $"Found {FilteredImages.Count} results";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
            finally
            {
                IsSearching = false;
            }
        }

        // Helper method removed as no longer needed for search walk
        private void FlushBatch(System.Collections.Concurrent.ConcurrentBag<ImageModel> batch) { }










        private async Task LoadImages(string path, bool recursive = false, bool showOverlay = true)
        {
            if (_loadedFolders.Contains(path) && !recursive) return;
            
            if (showOverlay) IsBusy = true;
            StatusMessage = $"Scanning {Path.GetFileName(path)}...";
            try
            {
                var newImages = await _imageService.ScanFolderAsync(path, recursive);
                lock (_allImages)
                {
                    foreach (var img in newImages)
                    {
                        if (!_allImagesLookup.ContainsKey(img.FilePath))
                        {
                            img.PropertyChanged += ImageModel_PropertyChanged;
                            _allImages.Add(img);
                            _allImagesLookup[img.FilePath] = img;
                        }
                    }
                }
                OnPropertyChanged(nameof(TotalImagesCount));

                _loadedFolders.Add(path);
                StatusMessage = $"Found {newImages.Count} images. Extracting metadata in background...";
                
                // Background metadata loading
                _ = Task.Run(async () => 
                {
                    foreach (var img in newImages)
                    {
                        await _imageService.EnrichMetadataAsync(img);
                    }
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        StatusMessage = $"Ready. {newImages.Count} images fully loaded.";
                        ApplyFilter(); // Refresh once meta is in
                    });
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (showOverlay) IsBusy = false;
                ApplyFilter();
            }
        }


        private async Task ExportImage(string? mode)
        {
            var selectedImages = _allImages.Where(i => i.IsSelected).ToList();
            if (!selectedImages.Any() && SelectedImage != null)
            {
                selectedImages.Add(SelectedImage);
            }

            if (!selectedImages.Any() || mode == null) return;

            double scale = 1.0;
            switch (mode)
            {
                case "Half": scale = 0.5; break;
                case "Quarter": scale = 0.25; break;
                case "Custom":
                    scale = 0.1; 
                    break;
                default: scale = 1.0; break;
            }


            var picker = new Snappr.Views.FolderPickerWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (picker.ShowDialog() == true)
            {
                var targetFolder = picker.SelectedPath;
                IsBusy = true;
                
                int total = selectedImages.Count;
                int current = 0;

                try
                {
                    foreach (var img in selectedImages)
                    {
                        current++;
                        StatusMessage = $"Exporting {current}/{total}: {img.FileName}...";
                        
                        var fileName = Path.GetFileNameWithoutExtension(img.FileName) + $"_{mode}" + Path.GetExtension(img.FileName);
                        var targetPath = Path.Combine(targetFolder, fileName);
                        
                        await _imageService.ExportImageAsync(img.FilePath, targetPath, scale);
                    }
                    StatusMessage = $"Export of {total} images complete.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private void OpenInExplorer()
        {
            if (SelectedImage == null) return;
            Process.Start("explorer.exe", $"/select,\"{SelectedImage.FilePath}\"");
        }

        private async Task Refresh()
        {
            if (_allImages.Any())
            {
                var folder = Path.GetDirectoryName(_allImages.First().FilePath);
                if (folder != null && Directory.Exists(folder))
                {
                    await LoadImages(folder);
                }
            }
        }

        private void ImageModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageModel.IsSelected))
            {
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        private void NextImage()
        {
            if (SelectedImage == null || !FilteredImages.Any()) return;
            int index = FilteredImages.IndexOf(SelectedImage);
            if (index >= 0 && index < FilteredImages.Count - 1)
            {
                SelectedImage = FilteredImages[index + 1];
            }
        }

        private void PreviousImage()
        {
            if (SelectedImage == null || !FilteredImages.Any()) return;
            int index = FilteredImages.IndexOf(SelectedImage);
            if (index > 0)
            {
                SelectedImage = FilteredImages[index - 1];
            }
        }
    }
}
