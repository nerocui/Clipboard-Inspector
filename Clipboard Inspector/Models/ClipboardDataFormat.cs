using System;

namespace Clipboard_Inspector.Models
{
    public class ClipboardDataFormat
    {
        public uint FormatId { get; set; }
        public string FormatName { get; set; }
        public string HandleType { get; set; }
        public uint Size { get; set; }
        public int Index { get; set; }
        public IntPtr DataHandle { get; set; }
        public byte[] Data { get; set; }
    }
}