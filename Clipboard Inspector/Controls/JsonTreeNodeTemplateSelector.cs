// This class is no longer used in our programmatic approach
// Keeping the file for reference

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipboard_Inspector.Controls
{
    // This implementation is not used in the current version
    public class JsonTreeNodeTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ObjectTemplate { get; set; }
        public DataTemplate ArrayTemplate { get; set; }
        public DataTemplate StringTemplate { get; set; }
        public DataTemplate NumberTemplate { get; set; }
        public DataTemplate BooleanTemplate { get; set; }
        public DataTemplate NullTemplate { get; set; }
        public DataTemplate UrlTemplate { get; set; }
        public DataTemplate ErrorTemplate { get; set; }
        
        protected override DataTemplate SelectTemplateCore(object item)
        {
            return base.SelectTemplateCore(item);
        }
    }
}