using Clipboard_Inspector.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Clipboard_Inspector.Services
{
    public class ClipboardService
    {
        #region Win32 API Imports

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

        [DllImport("user32.dll")]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint CountClipboardFormats();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalSize(IntPtr hMem);

        #endregion

        #region Windows Clipboard Format Constants
        // Standard clipboard formats
        public const uint CF_TEXT = 1;
        public const uint CF_BITMAP = 2;
        public const uint CF_METAFILEPICT = 3;
        public const uint CF_SYLK = 4;
        public const uint CF_DIF = 5;
        public const uint CF_TIFF = 6;
        public const uint CF_OEMTEXT = 7;
        public const uint CF_DIB = 8;
        public const uint CF_PALETTE = 9;
        public const uint CF_PENDATA = 10;
        public const uint CF_RIFF = 11;
        public const uint CF_WAVE = 12;
        public const uint CF_UNICODETEXT = 13;
        public const uint CF_ENHMETAFILE = 14;
        public const uint CF_HDROP = 15;
        public const uint CF_LOCALE = 16;
        public const uint CF_DIBV5 = 17;
        public const uint CF_MAX = 18;
        public const uint CF_OWNERDISPLAY = 0x0080;
        public const uint CF_DSPTEXT = 0x0081;
        public const uint CF_DSPBITMAP = 0x0082;
        public const uint CF_DSPMETAFILEPICT = 0x0083;
        public const uint CF_DSPENHMETAFILE = 0x008E;
        #endregion

        private static readonly Dictionary<uint, string> StandardFormatNames = new Dictionary<uint, string>
        {
            { CF_TEXT, "CF_TEXT" },
            { CF_BITMAP, "CF_BITMAP" },
            { CF_METAFILEPICT, "CF_METAFILEPICT" },
            { CF_SYLK, "CF_SYLK" },
            { CF_DIF, "CF_DIF" },
            { CF_TIFF, "CF_TIFF" },
            { CF_OEMTEXT, "CF_OEMTEXT" },
            { CF_DIB, "CF_DIB" },
            { CF_PALETTE, "CF_PALETTE" },
            { CF_PENDATA, "CF_PENDATA" },
            { CF_RIFF, "CF_RIFF" },
            { CF_WAVE, "CF_WAVE" },
            { CF_UNICODETEXT, "CF_UNICODETEXT" },
            { CF_ENHMETAFILE, "CF_ENHMETAFILE" },
            { CF_HDROP, "CF_HDROP" },
            { CF_LOCALE, "CF_LOCALE" },
            { CF_DIBV5, "CF_DIBV5" }
        };

        public List<ClipboardDataFormat> GetClipboardFormats()
        {
            List<ClipboardDataFormat> formats = new List<ClipboardDataFormat>();

            if (!OpenClipboard(IntPtr.Zero))
            {
                throw new Exception("Failed to open clipboard. Error code: " + Marshal.GetLastWin32Error());
            }

            try
            {
                uint format = 0;
                int index = 0;
                
                // Enumerate all available clipboard formats
                while ((format = EnumClipboardFormats(format)) != 0)
                {
                    ClipboardDataFormat dataFormat = new ClipboardDataFormat
                    {
                        FormatId = format,
                        Index = ++index
                    };

                    // Get format name
                    if (format < CF_MAX || StandardFormatNames.ContainsKey(format))
                    {
                        if (StandardFormatNames.TryGetValue(format, out string stdFormatName))
                        {
                            dataFormat.FormatName = stdFormatName;
                        }
                        else
                        {
                            dataFormat.FormatName = $"Unknown Format ({format})";
                        }
                    }
                    else
                    {
                        StringBuilder formatName = new StringBuilder(256);
                        int result = GetClipboardFormatName(format, formatName, formatName.Capacity);
                        if (result > 0)
                        {
                            dataFormat.FormatName = formatName.ToString();
                        }
                        else
                        {
                            dataFormat.FormatName = $"Custom Format ({format})";
                        }
                    }

                    // Get data handle and size
                    IntPtr dataHandle = GetClipboardData(format);
                    dataFormat.DataHandle = dataHandle;
                    dataFormat.HandleType = "Memory";

                    if (dataHandle != IntPtr.Zero)
                    {
                        IntPtr size = GlobalSize(dataHandle);
                        dataFormat.Size = (uint)size.ToInt32();
                        
                        // Get data bytes
                        dataFormat.Data = GetDataFromHandle(dataHandle, format, dataFormat.Size);
                    }

                    formats.Add(dataFormat);
                }
            }
            finally
            {
                CloseClipboard();
            }

            return formats;
        }

        private byte[] GetDataFromHandle(IntPtr dataHandle, uint format, uint size)
        {
            if (dataHandle == IntPtr.Zero || size == 0)
                return null;

            try
            {
                IntPtr dataPointer = GlobalLock(dataHandle);
                if (dataPointer == IntPtr.Zero)
                    return null;

                try
                {
                    byte[] data = new byte[size];
                    Marshal.Copy(dataPointer, data, 0, (int)size);
                    return data;
                }
                finally
                {
                    GlobalUnlock(dataHandle);
                }
            }
            catch
            {
                return null;
            }
        }

        public string GetPreviewForFormat(ClipboardDataFormat format)
        {
            if (format == null || format.Data == null)
                return "No data available";

            // Handle text formats
            if (format.FormatId == CF_TEXT || format.FormatName.Contains("TEXT"))
            {
                return Encoding.ASCII.GetString(format.Data).TrimEnd('\0');
            }
            else if (format.FormatId == CF_UNICODETEXT)
            {
                return Encoding.Unicode.GetString(format.Data).TrimEnd('\0');
            }
            // Handle HTML Format
            else if (format.FormatName.Contains("HTML"))
            {
                return Encoding.UTF8.GetString(format.Data).TrimEnd('\0');
            }
            else
            {
                // For binary data, display a hex dump
                return BitConverter.ToString(format.Data, 0, Math.Min(format.Data.Length, 100))
                    .Replace("-", " ") + (format.Data.Length > 100 ? "..." : "");
            }
        }
    }
}