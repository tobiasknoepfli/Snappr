using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Snappr.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;



namespace Snappr.Services
{
    public class ImageService
    {
        public static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".webp" };


        private readonly Dictionary<string, List<ImageModel>> _cache = new Dictionary<string, List<ImageModel>>();

        public async Task<List<ImageModel>> ScanFolderAsync(string folderPath, bool recursive = false)
        {
            if (_cache.TryGetValue(folderPath + recursive, out var cached)) return cached;

            var results = await Task.Run(() =>
            {
                var images = new List<ImageModel>();
                if (!System.IO.Directory.Exists(folderPath)) return images;

                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = System.IO.Directory.EnumerateFiles(folderPath, "*.*", option)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()));

                foreach (var file in files)
                {
                    images.Add(new ImageModel
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file) ?? "Unknown",
                        FolderPath = Path.GetDirectoryName(file) ?? "Unknown",
                        MetadataSummary = "Pending..."
                    });
                }
                return images;
            });

            _cache[folderPath + recursive] = results;
            return results;
        }

        public async Task EnrichMetadataAsync(ImageModel model)
        {
            if (model.MetadataSummary != "Pending...") return;

            await Task.Run(() =>
            {
                try
                {
                    var enriched = ExtractMetadata(model.FilePath);
                    model.DateTaken = enriched.DateTaken;
                    model.Keywords.Clear();
                    model.Keywords.AddRange(enriched.Keywords);
                    model.MetadataSummary = enriched.MetadataSummary;
                }
                catch { model.MetadataSummary = "Error"; }
            });
        }



        private ImageModel ExtractMetadata(string filePath)
        {
            var model = new ImageModel
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath) ?? "Unknown",
                FolderPath = Path.GetDirectoryName(filePath) ?? "Unknown"
            };


            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var metaPairs = new List<string>();

                // Get EXIF SubIFD for camera settings
                var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (subIfdDirectory != null)
                {
                    if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
                        model.DateTaken = dateTime;

                    var iso = subIfdDirectory.GetString(ExifDirectoryBase.TagIsoEquivalent);
                    var f = subIfdDirectory.GetString(ExifDirectoryBase.TagFNumber);
                    var s = subIfdDirectory.GetString(ExifDirectoryBase.TagExposureTime);
                    
                    if (!string.IsNullOrEmpty(iso)) metaPairs.Add($"ISO {iso}");
                    if (!string.IsNullOrEmpty(f)) metaPairs.Add($"f/{f}");
                    if (!string.IsNullOrEmpty(s)) metaPairs.Add($"{s}s");
                }

                // Get EXIF IFD0 for Camera Model and dimensions
                var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                if (ifd0Directory != null)
                {
                    var make = ifd0Directory.GetString(ExifDirectoryBase.TagMake);
                    var camModel = ifd0Directory.GetString(ExifDirectoryBase.TagModel);
                    if (!string.IsNullOrEmpty(camModel)) metaPairs.Add(camModel);
                    
                    var w = ifd0Directory.GetString(ExifDirectoryBase.TagImageWidth);
                    var h = ifd0Directory.GetString(ExifDirectoryBase.TagImageHeight);
                    if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h)) metaPairs.Add($"{w}x{h}");
                }

                // Get Keywords (IPTC)
                var iptcDirectory = directories.OfType<IptcDirectory>().FirstOrDefault();
                if (iptcDirectory != null)
                {
                    var keywords = iptcDirectory.GetStringArray(IptcDirectory.TagKeywords);
                    if (keywords != null) model.Keywords.AddRange(keywords);

                    var city = iptcDirectory.GetString(90); // City
                    var prov = iptcDirectory.GetString(95); // Province/State
                    var country = iptcDirectory.GetString(101); // Country
                    
                    var locationParts = new[] { city, prov, country }.Where(s => !string.IsNullOrWhiteSpace(s));
                    model.Location = string.Join(", ", locationParts);
                }

                model.MetadataSummary = string.Join(" | ", metaPairs);
                if (!string.IsNullOrEmpty(model.Location))
                {
                    if (!string.IsNullOrEmpty(model.MetadataSummary)) model.MetadataSummary += " — ";
                    model.MetadataSummary += model.Location;
                }

                if (string.IsNullOrEmpty(model.MetadataSummary))
                {
                    model.MetadataSummary = "Basic Image Data";
                }
            }
            catch
            {
                model.MetadataSummary = "Metadata Error";
            }

            return model;
        }

        public async Task ExportImageAsync(string sourcePath, string targetPath, double scale)
        {
            await Task.Run(() =>
            {
                using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var bitmap = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    
                    var scaleTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                    var scaledBitmap = new System.Windows.Media.Imaging.TransformedBitmap(bitmap, scaleTransform);

                    var encoder = GetEncoder(targetPath);
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaledBitmap));

                    using (var outStream = new FileStream(targetPath, FileMode.Create))
                    {
                        encoder.Save(outStream);
                    }
                }
            });
        }


        private System.Windows.Media.Imaging.BitmapEncoder GetEncoder(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".png" => new System.Windows.Media.Imaging.PngBitmapEncoder(),
                ".bmp" => new System.Windows.Media.Imaging.BmpBitmapEncoder(),
                ".tiff" => new System.Windows.Media.Imaging.TiffBitmapEncoder(),
                _ => new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 90 }
            };
        }
    }
}
