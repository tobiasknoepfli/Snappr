using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Snappr.Models
{
    public class FolderNode : Snappr.ViewModels.ViewModelBase
    {
        private string _name = string.Empty;
        public string Name 
        { 
            get => _name; 
            set => SetProperty(ref _name, value); 
        }
        
        public string FullPath { get; set; } = string.Empty;
        public ObservableCollection<FolderNode> SubFolders { get; set; } = new ObservableCollection<FolderNode>();

        
        private bool _isLoaded = false;
        private bool _isExpanded;

        public bool IsExpanded
        {
            get => _isExpanded;
            set 
            { 
                _isExpanded = value; 
                if (_isExpanded) LoadSubFolders();
            }
        }

        public void LoadSubFolders()
        {
            if (_isLoaded) return;
            _isLoaded = true;
            
            SubFolders.Clear();
            try
            {
                var dirs = Directory.GetDirectories(FullPath);
                foreach (var dir in dirs)
                {
                    var node = new FolderNode { Name = Path.GetFileName(dir), FullPath = dir };
                    // Add dummy if folder has subdirectories
                    if (Directory.GetDirectories(dir).Any())
                    {
                        node.SubFolders.Add(new FolderNode { Name = "Loading..." });
                    }
                    SubFolders.Add(node);
                }
            }
            catch { }
        }
    }

}
