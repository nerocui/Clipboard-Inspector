using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Text.Json;

namespace Clipboard_Inspector.Controls
{
    public sealed partial class JsonTreeViewer : UserControl
    {
        public JsonTreeViewer()
        {
            this.InitializeComponent();
        }

        public static readonly DependencyProperty JsonContentProperty = 
            DependencyProperty.Register(
                "JsonContent", 
                typeof(string), 
                typeof(JsonTreeViewer), 
                new PropertyMetadata(null, OnJsonContentChanged)
            );

        public string JsonContent
        {
            get { return (string)GetValue(JsonContentProperty); }
            set { SetValue(JsonContentProperty, value); }
        }

        private static void OnJsonContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is JsonTreeViewer control && e.NewValue is string jsonString)
            {
                control.ParseJsonContent(jsonString);
            }
        }

        private void ParseJsonContent(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return;

            // Clear existing items
            JsonTreeView.RootNodes.Clear();

            try
            {
                // Parse the JSON content
                using (JsonDocument doc = JsonDocument.Parse(jsonString))
                {
                    // Get the root element
                    JsonElement root = doc.RootElement;
                    
                    // Process the root element based on its kind
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        var rootItem = new TreeViewNode { Content = CreateObjectNodeContent("Root") };
                        PopulateObjectNode(rootItem, root);
                        JsonTreeView.RootNodes.Add(rootItem);
                    }
                    else if (root.ValueKind == JsonValueKind.Array)
                    {
                        var rootItem = new TreeViewNode { Content = CreateArrayNodeContent("Root Array") };
                        PopulateArrayNode(rootItem, root);
                        JsonTreeView.RootNodes.Add(rootItem);
                    }
                    else
                    {
                        // Handle simple value as root (less common but possible)
                        var content = CreateSimpleValueNodeContent("Root", root);
                        JsonTreeView.RootNodes.Add(new TreeViewNode { Content = content });
                    }
                }
            }
            catch (Exception ex)
            {
                // Add an error node
                var errorNode = new TreeViewNode
                {
                    Content = CreateErrorNodeContent("Error parsing JSON", ex.Message)
                };
                JsonTreeView.RootNodes.Add(errorNode);
            }
        }

        private void PopulateObjectNode(TreeViewNode parentNode, JsonElement element)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                var propertyNode = CreateNodeForJsonElement(property.Name, property.Value);
                parentNode.Children.Add(propertyNode);
            }
        }

        private void PopulateArrayNode(TreeViewNode parentNode, JsonElement element)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                var arrayItemNode = CreateNodeForJsonElement($"[{index}]", item);
                parentNode.Children.Add(arrayItemNode);
                index++;
            }
        }

        private TreeViewNode CreateNodeForJsonElement(string name, JsonElement element)
        {
            TreeViewNode node = new TreeViewNode();
            
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    node.Content = CreateObjectNodeContent(name);
                    PopulateObjectNode(node, element);
                    break;
                    
                case JsonValueKind.Array:
                    node.Content = CreateArrayNodeContent(name);
                    PopulateArrayNode(node, element);
                    break;
                    
                case JsonValueKind.String:
                    string strValue = element.GetString();
                    // Check if the string is a URL
                    if (Uri.TryCreate(strValue, UriKind.Absolute, out Uri uri) && 
                        (uri.Scheme == "http" || uri.Scheme == "https"))
                    {
                        node.Content = CreateUrlNodeContent(name, strValue, uri.ToString());
                    }
                    else
                    {
                        node.Content = CreateStringNodeContent(name, strValue);
                    }
                    break;
                    
                case JsonValueKind.Number:
                    string numValue;
                    if (element.TryGetInt64(out long longValue))
                    {
                        numValue = longValue.ToString();
                    }
                    else
                    {
                        numValue = element.GetDouble().ToString();
                    }
                    node.Content = CreateNumberNodeContent(name, numValue);
                    break;
                    
                case JsonValueKind.True:
                case JsonValueKind.False:
                    node.Content = CreateBooleanNodeContent(name, element.GetBoolean().ToString().ToLower());
                    break;
                    
                case JsonValueKind.Null:
                    node.Content = CreateNullNodeContent(name);
                    break;
                    
                default:
                    node.Content = CreateStringNodeContent(name, "Unknown type");
                    break;
            }
            
            return node;
        }

        // Content creation methods for different node types
        private UIElement CreateObjectNodeContent(string name)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var icon = new FontIcon { 
                Glyph = "\uE81E", 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215)) 
            };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var typeText = new TextBlock { 
                Text = "{Object}", 
                Opacity = 0.6 
            };
            
            panel.Children.Add(icon);
            panel.Children.Add(nameText);
            panel.Children.Add(typeText);
            
            return panel;
        }

        private UIElement CreateArrayNodeContent(string name)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var icon = new FontIcon { 
                Glyph = "\uE71D", 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 127, 181, 181)) 
            };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var typeText = new TextBlock { 
                Text = "{Array}", 
                Opacity = 0.6 
            };
            
            panel.Children.Add(icon);
            panel.Children.Add(nameText);
            panel.Children.Add(typeText);
            
            return panel;
        }

        private UIElement CreateStringNodeContent(string name, string value)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var separator = new TextBlock { 
                Text = ":", 
                Opacity = 0.6 
            };
            
            var valueText = new TextBlock { 
                Text = value, 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 83, 135, 87)) 
            };
            
            panel.Children.Add(nameText);
            panel.Children.Add(separator);
            panel.Children.Add(valueText);
            
            return panel;
        }

        private UIElement CreateNumberNodeContent(string name, string value)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var separator = new TextBlock { 
                Text = ":", 
                Opacity = 0.6 
            };
            
            var valueText = new TextBlock { 
                Text = value, 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 144, 106, 106)) 
            };
            
            panel.Children.Add(nameText);
            panel.Children.Add(separator);
            panel.Children.Add(valueText);
            
            return panel;
        }

        private UIElement CreateBooleanNodeContent(string name, string value)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var separator = new TextBlock { 
                Text = ":", 
                Opacity = 0.6 
            };
            
            var valueText = new TextBlock { 
                Text = value, 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 93, 102, 106)) 
            };
            
            panel.Children.Add(nameText);
            panel.Children.Add(separator);
            panel.Children.Add(valueText);
            
            return panel;
        }

        private UIElement CreateNullNodeContent(string name)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var separator = new TextBlock { 
                Text = ":", 
                Opacity = 0.6 
            };
            
            var valueText = new TextBlock { 
                Text = "null", 
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 122, 128)) 
            };
            
            panel.Children.Add(nameText);
            panel.Children.Add(separator);
            panel.Children.Add(valueText);
            
            return panel;
        }

        private UIElement CreateUrlNodeContent(string name, string displayText, string url)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var separator = new TextBlock { 
                Text = ":", 
                Opacity = 0.6 
            };
            
            var link = new HyperlinkButton { 
                Content = displayText,
                NavigateUri = new Uri(url),
                Padding = new Thickness(0)
            };
            
            panel.Children.Add(nameText);
            panel.Children.Add(separator);
            panel.Children.Add(link);
            
            return panel;
        }

        private UIElement CreateErrorNodeContent(string name, string errorMessage)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            var icon = new FontIcon { 
                Glyph = "\uE783", 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35)) 
            };
            
            var nameText = new TextBlock { 
                Text = name, 
                FontWeight = FontWeights.SemiBold 
            };
            
            var messageText = new TextBlock { 
                Text = errorMessage, 
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35))
            };
            
            panel.Children.Add(icon);
            panel.Children.Add(nameText);
            panel.Children.Add(messageText);
            
            return panel;
        }

        private UIElement CreateSimpleValueNodeContent(string name, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return CreateStringNodeContent(name, element.GetString());
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long longValue))
                    {
                        return CreateNumberNodeContent(name, longValue.ToString());
                    }
                    else
                    {
                        return CreateNumberNodeContent(name, element.GetDouble().ToString());
                    }
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return CreateBooleanNodeContent(name, element.GetBoolean().ToString().ToLower());
                case JsonValueKind.Null:
                    return CreateNullNodeContent(name);
                default:
                    return CreateStringNodeContent(name, "Unknown type");
            }
        }
    }
}

