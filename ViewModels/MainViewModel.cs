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
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _searchCts?.Cancel();
                    _searchCts = new System.Threading.CancellationTokenSource();
                    var token = _searchCts.Token;
                    Task.Delay(300, token).ContinueWith(t => 
                    {
                        if (!t.IsCanceled)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyFilter());
                        }
                    }, token);
                }
            }
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

        public ICommand SelectFolderCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand OpenInExplorerCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ClosePreviewCommand { get; }
        public ICommand OpenPreviewCommand { get; }
        public ICommand ClearSearchCommand { get; }


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
            _allImages.Clear();
            _allImagesLookup.Clear();
            _loadedFolders.Clear();

            await LoadImages(path, false);
            LoadFolderTree(path);
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
            set => SetProperty(ref _isDeepScanning, value);
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
                var currentGroup = group; // Local copy for closure
                var andPredicates = new List<Func<ImageModel, bool>>();
                foreach (var term in currentGroup)
                {
                    var currentTerm = term; // Local copy for closure
                    if (currentTerm.StartsWith("gps=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = currentTerm.Substring(4).Trim();
                        andPredicates.Add(img => img.Location != null && img.Location.Contains(val, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (currentTerm.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = currentTerm.Substring(5).Trim();
                        var regex = new Regex(val.Contains("*") ? "^" + Regex.Escape(val).Replace("\\*", ".*") + "$" : Regex.Escape(val), RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        andPredicates.Add(img => !string.IsNullOrEmpty(img.FileName) && regex.IsMatch(img.FileName));
                    }
                    else if (currentTerm.StartsWith("folder=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = currentTerm.Substring(7).Trim();
                        var regex = new Regex(val.Contains("*") ? "^" + Regex.Escape(val).Replace("\\*", ".*") + "$" : Regex.Escape(val), RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        andPredicates.Add(img => !string.IsNullOrEmpty(img.FolderPath) && regex.IsMatch(img.FolderPath));
                    }
                    else if (currentTerm.StartsWith("date=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(currentTerm.Substring(5), out var d)) andPredicates.Add(img => img.DateTaken?.Date == d.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else if (currentTerm.StartsWith("month=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(currentTerm.Substring(6), out var m)) andPredicates.Add(img => img.DateTaken?.Month == m);
                        else andPredicates.Add(_ => false);
                    }
                    else if (currentTerm.StartsWith("year=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(currentTerm.Substring(5), out var y)) andPredicates.Add(img => img.DateTaken?.Year == y);
                        else andPredicates.Add(_ => false);
                    }
                    else if (currentTerm.StartsWith("datebefore=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(currentTerm.Substring(11), out var d)) andPredicates.Add(img => img.DateTaken?.Date <= d.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else if (currentTerm.StartsWith("dateafter=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(currentTerm.Substring(10), out var d)) andPredicates.Add(img => img.DateTaken?.Date >= d.Date);
                        else andPredicates.Add(_ => false);
                    }
                    else if (currentTerm.StartsWith("datebetween=", StringComparison.OrdinalIgnoreCase))
                    {
                        var s = currentTerm.Substring(12).Trim();
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
                        var regex = new Regex(currentTerm.Contains("*") ? "^" + Regex.Escape(currentTerm).Replace("\\*", ".*") + "$" : Regex.Escape(currentTerm), RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        andPredicates.Add(img => 
                            (img.FileName != null && regex.IsMatch(img.FileName)) || 
                            (img.Keywords != null && img.Keywords.Any(k => k != null && regex.IsMatch(k))) ||
                            (img.Location != null && regex.IsMatch(img.Location)));
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

            // Clear current view
            FilteredImages.Clear();

            if (string.IsNullOrWhiteSpace(search))
            {
                IsDeepScanning = false;
                if (SelectedFolderNode != null)
                {
                    if (!_loadedFolders.Contains(SelectedFolderNode.FullPath))
                    {
                        await LoadImages(SelectedFolderNode.FullPath, false);
                    }
                    var folderImages = _allImages.Where(i => i.FolderPath == SelectedFolderNode.FullPath).ToList();
                    foreach (var img in folderImages) FilteredImages.Add(img);
                }
                return;
            }

            // Compiled Search Context for max speed
            var searchContext = CompileSearch(search);
            IsDeepScanning = true;

            StatusMessage = "Searching...";
            _ = Task.Run(async () => 
            {
                try 
                {
                    var addedFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                    
                    // Trigger metadata enrichment if any term looks for keywords, location, or dates
                    bool needsEnrichment = search.Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(t => !t.StartsWith("name=", StringComparison.OrdinalIgnoreCase) && 
                                 !t.StartsWith("folder=", StringComparison.OrdinalIgnoreCase));

                    // 1. Process library

                    List<ImageModel> snapshot;
                    HashSet<string> existingPaths;
                    lock (_allImages)
                    {
                        snapshot = _allImages.ToList();
                        existingPaths = new HashSet<string>(_allImagesLookup.Keys, StringComparer.OrdinalIgnoreCase);
                    }

                    var matches = snapshot.AsParallel().WithCancellation(token).Where(i => searchContext(i)).ToList();
                    if (matches.Any())
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                            foreach (var img in matches) {
                                if (token.IsCancellationRequested) return;
                                if (addedFiles.TryAdd(img.FilePath, 0)) FilteredImages.Add(img);
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }

                    // 2. Scan folders if needed
                    if (string.IsNullOrEmpty(SourceFolder) || !Directory.Exists(SourceFolder))
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => StatusMessage = "Ready");
                        return;
                    }

                    var allFiles = Directory.EnumerateFiles(SourceFolder, "*.*", SearchOption.AllDirectories);
                    var batch = new System.Collections.Concurrent.ConcurrentBag<ImageModel>();
                    
                    try {
                        Parallel.ForEach(allFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token }, (file) => {
                            var ext = Path.GetExtension(file).ToLower();
                            if (!ImageService.SupportedExtensions.Contains(ext)) return;

                            if (existingPaths.Contains(file)) return;

                            var target = new ImageModel { 
                                FilePath = file, 
                                FileName = Path.GetFileName(file) ?? "Unknown",
                                FolderPath = Path.GetDirectoryName(file) ?? "Unknown",
                                MetadataSummary = "Pending..."
                            };

                            bool matchesSearch = searchContext(target);
                            if (!matchesSearch && needsEnrichment)
                            {
                                _imageService.EnrichMetadataAsync(target).Wait();
                                matchesSearch = searchContext(target);
                            }

                            if (matchesSearch && addedFiles.TryAdd(file, 0))
                            {
                                batch.Add(target);
                                if (batch.Count >= 30) FlushBatch(batch);
                            }
                        });
                    } catch (OperationCanceledException) { }

                    FlushBatch(batch);
                }
                catch (Exception ex) { Debug.WriteLine(ex.Message); }
                finally 
                { 
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        IsDeepScanning = false;
                        StatusMessage = $"Found {FilteredImages.Count} results";
                    });
                }
            }, token);
        }

        private void FlushBatch(System.Collections.Concurrent.ConcurrentBag<ImageModel> batch)
        {
            var toFlush = new List<ImageModel>();
            while (batch.TryTake(out var item) && toFlush.Count < 50) toFlush.Add(item);
            if (!toFlush.Any()) return;

            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                foreach (var img in toFlush) {
                    lock (_allImages) {
                        if (!_allImagesLookup.ContainsKey(img.FilePath)) {
                            _allImages.Add(img);
                            _allImagesLookup[img.FilePath] = img;
                        }
                    }
                    FilteredImages.Add(img);
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }










        private async Task LoadImages(string path, bool recursive = false)
        {
            if (_loadedFolders.Contains(path) && !recursive) return;
            
            IsBusy = true;
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
                            _allImages.Add(img);
                            _allImagesLookup[img.FilePath] = img;
                        }
                    }
                }

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
                IsBusy = false;
                ApplyFilter();
            }
        }


        private async Task ExportImage(string? mode)
        {
            if (SelectedImage == null || mode == null) return;

            double scale = 1.0;
            switch (mode)
            {
                case "Half": scale = 0.5; break;
                case "Quarter": scale = 0.25; break;
                case "Custom":
                    // For brevity, let's assume 10% in this mock but in real app we'd ask
                    scale = 0.1; 
                    break;
                default: scale = 1.0; break;
            }


            var saveDialog = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(SelectedImage.FileName) + $"_{mode}" + Path.GetExtension(SelectedImage.FileName),
                Filter = "Image Files|*.jpg;*.png;*.bmp"
            };

            if (saveDialog.ShowDialog() == true)
            {
                IsBusy = true;
                StatusMessage = "Exporting...";
                try
                {
                    await _imageService.ExportImageAsync(SelectedImage.FilePath, saveDialog.FileName, scale);
                    StatusMessage = "Export complete.";
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
                if (Directory.Exists(folder))
                {
                    await LoadImages(folder);
                }
            }
        }
    }
}
