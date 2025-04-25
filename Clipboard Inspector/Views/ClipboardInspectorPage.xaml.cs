using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Clipboard_Inspector.Models;
using Clipboard_Inspector.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.IO;

namespace Clipboard_Inspector.Views
{
    public sealed partial class ClipboardInspectorPage : Page
    {
        private ClipboardService _clipboardService;
        private List<ClipboardDataFormat> _formats;

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
                    PreviewPivot.Visibility = Visibility.Collapsed;
                    ContentPivot.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
                ContentEditor.Editor.SetText($"Failed to retrieve clipboard data: {ex.Message}");
                PreviewPivot.Visibility = Visibility.Collapsed;
                ContentPivot.SelectedIndex = 0;
            }
        }

        private void FormatsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatsDataGrid.SelectedItem is ClipboardDataFormat selectedFormat)
            {
                try
                {
                    // Check if the format is HTML
                    bool isHtmlFormat = IsHtmlFormat(selectedFormat);
                    
                    // Get the content preview
                    string preview;
                    
                    if (isHtmlFormat)
                    {
                        // Process and display HTML content
                        preview = ProcessHtmlClipboardContent(Encoding.UTF8.GetString(selectedFormat.Data).TrimEnd('\0'));
                        ContentEditor.HighlightingLanguage = "html";
                        
                        // Show the Preview pivot for HTML content
                        PreviewPivot.Visibility = Visibility.Visible;
                        
                        // Load HTML in WebView2
                        DisplayHtmlPreview(preview);
                    }
                    else
                    {
                        // Regular format display
                        preview = _clipboardService.GetPreviewForFormat(selectedFormat);
                        ContentEditor.HighlightingLanguage = "plaintext";
                        
                        // Hide the Preview pivot for non-HTML content
                        PreviewPivot.Visibility = Visibility.Collapsed;
                    }
                    
                    // Set content in the editor
                    ContentEditor.Editor.SetText(preview);
                    
                    // Ensure the Source pivot is selected
                    ContentPivot.SelectedIndex = 0;
                }
                catch (Exception ex)
                {
                    ContentEditor.Editor.SetText($"Error generating preview: {ex.Message}");
                    ContentEditor.HighlightingLanguage = "plaintext";
                    PreviewPivot.Visibility = Visibility.Collapsed;
                    ContentPivot.SelectedIndex = 0;
                }
            }
            else
            {
                ContentEditor.Editor.SetText(string.Empty);
                PreviewPivot.Visibility = Visibility.Collapsed;
                ContentPivot.SelectedIndex = 0;
            }
        }

        private bool IsHtmlFormat(ClipboardDataFormat format)
        {
            return format?.FormatName?.Contains("HTML", StringComparison.OrdinalIgnoreCase) == true ||
                   format?.FormatName?.Contains("Text/HTML", StringComparison.OrdinalIgnoreCase) == true;
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
    }
}