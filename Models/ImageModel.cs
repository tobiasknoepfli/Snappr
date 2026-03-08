using System;
using System.Collections.Generic;

namespace Snappr.Models
{
    public class ImageModel : Snappr.ViewModels.ViewModelBase
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        
        private DateTime? _dateTaken;
        public DateTime? DateTaken 
        { 
            get => _dateTaken; 
            set => SetProperty(ref _dateTaken, value); 
        }

        public List<string> Keywords { get; set; } = new List<string>();
        
        private string _metadataSummary = string.Empty;
        public string MetadataSummary 
        { 
            get => _metadataSummary; 
            set => SetProperty(ref _metadataSummary, value); 
        }

        public string Location { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }


        
        // UI Helpers
        public string DisplayKeywords => string.Join(", ", Keywords);
    }
}
