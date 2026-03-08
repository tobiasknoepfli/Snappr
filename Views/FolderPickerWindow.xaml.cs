using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Snappr.Views
{
    public partial class FolderPickerWindow : Window, INotifyPropertyChanged
    {
        private string _currentPath = string.Empty;
        private string _targetFolder = string.Empty;
        private Stack<string> _history = new Stack<string>();
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public string SelectedPath { get; private set; } = string.Empty;

        public string CurrentPath
        {
            get => _currentPath;
            set { _currentPath = value; OnPropertyChanged(); }
        }

        public string TargetFolder
        {
            get => _targetFolder;
            set { _targetFolder = value; OnPropertyChanged(); }
        }

        public bool CanGoBack => _history.Count > 0;

        public FolderPickerWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadQuickAccess();
            LoadDrives();
            
            // Start at user's home folder if possible
            NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        private void LoadQuickAccess()
        {
            var items = new List<FolderItem>
            {
                new FolderItem { Name = "Desktop", FullPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Icon = "🖥️" },
                new FolderItem { Name = "Documents", FullPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Icon = "📄" },
                new FolderItem { Name = "Pictures", FullPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Icon = "🖼️" },
                new FolderItem { Name = "Downloads", FullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Icon = "⬇️" }
            };
            QuickAccessList.ItemsSource = items;
        }

        private void LoadDrives()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new FolderItem { Name = d.Name, FullPath = d.RootDirectory.FullName, Icon = "💽" })
                .ToList();
            DrivesList.ItemsSource = drives;
        }

        private void NavigateTo(string path, bool addToHistory = true)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                var directories = Directory.GetDirectories(path)
                    .Select(d => new FolderItem { Name = Path.GetFileName(d), FullPath = d, Icon = "📁" })
                    .OrderBy(f => f.Name)
                    .ToList();

                ItemListBox.ItemsSource = directories;
                
                if (addToHistory && !string.IsNullOrEmpty(CurrentPath))
                {
                    _history.Push(CurrentPath);
                    OnPropertyChanged(nameof(CanGoBack));
                }

                CurrentPath = path;
                TargetFolder = Path.GetFileName(path);
                if (string.IsNullOrEmpty(TargetFolder)) TargetFolder = path; // For drives
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder: {ex.Message}");
            }
        }

        private void ItemListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemListBox.SelectedItem is FolderItem item)
            {
                NavigateTo(item.FullPath);
            }
        }

        private void ItemListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ItemListBox.SelectedItem is FolderItem item)
            {
                TargetFolder = item.Name;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count > 0)
            {
                NavigateTo(_history.Pop(), false);
                OnPropertyChanged(nameof(CanGoBack));
            }
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                NavigateTo(parent.FullName);
            }
        }

        private void PathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                NavigateTo(CurrentPath);
            }
        }

        private void QuickAccessList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (QuickAccessList.SelectedItem is FolderItem item)
            {
                NavigateTo(item.FullPath);
                QuickAccessList.SelectedItem = null;
            }
        }

        private void DrivesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DrivesList.SelectedItem is FolderItem item)
            {
                NavigateTo(item.FullPath);
                DrivesList.SelectedItem = null;
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            string finalPath = CurrentPath;
            if (ItemListBox.SelectedItem is FolderItem item)
            {
                finalPath = item.FullPath;
            }

            if (Directory.Exists(finalPath))
            {
                SelectedPath = finalPath;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class FolderItem
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public string Icon { get; set; } = "📁";
            public string IconColor => "#0078D4";
        }
    }
}
