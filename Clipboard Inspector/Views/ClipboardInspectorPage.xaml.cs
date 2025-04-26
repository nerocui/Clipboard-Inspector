using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Clipboard_Inspector.Models;
using Clipboard_Inspector.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace Clipboard_Inspector.Views
{
    public sealed partial class ClipboardInspectorPage : Page, INotifyPropertyChanged
    {
        private ClipboardService _clipboardService;
        private List<ClipboardDataFormat> _formats;
        
        // Format type constants for SwitchPresenter
        public enum FormatType
        {
            None,
            Text,
            HTML,
            Image,
            Link,
            RichText,
            DataObject,
            OleData
        }
        
        // Property for binding to SwitchPresenter
        private FormatType _currentFormatType = FormatType.None;
        public FormatType CurrentFormatType
        {
            get => _currentFormatType;
            set
            {
                if (_currentFormatType != value)
                {
                    _currentFormatType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentFormatTypeString));
                }
            }
        }

        // String version of CurrentFormatType for SwitchPresenter to use
        public string CurrentFormatTypeString => _currentFormatType.ToString();

        // Image format identifiers
        private static readonly int[] ImageFormatIds = new[] 
        { 
            2,      // CF_BITMAP
            8,      // CF_DIB (Device Independent Bitmap)
            17,     // CF_DIBV5 (Enhanced Device Independent Bitmap)
            49379   // PNG format
        };

        // Link format identifiers
        private static readonly int[] LinkFormatIds = new[]
        {
            49717,  // Link Preview Format
            49723   // Titled Hyperlink Format
        };

        // Format identifiers for standard formats
        private static readonly Dictionary<int, string> StandardFormatIds = new Dictionary<int, string>
        {
            { 1, "CF_TEXT" },               // Plain text
            { 13, "CF_UNICODETEXT" },       // Unicode text
            { 16, "CF_LOCALE" },            // Locale identifier
            { 49161, "DataObject" },        // DataObject format (specific to some applications)
            { 49171, "Ole Private Data" },  // OLE private data
            { 49372, "Rich Text Format" },  // RTF
            // Add more standard formats as needed
        };
        
        public ClipboardInspectorPage()
        {
            this.InitializeComponent();
            _clipboardService = new ClipboardService();
            
            // Load clipboard data when page is loaded
            this.Loaded += ClipboardInspectorPage_Loaded;
        }

        private void ClipboardInspectorPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshClipboardData();
            
            // Initialize WebView2 when loaded
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                // Ensure WebView2 is initialized
                await HtmlPreview.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"WebView2 initialization error: {ex.Message}";
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshClipboardData();
        }

        private void RefreshClipboardData()
        {
            try
            {
                StatusTextBlock.Text = "Loading clipboard data...";
                
                _formats = _clipboardService.GetClipboardFormats();
                FormatsDataGrid.ItemsSource = _formats;
                
                StatusTextBlock.Text = $"Found {_formats.Count} clipboard formats";
                
                // Select first item if available
                if (_formats.Count > 0)
                {
                    FormatsDataGrid.SelectedIndex = 0;
                }
                else
                {
                    ContentEditor.Editor.SetText("No clipboard data available");
                    CurrentFormatType = FormatType.None;
                    ContentPivot.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
                ContentEditor.Editor.SetText($"Failed to retrieve clipboard data: {ex.Message}");
                CurrentFormatType = FormatType.None;
                ContentPivot.SelectedIndex = 0;
            }
        }

        private void FormatsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatsDataGrid.SelectedItem is ClipboardDataFormat selectedFormat)
            {
                try
                {
                    // Determine format type
                    bool isHtmlFormat = IsHtmlFormat(selectedFormat);
                    bool isImageFormat = IsImageFormat(selectedFormat);
                    bool isLinkFormat = IsLinkFormat(selectedFormat);
                    bool isRichTextFormat = IsRichTextFormat(selectedFormat);
                    bool isUnicodeText = IsUnicodeText(selectedFormat);
                    bool isOleData = IsOleData(selectedFormat);
                    bool isDataObject = IsDataObject(selectedFormat);
                    bool isLocaleFormat = IsLocaleFormat(selectedFormat);
                    
                    // Process the data for the Source view
                    ProcessForSourceView(selectedFormat, isHtmlFormat, isImageFormat, isLinkFormat,
                                        isRichTextFormat, isUnicodeText, isOleData, isDataObject,
                                        isLocaleFormat);
                    
                    // Try to prepare preview in a separate try-catch block
                    try
                    {
                        // Prepare preview based on format type
                        PreparePreview(selectedFormat, isHtmlFormat, isImageFormat, isLinkFormat,
                                      isRichTextFormat, isUnicodeText, isOleData, isDataObject,
                                      isLocaleFormat);
                    }
                    catch (Exception previewEx)
                    {
                        // Log preview error but don't affect the source view
                        StatusTextBlock.Text = $"Preview error: {previewEx.Message}";
                        CurrentFormatType = FormatType.None;
                    }
                    
                    // Switch to source view by default for consistency
                    ContentPivot.SelectedIndex = 0;
                }
                catch (Exception ex)
                {
                    ContentEditor.Editor.SetText($"Error generating source view: {ex.Message}");
                    ContentEditor.HighlightingLanguage = "plaintext";
                    CurrentFormatType = FormatType.None;
                    ContentPivot.SelectedIndex = 0;
                }
            }
            else
            {
                ContentEditor.Editor.SetText(string.Empty);
                CurrentFormatType = FormatType.None;
                ContentPivot.SelectedIndex = 0;
            }
        }
        
        private void ProcessForSourceView(ClipboardDataFormat format, bool isHtmlFormat, bool isImageFormat, bool isLinkFormat,
                                          bool isRichTextFormat, bool isUnicodeText, bool isOleData, bool isDataObject,
                                          bool isLocaleFormat)
        {
            bool isHtmlLinkFormat = IsHtmlLinkFormat(format);
            bool isJsonLinkFormat = IsJsonLinkFormat(format);
            
            if (isImageFormat)
            {
                // For image formats, show a hex dump in the source view
                string hexPreview = GenerateHexDump(format.Data, 16);
                SafeSetEditorText(ContentEditor, hexPreview, "plaintext");
            }
            else if (isHtmlFormat || isHtmlLinkFormat)
            {
                // For HTML formats, syntax highlight the raw HTML
                string htmlContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                SafeSetEditorText(ContentEditor, htmlContent, "html");
            }
            else if (isJsonLinkFormat)
            {
                // For JSON link format, use JSON formatting
                string rawContent = GetLinkRawContent(format.Data);
                string formattedJson = FormatJsonString(rawContent);
                SafeSetEditorText(ContentEditor, formattedJson, "json");
            }
            else if (isLinkFormat)
            {
                // For other link formats, try to show a readable version with proper encoding
                string rawContent = GetLinkRawContent(format.Data);
                
                // Try to detect if content is JSON
                if (IsJsonContent(rawContent))
                {
                    // Format the JSON for better readability
                    string formattedJson = FormatJsonString(rawContent);
                    SafeSetEditorText(ContentEditor, formattedJson, "json");
                }
                else
                {
                    // Not JSON, just show the raw content
                    SafeSetEditorText(ContentEditor, rawContent, "plaintext");
                }
            }
            else if (isRichTextFormat)
            {
                // For RTF formats, show the raw RTF markup
                string rtfContent = ExtractTextFromRTF(format.Data);
                SafeSetEditorText(ContentEditor, rtfContent, "rtf");
            }
            else if (isUnicodeText)
            {
                // For Unicode text, decode properly
                string textContent = Encoding.Unicode.GetString(format.Data).TrimEnd('\0');
                SafeSetEditorText(ContentEditor, textContent, "plaintext");
            }
            else if (isDataObject || isOleData || isLocaleFormat)
            {
                // For these specialized binary formats, use hex dump
                string hexPreview = GenerateHexDump(format.Data, 16);
                SafeSetEditorText(ContentEditor, hexPreview, "plaintext");
            }
            else
            {
                // For other formats, try to get a text preview first
                string preview = _clipboardService.GetPreviewForFormat(format);
                
                // Only use hex dump if the content appears to be binary
                if (preview == null || HasBinaryContent(preview))
                {
                    // Fallback to hex dump for binary formats
                    string hexPreview = GenerateHexDump(format.Data, 16);
                    SafeSetEditorText(ContentEditor, hexPreview, "plaintext");
                }
                else
                {
                    // For text formats, show the readable content
                    SafeSetEditorText(ContentEditor, preview, "plaintext");
                }
            }
        }
        
        private void PreparePreview(ClipboardDataFormat format, bool isHtmlFormat, bool isImageFormat, bool isLinkFormat,
                                    bool isRichTextFormat, bool isUnicodeText, bool isOleData, bool isDataObject,
                                    bool isLocaleFormat)
        {
            bool isHtmlLinkFormat = IsHtmlLinkFormat(format);
            bool isJsonLinkFormat = IsJsonLinkFormat(format);
            
            if (isImageFormat)
            {
                // Image format - prepare image preview
                DisplayImageContent(format);
                CurrentFormatType = FormatType.Image;
            }
            else if (isHtmlFormat || isHtmlLinkFormat)
            {
                // HTML format - prepare HTML preview
                string htmlContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                string processedHtml = ProcessHtmlClipboardContent(htmlContent);
                DisplayHtmlPreview(processedHtml);
                CurrentFormatType = FormatType.HTML;
            }
            else if (isRichTextFormat)
            {
                // Rich Text Format - prepare RTF preview
                DisplayRichTextContent(format);
                CurrentFormatType = FormatType.RichText;
            }
            else if (isJsonLinkFormat)
            {
                // JSON Link format (Link Preview Format) - prepare JSON tree view
                DisplayJsonLinkContent(format);
                CurrentFormatType = FormatType.Link;
            }
            else if (isLinkFormat)
            {
                // Other link formats - prepare link preview
                DisplayLinkContent(format);
                CurrentFormatType = FormatType.Link;
            }
            else if (isDataObject)
            {
                // DataObject format - simple text preview for now
                string preview = _clipboardService.GetPreviewForFormat(format);
                TextPreviewEditor.HighlightingLanguage = "plaintext";
                TextPreviewEditor.Editor.SetText(preview);
                CurrentFormatType = FormatType.DataObject;
            }
            else if (isOleData)
            {
                // OLE data format - simple text preview for now
                string preview = _clipboardService.GetPreviewForFormat(format);
                TextPreviewEditor.HighlightingLanguage = "plaintext";
                TextPreviewEditor.Editor.SetText(preview);
                CurrentFormatType = FormatType.OleData;
            }
            else
            {
                // Text and other formats - use the text preview
                string preview = _clipboardService.GetPreviewForFormat(format);
                TextPreviewEditor.HighlightingLanguage = "plaintext";
                TextPreviewEditor.Editor.SetText(preview);
                CurrentFormatType = FormatType.Text;
            }
        }

        private void DisplayJsonLinkContent(ClipboardDataFormat format)
        {
            if (format?.Data == null || format.Data.Length == 0)
                return;
                
            try
            {
                // Get JSON content from the format data
                string jsonContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                
                // Setup basic link preview information
                LinkPreviewFormatText.Text = format.FormatName;
                
                // Pass the JSON content to our JsonTreeViewer control
                JsonTreeViewControl.JsonContent = jsonContent;
                
                // Make the JSON tree view container visible
                JsonTreeViewContainer.Visibility = Visibility.Visible;
                
                // Try to extract information for quick access at the top
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                    {
                        ExtractLinkInfoFromJson(doc.RootElement);
                    }
                    
                    // Check if we found a URL in the JSON
                    if (string.IsNullOrEmpty(LinkPreviewUrl.Text) || LinkPreviewUrl.Text == "https://example.com")
                    {
                        // No URL found, hide the URL row
                        LinkPreviewUrlRow.Visibility = Visibility.Collapsed;
                    }
                    else 
                    {
                        // We found a URL, enable the open button
                        LinkPreviewUrlRow.Visibility = Visibility.Visible;
                        OpenLinkButton.IsEnabled = true;
                    }
                }
                catch
                {
                    // If we can't parse the JSON for URL extraction, hide the URL row
                    LinkPreviewUrlRow.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                // Show error in the link preview
                LinkPreviewUrl.Text = "Error parsing JSON";
                LinkPreviewTitle.Text = ex.Message;
                LinkPreviewTitleRow.Visibility = Visibility.Visible;
                JsonTreeViewContainer.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"JSON parsing error: {ex.Message}";
            }
        }
        
        private JsonTreeNode CreateJsonTree(JsonElement element, string name = "Root")
        {
            JsonTreeNode node = new JsonTreeNode
            {
                Name = name
            };
            
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    node.DisplayType = "Object";
                    
                    // Process each property in the object
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        node.Children.Add(CreateJsonTree(property.Value, property.Name));
                    }
                    break;
                    
                case JsonValueKind.Array:
                    node.DisplayType = "Array";
                    
                    // Process each item in the array
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        node.Children.Add(CreateJsonTree(item, $"[{index}]"));
                        index++;
                    }
                    break;
                    
                case JsonValueKind.String:
                    string value = element.GetString();
                    node.Value = value;
                    
                    // Check if the string is a URL
                    if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && 
                        (uri.Scheme == "http" || uri.Scheme == "https"))
                    {
                        node.DisplayType = "Url";
                    }
                    else
                    {
                        node.DisplayType = "String";
                    }
                    break;
                    
                case JsonValueKind.Number:
                    // Handle numbers (could be int, float, etc.)
                    if (element.TryGetInt64(out long longValue))
                    {
                        node.Value = longValue.ToString();
                        node.DisplayType = "Number";
                    }
                    else if (element.TryGetDouble(out double doubleValue))
                    {
                        node.Value = doubleValue.ToString();
                        node.DisplayType = "Number";
                    }
                    break;
                    
                case JsonValueKind.True:
                case JsonValueKind.False:
                    node.Value = element.GetBoolean().ToString();
                    node.DisplayType = "Boolean";
                    break;
                    
                case JsonValueKind.Null:
                    node.Value = "null";
                    node.DisplayType = "Null";
                    break;
                    
                default:
                    node.Value = element.ToString();
                    node.DisplayType = "Unknown";
                    break;
            }
            
            return node;
        }
        
        private void ExtractLinkInfoFromJson(JsonElement rootElement)
        {
            // Try to find URL and title in the JSON
            // Common patterns for URL properties
            string[] urlPropertyNames = { "url", "uri", "link", "href" };
            // Common patterns for title properties
            string[] titlePropertyNames = { "title", "name", "description", "text", "label" };
            
            string url = null;
            string title = null;
            
            // Function to search for properties in an object
            Action<JsonElement> searchElement = null;
            searchElement = (element) =>
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        // Check if this property is a URL
                        if (url == null && urlPropertyNames.Any(p => property.Name.ToLowerInvariant().Contains(p)))
                        {
                            if (property.Value.ValueKind == JsonValueKind.String)
                            {
                                string value = property.Value.GetString();
                                if (Uri.TryCreate(value, UriKind.Absolute, out _))
                                {
                                    url = value;
                                }
                            }
                        }
                        
                        // Check if this property is a title
                        if (title == null && titlePropertyNames.Any(p => property.Name.ToLowerInvariant().Contains(p)))
                        {
                            if (property.Value.ValueKind == JsonValueKind.String)
                            {
                                title = property.Value.GetString();
                            }
                        }
                        
                        // Continue searching in nested objects and arrays
                        if (property.Value.ValueKind == JsonValueKind.Object || 
                            property.Value.ValueKind == JsonValueKind.Array)
                        {
                            searchElement(property.Value);
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        searchElement(item);
                    }
                }
            };
            
            // Start searching from the root element
            searchElement(rootElement);
            
            // Update the link preview UI with what we found
            if (!string.IsNullOrEmpty(url))
            {
                LinkPreviewUrl.Text = url;
                LinkPreviewUrlRow.Visibility = Visibility.Visible;
            }
            
            if (!string.IsNullOrEmpty(title))
            {
                LinkPreviewTitle.Text = title;
                LinkPreviewTitleRow.Visibility = Visibility.Visible;
            }
        }

        private bool IsHtmlFormat(ClipboardDataFormat format)
        {
            return format?.FormatName?.Contains("HTML", StringComparison.OrdinalIgnoreCase) == true ||
                   format?.FormatName?.Contains("Text/HTML", StringComparison.OrdinalIgnoreCase) == true;
        }
        
        private bool IsImageFormat(ClipboardDataFormat format)
        {
            // Check by format ID
            if (ImageFormatIds.Any( i => i == format.FormatId ) )
                return true;
                
            // Check by format name
            string formatName = format.FormatName?.ToLowerInvariant() ?? string.Empty;
            return formatName.Contains("bitmap") || 
                   formatName.Contains("image") || 
                   formatName.Contains("png") || 
                   formatName.Contains("jpg") || 
                   formatName.Contains("jpeg") || 
                   formatName.Contains("gif");
        }

        private bool IsLinkFormat(ClipboardDataFormat format)
        {
            // Check by format ID
            if (LinkFormatIds.Any(i => i == format.FormatId))
                return true;
                
            // Check by format name
            string formatName = format.FormatName?.ToLowerInvariant() ?? string.Empty;
            return formatName.Contains("link") || 
                   formatName.Contains("hyperlink") || 
                   formatName.Contains("url");
        }
        
        private bool IsHtmlLinkFormat(ClipboardDataFormat format)
        {
            return format.FormatId == 49723 || // Titled Hyperlink Format
                  (format.FormatName?.ToLowerInvariant()?.Contains("titled") == true && 
                   format.FormatName?.ToLowerInvariant()?.Contains("hyperlink") == true);
        }
        
        private bool IsJsonLinkFormat(ClipboardDataFormat format)
        {
            if (format.FormatId != 49717) // Link Preview Format
                return false;
                
            // Verify it's actually JSON
            try
            {
                string content = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                return IsJsonContent(content);
            }
            catch
            {
                return false;
            }
        }

        private bool IsRichTextFormat(ClipboardDataFormat format)
        {
            // Check by format ID
            if (format.FormatId == 49372 || format.FormatId == 49316)
                return true;
                
            // Check by format name
            string formatName = format.FormatName?.ToLowerInvariant() ?? string.Empty;
            return formatName.Contains("rich") && formatName.Contains("text") || 
                   formatName.Contains("rtf");
        }
        
        private bool IsUnicodeText(ClipboardDataFormat format)
        {
            return format.FormatId == 13 || // Standard CF_UNICODETEXT
                  (format.FormatName?.ToLowerInvariant()?.Contains("unicode") == true);
        }
        
        private bool IsOleData(ClipboardDataFormat format)
        {
            return format.FormatId == 49161 || // DataObject
                   format.FormatId == 49171 || // Ole Private Data
                  (format.FormatName?.ToLowerInvariant()?.Contains("ole") == true);
        }
        
        private bool IsDataObject(ClipboardDataFormat format)
        {
            return format.FormatId == 49161 || // DataObject ID
                  (format.FormatName?.Equals("DataObject", StringComparison.OrdinalIgnoreCase) == true);
        }
        
        private bool IsLocaleFormat(ClipboardDataFormat format)
        {
            return format.FormatId == 16 || // CF_LOCALE
                  (format.FormatName?.Equals("CF_LOCALE", StringComparison.OrdinalIgnoreCase) == true);
        }

        private void DisplayImageContent(ClipboardDataFormat format)
        {
            if (format?.Data == null || format.Data.Length == 0)
                return;
                
            try
            {
                // Create a BitmapImage from the byte array
                BitmapImage bitmapImage = null;
                
                // Handle different image formats
                switch (format.FormatId)
                {
                    case 49379: // PNG
                        bitmapImage = CreateBitmapFromBytes(format.Data);
                        break;
                        
                    case 2: // CF_BITMAP
                    case 8: // CF_DIB
                    case 17: // CF_DIBV5
                        // DIB format needs to be converted
                        bitmapImage = CreateBitmapFromDib(format.Data);
                        break;
                        
                    default:
                        // Try generic approach
                        bitmapImage = CreateBitmapFromBytes(format.Data);
                        break;
                }
                
                if (bitmapImage != null)
                {
                    // Set image to the image control
                    ImagePreview.Source = bitmapImage;
                    
                    // Update image info text
                    bitmapImage.ImageOpened += (s, e) =>
                    {
                        ImageInfoText.Text = $"Image Size: {bitmapImage.PixelWidth} x {bitmapImage.PixelHeight} pixels | Format: {format.FormatName} | Data Size: {format.Size} bytes";
                    };
                }
                else
                {
                    ImageInfoText.Text = $"Unable to render image from {format.FormatName} format";
                }
            }
            catch (Exception ex)
            {
                ImageInfoText.Text = $"Error loading image: {ex.Message}";
            }
        }
        
        private BitmapImage CreateBitmapFromBytes(byte[] imageData)
        {
            // Create a memory stream from the byte data
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                using (DataWriter writer = new DataWriter(stream))
                {
                    writer.WriteBytes(imageData);
                    writer.StoreAsync().GetResults();
                    writer.DetachStream();
                }
                
                // Reset position
                stream.Seek(0);
                
                // Create and load the bitmap
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.SetSource(stream);
                return bitmapImage;
            }
        }
        
        private BitmapImage CreateBitmapFromDib(byte[] dibData)
        {
            try
            {
                // For DIB/DIBV5 format, we need to add a BMP header
                byte[] bmpData = ConvertDibToBmp(dibData);
                return CreateBitmapFromBytes(bmpData);
            }
            catch
            {
                // If conversion fails, try direct loading
                return CreateBitmapFromBytes(dibData);
            }
        }
        
        private byte[] ConvertDibToBmp(byte[] dibData)
        {
            const int BITMAPFILEHEADER_SIZE = 14;
            
            // Create a new array with space for the BMP header
            byte[] bmpData = new byte[dibData.Length + BITMAPFILEHEADER_SIZE];
            
            // BMP header structure
            // BITMAPFILEHEADER
            bmpData[0] = (byte)'B';
            bmpData[1] = (byte)'M';
            
            // File size
            int fileSize = dibData.Length + BITMAPFILEHEADER_SIZE;
            bmpData[2] = (byte)(fileSize & 0xFF);
            bmpData[3] = (byte)((fileSize >> 8) & 0xFF);
            bmpData[4] = (byte)((fileSize >> 16) & 0xFF);
            bmpData[5] = (byte)((fileSize >> 24) & 0xFF);
            
            // Reserved
            bmpData[6] = 0;
            bmpData[7] = 0;
            bmpData[8] = 0;
            bmpData[9] = 0;
            
            // Determine offset to pixel data (header size + DIB header)
            int pixelOffset = BITMAPFILEHEADER_SIZE;
            if (dibData.Length >= 4)
            {
                // Get the DIB header size from the first 4 bytes
                int dibHeaderSize = dibData[0] | (dibData[1] << 8) | (dibData[2] << 16) | (dibData[3] << 24);
                pixelOffset += dibHeaderSize;
            }
            else
            {
                // Default to BITMAPINFOHEADER (40 bytes)
                pixelOffset += 40;
            }
            
            // Offset to pixel data
            bmpData[10] = (byte)(pixelOffset & 0xFF);
            bmpData[11] = (byte)((pixelOffset >> 8) & 0xFF);
            bmpData[12] = (byte)((pixelOffset >> 16) & 0xFF);
            bmpData[13] = (byte)((pixelOffset >> 24) & 0xFF);
            
            // Copy DIB data
            Array.Copy(dibData, 0, bmpData, BITMAPFILEHEADER_SIZE, dibData.Length);
            
            return bmpData;
        }
        
        private string GenerateHexDump(byte[] data, int bytesPerLine)
        {
            if (data == null || data.Length == 0)
                return "No data available";
                
            StringBuilder sb = new StringBuilder();
            
            // Limit to first 4KB to avoid overwhelming the display
            int maxBytes = Math.Min(data.Length, 4096);
            
            for (int i = 0; i < maxBytes; i += bytesPerLine)
            {
                // Address column
                sb.AppendFormat("{0:X8}: ", i);
                
                // Hex columns
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (i + j < maxBytes)
                        sb.AppendFormat("{0:X2} ", data[i + j]);
                    else
                        sb.Append("   ");
                        
                    // Add an extra space in the middle for readability
                    if (j == (bytesPerLine / 2) - 1)
                        sb.Append(" ");
                }
                
                sb.Append(" | ");
                
                // ASCII column
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (i + j < maxBytes)
                    {
                        byte b = data[i + j];
                        char c = (b >= 32 && b <= 126) ? (char)b : '.';
                        sb.Append(c);
                    }
                }
                
                sb.AppendLine();
            }
            
            if (maxBytes < data.Length)
                sb.AppendLine($"... (truncated, {data.Length - maxBytes} more bytes not shown)");
                
            return sb.ToString();
        }

        private async void DisplayHtmlPreview(string htmlContent)
        {
            try
            {
                // Ensure WebView2 is initialized
                if (HtmlPreview.CoreWebView2 == null)
                {
                    await HtmlPreview.EnsureCoreWebView2Async();
                }

                // Create a temporary HTML file for the WebView2 to display
                string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"clipboard_preview_{Guid.NewGuid()}.html");
                File.WriteAllText(tempHtmlPath, htmlContent);

                // Navigate to the temp file
                HtmlPreview.Source = new Uri($"file:///{tempHtmlPath.Replace("\\", "/")}");
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"HTML preview error: {ex.Message}";
            }
        }

        private string ProcessHtmlClipboardContent(string htmlContent)
        {
            // Split the content into lines for easier processing
            string[] lines = htmlContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            StringBuilder processedHtml = new StringBuilder();
            
            // Process metadata lines at the beginning (convert to HTML comments)
            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("<"))
                {
                    // This is a metadata line, comment it
                    processedHtml.AppendLine($"<!-- {trimmedLine} -->");
                }
                else
                {
                    // We've reached HTML content, stop processing metadata
                    break;
                }
            }
            
            // Extract the actual HTML content using regex to handle complex cases
            string fullContent = string.Join(Environment.NewLine, lines);
            
            // First try to extract content between fragment markers
            Match fragmentMatch = Regex.Match(fullContent, 
                @"<!--StartFragment-->(.*?)<!--EndFragment-->", 
                RegexOptions.Singleline);
            
            string contentToFormat;
            
            if (fragmentMatch.Success && fragmentMatch.Groups.Count > 1)
            {
                // Get content between fragment markers
                contentToFormat = fragmentMatch.Groups[1].Value;
                
                // If there's another HTML structure inside (common in clipboard), extract the inner content
                Match innerHtmlMatch = Regex.Match(contentToFormat, 
                    @"<html.*?>.*?<body.*?>(.*?)</body>.*?</html>", 
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                if (innerHtmlMatch.Success && innerHtmlMatch.Groups.Count > 1)
                {
                    // Get the innermost HTML content
                    contentToFormat = innerHtmlMatch.Groups[1].Value;
                }
            }
            else
            {
                // If no fragment markers found, try to extract content from the body tag directly
                Match bodyMatch = Regex.Match(fullContent, 
                    @"<body.*?>(.*?)</body>", 
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                if (bodyMatch.Success && bodyMatch.Groups.Count > 1)
                {
                    contentToFormat = bodyMatch.Groups[1].Value;
                }
                else
                {
                    // Fallback to the original content if no structure is recognized
                    contentToFormat = fullContent;
                }
            }
            
            // Before formatting, clean up character entities and complex styling
            string cleanedContent = CleanHtmlContent(contentToFormat);
            
            // Rewrap the content in a complete HTML document structure with proper formatting
            string completeHtml = $"<html>\n\t<head></head>\n\t<body>\n{BeautifyHtml(cleanedContent, 2)}\n\t</body>\n</html>";
            
            // Add the formatted content to our result
            processedHtml.Append(completeHtml);
            
            return processedHtml.ToString();
        }

        private string CleanHtmlContent(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent))
                return htmlContent;
                
            // Replace HTML character entities with actual characters
            string decodedHtml = HttpUtility.HtmlDecode(htmlContent);
            
            // Handle the case where we have VS Code style HTML with spans for syntax highlighting
            if (decodedHtml.Contains("style=\"color:") || decodedHtml.Contains("style=\"background-color:"))
            {
                // Extract the actual HTML content from the syntax-highlighted version
                // This tries to find the actual HTML content inside the syntax highlighted spans
                try
                {
                    StringBuilder extractedHtml = new StringBuilder();
                    Stack<string> extractionStack = new Stack<string>();
                    
                    // Use regex to find spans with syntax highlighting
                    var matches = Regex.Matches(decodedHtml, @"<span[^>]*>(.*?)</span>", RegexOptions.Singleline);
                    
                    foreach (Match match in matches)
                    {
                        string spanContent = match.Groups[1].Value.Trim();
                        if (spanContent.StartsWith("&lt;") && spanContent.EndsWith("&gt;"))
                        {
                            // This is a tag span, convert it back to a real tag
                            string tagContent = spanContent.Replace("&lt;", "<").Replace("&gt;", ">");
                            extractedHtml.Append(tagContent);
                        }
                        else if (!spanContent.StartsWith("&lt;") && !spanContent.EndsWith("&gt;") && 
                                 !spanContent.Contains("&lt;") && !spanContent.Contains("&gt;"))
                        {
                            // This is content text, keep it
                            extractedHtml.Append(spanContent);
                        }
                    }
                    
                    string extracted = extractedHtml.ToString();
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        return extracted;
                    }
                }
                catch (Exception)
                {
                    // If extraction fails, continue with the normal processing
                }
            }
            
            // Remove excessive whitespace characters
            decodedHtml = Regex.Replace(decodedHtml, @"\s+", " ");
            
            return decodedHtml;
        }

        private string BeautifyHtml(string html, int baseIndent = 0)
        {
            if (string.IsNullOrWhiteSpace(html))
                return html;
            
            StringBuilder formattedHtml = new StringBuilder();
            
            try
            {
                // Define self-closing tags
                var selfClosingTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "area", "base", "br", "col", "embed", "hr", "img", "input", 
                    "link", "meta", "param", "source", "track", "wbr" 
                };
                
                // Create a stack to track nesting
                Stack<string> tagStack = new Stack<string>();
                int indentLevel = baseIndent;
                
                // Create a tokenizer to break HTML into elements and text
                List<HtmlToken> tokens = new List<HtmlToken>();
                
                // First pass: Tokenize the HTML
                int position = 0;
                while (position < html.Length)
                {
                    // Skip whitespace
                    while (position < html.Length && char.IsWhiteSpace(html[position]))
                    {
                        position++;
                    }
                    
                    if (position >= html.Length)
                        break;
                    
                    // Look for tags
                    if (html[position] == '<')
                    {
                        // Find the end of the tag
                        int closePos = html.IndexOf('>', position);
                        if (closePos != -1)
                        {
                            // Extract the tag
                            string tag = html.Substring(position, closePos - position + 1);
                            tokens.Add(new HtmlToken { Content = tag, Type = HtmlTokenType.Tag });
                            position = closePos + 1;
                        }
                        else
                        {
                            // Malformed HTML, add the rest as text
                            string text = html.Substring(position);
                            tokens.Add(new HtmlToken { Content = text, Type = HtmlTokenType.Text });
                            position = html.Length;
                        }
                    }
                    else
                    {
                        // Find the next tag
                        int nextTagPos = html.IndexOf('<', position);
                        if (nextTagPos == -1)
                        {
                            // No more tags, add the rest as text
                            string text = html.Substring(position);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                tokens.Add(new HtmlToken { Content = text, Type = HtmlTokenType.Text });
                            }
                            position = nextTagPos;
                        }
                        else
                        {
                            // Add text up to the next tag
                            string text = html.Substring(position, nextTagPos - position);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                tokens.Add(new HtmlToken { Content = text, Type = HtmlTokenType.Text });
                            }
                            position = nextTagPos;
                        }
                    }
                }
                
                // Second pass: Format with indentation
                for (int i = 0; i < tokens.Count; i++)
                {
                    HtmlToken token = tokens[i];
                    
                    if (token.Type == HtmlTokenType.Tag)
                    {
                        string tagContent = token.Content.Trim();
                        
                        if (tagContent.StartsWith("</"))
                        {
                            // Closing tag - decrease indent after tag
                            string tagName = Regex.Match(tagContent, @"</([^\s>]+)").Groups[1].Value;
                            
                            if (tagStack.Count > 0)
                            {
                                tagStack.Pop();
                                indentLevel = baseIndent + tagStack.Count;
                            }
                            
                            formattedHtml.AppendLine();
                            formattedHtml.Append(new string('\t', indentLevel));
                            formattedHtml.Append(tagContent);
                        }
                        else if (tagContent.EndsWith("/>") || 
                                 selfClosingTags.Contains(Regex.Match(tagContent, @"<([^\s/>]+)").Groups[1].Value))
                        {
                            // Self-closing tag - no indent change
                            formattedHtml.AppendLine();
                            formattedHtml.Append(new string('\t', indentLevel));
                            formattedHtml.Append(tagContent);
                        }
                        else
                        {
                            // Opening tag - increase indent for content
                            string tagName = Regex.Match(tagContent, @"<([^\s>]+)").Groups[1].Value;
                            
                            formattedHtml.AppendLine();
                            formattedHtml.Append(new string('\t', indentLevel));
                            formattedHtml.Append(tagContent);
                            
                            tagStack.Push(tagName);
                            indentLevel = baseIndent + tagStack.Count;
                        }
                    }
                    else if (token.Type == HtmlTokenType.Text)
                    {
                        // Text content - format with indentation and line breaks
                        string text = token.Content.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            formattedHtml.AppendLine();
                            formattedHtml.Append(new string('\t', indentLevel));
                            formattedHtml.Append(text);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If anything goes wrong, return the original content
                return html;
            }
            
            return formattedHtml.ToString();
        }

        private void DisplayLinkContent(ClipboardDataFormat format)
        {
            if (format?.Data == null || format.Data.Length == 0)
                return;
                
            try
            {
                // For text-based link formats, try to parse as UTF-8 or UTF-16 string
                string linkContent = null;
                
                // Try UTF-8 first
                try
                {
                    linkContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                }
                catch
                {
                    // If UTF-8 fails, try UTF-16
                    try
                    {
                        linkContent = Encoding.Unicode.GetString(format.Data).TrimEnd('\0');
                    }
                    catch
                    {
                        // If both encodings fail, fall back to showing hex data
                        linkContent = GenerateHexDump(format.Data, 16);
                    }
                }
                
                // Try to extract URL and Title from the content
                Uri uri = null;
                string title = null;
                string sourceFormatName = format.FormatName;
                
                // Look for URL patterns first - try to find a URL in any format
                var urlMatches = Regex.Matches(linkContent, @"(https?://[^\s""'<>()]+)");
                if (urlMatches.Count > 0)
                {
                    // Use the first URL found
                    string url = urlMatches[0].Value;
                    if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                    {
                        // Successfully extracted a URL
                    }
                }
                
                // Try to extract title - varies by format, look for common patterns
                var titleMatches = Regex.Matches(linkContent, @"""([^""]+)""");
                if (titleMatches.Count > 0)
                {
                    // Use first quoted string as possible title
                    title = titleMatches[0].Groups[1].Value;
                }
                
                // If title wasn't found in quotes, look for other patterns
                if (string.IsNullOrEmpty(title))
                {
                    // Look for title between specific delimiters or patterns
                    titleMatches = Regex.Matches(linkContent, @"Title:\s*([^\r\n]+)");
                    if (titleMatches.Count > 0)
                    {
                        title = titleMatches[0].Groups[1].Value;
                    }
                }
                
                // Check for binary format with null-terminated strings (common in Windows clipboard formats)
                if (uri == null)
                {
                    // For binary formats, try to find strings separated by nulls
                    List<string> extractedStrings = ExtractNullTerminatedStrings(format.Data);
                    if (extractedStrings.Count > 0)
                    {
                        foreach (var str in extractedStrings)
                        {
                            // Check if this string is a URL
                            if (Uri.TryCreate(str, UriKind.Absolute, out uri))
                            {
                                break; // Found a URL
                            }
                        }
                        
                        // If we found a URL and there's at least one more string, use the next one as title
                        int urlIndex = -1;
                        for (int i = 0; i < extractedStrings.Count; i++)
                        {
                            if (Uri.TryCreate(extractedStrings[i], UriKind.Absolute, out _))
                            {
                                urlIndex = i;
                                break;
                            }
                        }
                        
                        if (urlIndex >= 0 && urlIndex + 1 < extractedStrings.Count)
                        {
                            title = extractedStrings[urlIndex + 1];
                        }
                    }
                }
                
                // Update the link preview
                UpdateLinkPreview(uri, title, sourceFormatName, linkContent);
            }
            catch (Exception ex)
            {
                // Show error in the link preview
                LinkPreviewUrl.Text = "Error parsing link";
                LinkPreviewTitle.Text = ex.Message;
                LinkPreviewTitleRow.Visibility = Visibility.Visible;
            }
        }
        
        private void UpdateLinkPreview(Uri uri, string title, string formatName, string rawContent)
        {
            // Configure visibility and content based on what data we have
            LinkPreviewFormatText.Text = formatName;
            
            // URL row
            if (uri != null)
            {
                LinkPreviewUrl.Text = uri.ToString();
                LinkPreviewUrlRow.Visibility = Visibility.Visible;
            }
            else
            {
                LinkPreviewUrlRow.Visibility = Visibility.Collapsed;
            }
            
            // Title row
            if (!string.IsNullOrEmpty(title))
            {
                LinkPreviewTitle.Text = title;
                LinkPreviewTitleRow.Visibility = Visibility.Visible;
            }
            else
            {
                LinkPreviewTitleRow.Visibility = Visibility.Collapsed;
            }
            
            // Enable or disable the Open button based on whether we have a valid URL
            OpenLinkButton.IsEnabled = uri != null;
        }
        
        private List<string> ExtractNullTerminatedStrings(byte[] data)
        {
            List<string> strings = new List<string>();
            
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                // Check for null terminator in various encodings
                if (data[i] == 0 && (i + 1 >= data.Length || data[i + 1] == 0))
                {
                    if (i > start)
                    {
                        // Extract string between start and current position
                        try
                        {
                            // Try ASCII first
                            string str = Encoding.ASCII.GetString(data, start, i - start).Trim();
                            if (!string.IsNullOrWhiteSpace(str))
                            {
                                strings.Add(str);
                            }
                        }
                        catch
                        {
                            // If ASCII fails, try Unicode
                            try
                            {
                                if ((i - start) % 2 == 0) // Valid Unicode length
                                {
                                    string str = Encoding.Unicode.GetString(data, start, i - start).Trim();
                                    if (!string.IsNullOrWhiteSpace(str))
                                    {
                                        strings.Add(str);
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore encoding errors
                            }
                        }
                    }
                    
                    // Skip over null bytes (handle Unicode double-null termination)
                    while (i < data.Length && data[i] == 0)
                    {
                        i++;
                    }
                    
                    start = i;
                    if (i < data.Length)
                    {
                        i--; // Counter will increment in the loop
                    }
                }
            }
            
            return strings;
        }
        
        private async void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Uri.TryCreate(LinkPreviewUrl.Text, UriKind.Absolute, out Uri uri))
                {
                    // Use Windows.System.Launcher to open the URL in the default browser
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error opening link: {ex.Message}";
            }
        }

        private string GetLinkRawContent(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "No data available";
                
            // Try various encodings to get the most readable content
            try
            {
                // Try UTF-8 first (most common for web content)
                string content = Encoding.UTF8.GetString(data).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(content) && !HasBinaryContent(content))
                {
                    return content;
                }
            }
            catch { /* Try next encoding */ }
            
            try
            {
                // Try UTF-16 (common for Windows formats)
                string content = Encoding.Unicode.GetString(data).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(content) && !HasBinaryContent(content))
                {
                    return content;
                }
            }
            catch { /* Try next encoding */ }
            
            try
            {
                // Try ASCII as fallback
                string content = Encoding.ASCII.GetString(data).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(content) && !HasBinaryContent(content))
                {
                    return content;
                }
            }
            catch { /* Try next approach */ }
            
            // Try to extract strings from binary data
            List<string> strings = ExtractNullTerminatedStrings(data);
            if (strings.Count > 0)
            {
                return string.Join(Environment.NewLine, strings);
            }
            
            // Finally, if all else fails, generate a hex dump
            return GenerateHexDump(data, 16);
        }
        
        private bool HasBinaryContent(string text)
        {
            // Check if text contains too many non-printable characters
            int nonPrintableCount = 0;
            int sampleSize = Math.Min(text.Length, 1000); // Check first 1000 chars
            
            for (int i = 0; i < sampleSize; i++)
            {
                char c = text[i];
                if (c < 32 && c != '\r' && c != '\n' && c != '\t')
                {
                    nonPrintableCount++;
                }
            }
            
            // If more than 5% are non-printable, consider it binary
            return (nonPrintableCount * 100.0 / sampleSize) > 5;
        }
        
        private bool IsJsonContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;
                
            // Trim whitespace
            string trimmed = content.Trim();
            
            // Check for basic JSON patterns
            bool startsWithJsonStructure = trimmed.StartsWith("{") && trimmed.EndsWith("}") || // Object
                                           trimmed.StartsWith("[") && trimmed.EndsWith("]");   // Array
                                           
            if (!startsWithJsonStructure)
                return false;
                
            // Check for common JSON patterns
            bool containsKeyValuePairs = trimmed.Contains("\":") || trimmed.Contains("\": ");
            
            return containsKeyValuePairs;
        }
        
        private string FormatJsonString(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return jsonString;
                
            try
            {
                // Create a StringReader from the input string
                using (StringReader stringReader = new StringReader(jsonString))
                {
                    // Create a StringBuilder to hold the formatted output
                    StringBuilder sb = new StringBuilder();
                    
                    // Initialize formatting state
                    int indentLevel = 0;
                    bool inQuotes = false;
                    bool escaped = false;
                    int lastNewlinePos = 0;
                    
                    // Process each character
                    int nextChar;
                    while ((nextChar = stringReader.Read()) != -1)
                    {
                        char c = (char)nextChar;
                        
                        // Handle string content
                        if (c == '\"' && !escaped)
                        {
                            inQuotes = !inQuotes;
                            sb.Append(c);
                            continue;
                        }
                        
                        // Handle escape sequences
                        if (c == '\\' && inQuotes)
                        {
                            escaped = !escaped;
                            sb.Append(c);
                            continue;
                        }
                        
                        if (escaped)
                        {
                            escaped = false;
                            sb.Append(c);
                            continue;
                        }
                        
                        // Within quotes, just append characters
                        if (inQuotes)
                        {
                            sb.Append(c);
                            continue;
                        }
                        
                        // Handle whitespace outside quotes
                        if (char.IsWhiteSpace(c))
                        {
                            // Skip original whitespace
                            continue;
                        }
                        
                        // Handle structural characters
                        switch (c)
                        {
                            case '{':
                            case '[':
                                sb.Append(c);
                                sb.AppendLine();
                                indentLevel++;
                                sb.Append(new string('\t', indentLevel));
                                lastNewlinePos = sb.Length;
                                break;
                                
                            case '}':
                            case ']':
                                sb.AppendLine();
                                indentLevel--;
                                sb.Append(new string('\t', indentLevel));
                                sb.Append(c);
                                lastNewlinePos = sb.Length;
                                break;
                                
                            case ',':
                                sb.Append(c);
                                sb.AppendLine();
                                sb.Append(new string('\t', indentLevel));
                                lastNewlinePos = sb.Length;
                                break;
                                
                            case ':':
                                sb.Append(c);
                                sb.Append(' '); // Add space after colon for readability
                                break;
                                
                            default:
                                sb.Append(c);
                                break;
                        }
                    }
                    
                    return sb.ToString();
                }
            }
            catch (Exception)
            {
                // If formatting fails, return the original JSON string
                return jsonString;
            }
        }

        private string ExtractTextFromRTF(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "No RTF data available";
                
            try
            {
                // Convert the binary data to a string with RTF markup
                string rtfContent = Encoding.ASCII.GetString(data).TrimEnd('\0');
                
                // Return the raw RTF content
                return rtfContent;
            }
            catch (Exception ex)
            {
                return $"Error extracting RTF: {ex.Message}";
            }
        }
        
        private string DecodeDataObject(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "No DataObject data available";
                
            try
            {
                // For DataObject format, try to extract useful information
                // This could be structured data or binary metadata
                StringBuilder result = new StringBuilder();
                result.AppendLine("DataObject Format Content:");
                result.AppendLine();
                
                // Try to extract strings first
                List<string> strings = ExtractNullTerminatedStrings(data);
                if (strings.Count > 0)
                {
                    result.AppendLine("Extracted strings:");
                    foreach (var str in strings)
                    {
                        result.AppendLine($"- {str}");
                    }
                    result.AppendLine();
                }
                
                // Also try standard encodings
                try
                {
                    string asciiContent = Encoding.ASCII.GetString(data).TrimEnd('\0');
                    if (!HasBinaryContent(asciiContent))
                    {
                        result.AppendLine("ASCII Content:");
                        result.AppendLine(asciiContent);
                    }
                }
                catch { /* Ignore encoding error */ }
                
                // If we have a small amount of data, show as both hex dump and integer values
                if (data.Length <= 32)
                {
                    result.AppendLine("Raw data as integers:");
                    
                    for (int i = 0; i < data.Length; i += 4)
                    {
                        if (i + 3 < data.Length)
                        {
                            int value = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24);
                            result.AppendLine($"Offset {i,2}: {value,10} (0x{value:X8})");
                        }
                    }
                }
                
                // Add hex dump for all data
                result.AppendLine();
                result.AppendLine("Hex Dump:");
                result.AppendLine(GenerateHexDump(data, 16));
                
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Error decoding DataObject: {ex.Message}";
            }
        }
        
        private string DecodeOleData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "No OLE data available";
                
            try
            {
                // For OLE format, try to decode its structure if possible
                StringBuilder result = new StringBuilder();
                result.AppendLine("OLE Private Data Content:");
                result.AppendLine();
                
                // Try to extract strings from the OLE data
                List<string> strings = ExtractNullTerminatedStrings(data);
                if (strings.Count > 0)
                {
                    result.AppendLine("Extracted strings:");
                    foreach (var str in strings)
                    {
                        result.AppendLine($"- {str}");
                    }
                    result.AppendLine();
                }
                
                // Look for structured data patterns like GUIDs
                // OLE data often contains GUIDs/CLSIDs
                for (int i = 0; i < data.Length - 16; i++)
                {
                    // Check for potential GUID structure
                    if ((i == 0 || data[i-1] == 0) && // Start of data or preceded by null
                        data[i+4] == 0x00 && data[i+6] == 0x00 && // GUID structural checks
                        data[i+9] == 0x00)
                    {
                        try
                        {
                            byte[] guidBytes = new byte[16];
                            Array.Copy(data, i, guidBytes, 0, 16);
                            // Rearrange bytes to match GUID structure if needed
                            Guid guid = new Guid(guidBytes);
                            result.AppendLine($"Possible GUID at offset {i}: {guid}");
                        }
                        catch { /* Not a valid GUID */ }
                    }
                }
                
                // Add hex dump for better analysis
                result.AppendLine();
                result.AppendLine("Hex Dump:");
                result.AppendLine(GenerateHexDump(data, 16));
                
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Error decoding OLE data: {ex.Message}";
            }
        }
        
        private string DecodeLocaleData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "No locale data available";
                
            try
            {
                // CF_LOCALE typically contains a LCID (Locale Identifier)
                // which is a 32-bit value
                if (data.Length >= 4)
                {
                    int lcid = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
                    
                    StringBuilder result = new StringBuilder();
                    result.AppendLine($"Locale Identifier (LCID): {lcid} (0x{lcid:X8})");
                    
                    // Try to get locale name from LCID
                    try
                    {
                        // Get some common locale names
                        string localeName = lcid switch
                        {
                            1033 => "en-US (English - United States)",
                            1041 => "ja-JP (Japanese - Japan)",
                            1031 => "de-DE (German - Germany)",
                            1036 => "fr-FR (French - France)",
                            1042 => "ko-KR (Korean - Korea)",
                            2052 => "zh-CN (Chinese - China)",
                            1028 => "zh-TW (Chinese - Taiwan)",
                            1034 => "es-ES (Spanish - Spain)",
                            1040 => "it-IT (Italian - Italy)",
                            1046 => "pt-BR (Portuguese - Brazil)",
                            1049 => "ru-RU (Russian - Russia)",
                            1035 => "fi-FI (Finnish - Finland)",
                            1030 => "da-DK (Danish - Denmark)",
                            1044 => "no-NO (Norwegian - Norway)",
                            1053 => "sv-SE (Swedish - Sweden)",
                            1043 => "nl-NL (Dutch - Netherlands)",
                            2057 => "en-GB (English - United Kingdom)",
                            3081 => "en-AU (English - Australia)",
                            4105 => "en-CA (English - Canada)",
                            1025 => "ar-SA (Arabic - Saudi Arabia)",
                            1037 => "he-IL (Hebrew - Israel)",
                            1055 => "tr-TR (Turkish - Turkey)",
                            1060 => "sl-SI (Slovenian - Slovenia)",
                            1029 => "cs-CZ (Czech - Czech Republic)",
                            1038 => "hu-HU (Hungarian - Hungary)",
                            1045 => "pl-PL (Polish - Poland)",
                            _ => $"Unknown locale ({lcid})"
                        };
                        
                        result.AppendLine($"Locale Name: {localeName}");
                    }
                    catch (Exception)
                    {
                        result.AppendLine("Unable to identify locale name");
                    }
                    
                    // Display raw data
                    result.AppendLine();
                    result.AppendLine("Raw Data:");
                    result.AppendLine(GenerateHexDump(data, 16));
                    
                    return result.ToString();
                }
                else
                {
                    return "Invalid locale data format (expected at least 4 bytes for LCID)";
                }
            }
            catch (Exception ex)
            {
                return $"Error decoding locale data: {ex.Message}";
            }
        }

        private void DisplayRichTextContent(ClipboardDataFormat format)
        {
            if (format?.Data == null || format.Data.Length == 0)
                return;
                
            try
            {
                // Convert RTF data to string
                string rtfContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');

                  RtfPreview.IsReadOnly = false;
                  using( var stream = new MemoryStream( format.Data ).AsRandomAccessStream() )
                  {
                     RtfPreview.Document.LoadFromStream( Microsoft.UI.Text.TextSetOptions.FormatRtf, stream  );
                  }

                  RtfPreview.IsReadOnly = true;
            }
            catch (Exception ex)
            {
                // Simple error handling with no threading
                RtfInfoText.Text = $"Error loading RTF: {ex.Message}";
                RtfPreview.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, "Error displaying RTF content");
            }
        }

        private string ExtractPlainTextFromRTF(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent))
                return string.Empty;
                
            StringBuilder plainText = new StringBuilder();
            bool inControl = false;
            bool inPlainText = true;
            
            for (int i = 0; i < rtfContent.Length; i++)
            {
                char c = rtfContent[i];
                
                // Handle control sequences
                if (c == '\\')
                {
                    inControl = true;
                    
                    // Look ahead for control word
                    int j = i + 1;
                    if (j < rtfContent.Length)
                    {
                        char next = rtfContent[j];
                        
                        // Check for special control sequences
                        if (next == 'p' && rtfContent.Substring(j, Math.Min(4, rtfContent.Length - j)).StartsWith("par"))
                        {
                            // Paragraph marker
                            plainText.AppendLine();
                            inControl = true;
                        }
                        else if (next == '\'')
                        {
                            // Hex character escape - attempt to convert
                            if (j + 2 < rtfContent.Length)
                            {
                                try
                                {
                                    string hex = rtfContent.Substring(j + 1, 2);
                                    int charCode = Convert.ToInt32(hex, 16);
                                    plainText.Append((char)charCode);
                                }
                                catch { }
                                
                                // Skip the hex digits
                                i += 3;
                                inControl = false;
                                continue;
                            }
                        }
                    }
                }
                else if (c == '{' || c == '}')
                {
                    // Group markers - ignore
                    inControl = false;
                    continue;
                }
                else if (inControl)
                {
                    // Inside control sequence
                    if (!char.IsLetter(c) && c != '-' && c != '_' && c != '*')
                    {
                        // End of control word
                        inControl = false;
                    }
                    continue;
                }
                else if (inPlainText && !inControl)
                {
                    // Regular text character
                    plainText.Append(c);
                }
            }
            
            return plainText.ToString();
        }

        private void SafeSetEditorText(WinUIEditor.CodeEditorControl editor, string text, string highlightingLanguage = "plaintext")
        {
            try
            {
                // Set highlighting language first
                editor.HighlightingLanguage = highlightingLanguage;
                
                // Then attempt to set the text
                editor.Editor.SetText(text);
            }
            catch (Exception ex)
            {
                // If setting text directly fails, try with Dispatcher
                try
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            editor.Editor.SetText(text);
                        }
                        catch (Exception innerEx)
                        {
                            // If still failing, show error in status bar
                            StatusTextBlock.Text = $"Editor error: {innerEx.GetType().Name}: {innerEx.Message}";
                            
                            // As last resort, try creating a TextBlock with the content
                            Grid grid = new Grid();
                            ScrollViewer scrollViewer = new ScrollViewer
                            {
                                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                Padding = new Thickness(12)
                            };
                            
                            TextBlock fallbackTextBlock = new TextBlock
                            {
                                Text = text,
                                TextWrapping = TextWrapping.NoWrap,
                                FontFamily = new FontFamily("Consolas"),
                                FontSize = 13
                            };
                            
                            scrollViewer.Content = fallbackTextBlock;
                            grid.Children.Add(scrollViewer);
                            
                            // Replace editor with scrollviewer in the visual tree
                            var parent = editor.Parent as Panel;
                            if (parent != null)
                            {
                                int index = parent.Children.IndexOf(editor);
                                if (index >= 0)
                                {
                                    parent.Children.RemoveAt(index);
                                    parent.Children.Insert(index, grid);
                                }
                            }
                        }
                    });
                }
                catch
                {
                    StatusTextBlock.Text = $"Critical editor error: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        // Helper classes for HTML tokenization
        
        
        private void JsonTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            // When a JSON node is clicked, we can handle it here
            // For example, we might want to expand all children
            if (args.InvokedItem is JsonTreeNode node)
            {
                if (node.DisplayType == "Url" && node.Value != null)
                {
                    // If it's a URL node, we could optionally launch the URL
                    // But we'll leave that to the hyperlink control for now
                }
            }
        }
    }
   public enum HtmlTokenType { Tag, Text }
   public class HtmlToken
   {
      public string Content { get; set; }
      public HtmlTokenType Type { get; set; }
   }

   // XAML class to represent JSON data for tree view
   public class JsonTreeNode
   {
      public string Name { get; set; }
      public string Value { get; set; }
      public string DisplayType { get; set; }
      public List<JsonTreeNode> Children { get; set; } = new List<JsonTreeNode>();
      public bool IsLeaf => Children == null || Children.Count == 0;
      public bool IsArray => DisplayType == "Array";
      public bool IsObject => DisplayType == "Object";
      public bool IsProperty => DisplayType == "Property";
      public bool IsUrl => DisplayType == "Url";
   }

   // Template selector for JSON tree nodes
   public class JsonNodeTemplateSelector : DataTemplateSelector
   {
      public DataTemplate ObjectTemplate { get; set; }
      public DataTemplate ArrayTemplate { get; set; }
      public DataTemplate StringTemplate { get; set; }
      public DataTemplate NumberTemplate { get; set; }
      public DataTemplate BooleanTemplate { get; set; }
      public DataTemplate NullTemplate { get; set; }
      public DataTemplate UrlTemplate { get; set; }

      protected override DataTemplate SelectTemplateCore( object item )
      {
         if( item is JsonTreeNode node )
         {
            switch( node.DisplayType )
            {
               case "Object": return ObjectTemplate;
               case "Array": return ArrayTemplate;
               case "String": return StringTemplate;
               case "Number": return NumberTemplate;
               case "Boolean": return BooleanTemplate;
               case "Null": return NullTemplate;
               case "Url": return UrlTemplate;
               default: return StringTemplate; // Fallback
            }
         }

         return base.SelectTemplateCore( item );
      }
   }

}