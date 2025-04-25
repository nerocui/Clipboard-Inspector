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
            Image
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
                    
                    // Process the data for the Source view
                    ProcessForSourceView(selectedFormat, isHtmlFormat, isImageFormat);
                    
                    // Prepare preview based on format type
                    PreparePreview(selectedFormat, isHtmlFormat, isImageFormat);
                    
                    // Switch to source view by default for consistency
                    ContentPivot.SelectedIndex = 0;
                }
                catch (Exception ex)
                {
                    ContentEditor.Editor.SetText($"Error generating preview: {ex.Message}");
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
        
        private void ProcessForSourceView(ClipboardDataFormat format, bool isHtmlFormat, bool isImageFormat)
        {
            if (isImageFormat)
            {
                // For image formats, show a hex dump in the source view
                string hexPreview = GenerateHexDump(format.Data, 16);
                ContentEditor.HighlightingLanguage = "plaintext";
                ContentEditor.Editor.SetText(hexPreview);
            }
            else if (isHtmlFormat)
            {
                // For HTML formats, syntax highlight the raw HTML
                string htmlContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                ContentEditor.HighlightingLanguage = "html";
                ContentEditor.Editor.SetText(htmlContent);
            }
            else
            {
                // For other formats, use the default preview
                string preview = _clipboardService.GetPreviewForFormat(format);
                ContentEditor.HighlightingLanguage = "plaintext";
                ContentEditor.Editor.SetText(preview);
            }
        }
        
        private void PreparePreview(ClipboardDataFormat format, bool isHtmlFormat, bool isImageFormat)
        {
            if (isImageFormat)
            {
                // Image format - prepare image preview
                DisplayImageContent(format);
                CurrentFormatType = FormatType.Image;
            }
            else if (isHtmlFormat)
            {
                // HTML format - prepare HTML preview
                string htmlContent = Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
                string processedHtml = ProcessHtmlClipboardContent(htmlContent);
                DisplayHtmlPreview(processedHtml);
                CurrentFormatType = FormatType.HTML;
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
                            position = html.Length;
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

        // Helper classes for HTML tokenization
        private enum HtmlTokenType { Tag, Text }
        
        private class HtmlToken
        {
            public string Content { get; set; }
            public HtmlTokenType Type { get; set; }
        }
        
        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}