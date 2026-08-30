using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Png;

namespace snapvox.foundation.core
{
    public static class UiClipboard
    {



        private static readonly object _textHandlerLock = new object();
        private static readonly System.Collections.Generic.List<(object Owner, Func<string, Task> SetTextAsync)> _textHandlers
            = new System.Collections.Generic.List<(object, Func<string, Task>)>();
        private static Func<Avalonia.Input.Platform.IClipboard> _getClipboard;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string lpszFormat);

        private const uint CF_DIB = 8;
        private const uint GHND = 0x0042;
        private const int DefaultClipboardHistoryPromotionDelayMs = 400;
        private const string SnapVoxEditorImageFormat = "SnapVox.ImageEditorSource";
        private static readonly byte[] SnapVoxEditorImageBytes = { 1 };

        public static void Register(Func<string, Task> setTextAsync) => Register(null, setTextAsync);

        public static void Register(object owner, Func<string, Task> setTextAsync)
        {
            if (setTextAsync == null) return;
            lock (_textHandlerLock)
            {
                if (owner == null)
                {

                    _textHandlers.Clear();
                    _textHandlers.Add((null, setTextAsync));
                    return;
                }

                for (int i = 0; i < _textHandlers.Count; i++)
                {
                    if (ReferenceEquals(_textHandlers[i].Owner, owner))
                    {
                        _textHandlers[i] = (owner, setTextAsync);
                        return;
                    }
                }

                _textHandlers.Add((owner, setTextAsync));
            }
        }

        public static void Unregister(object owner)
        {
            if (owner == null) return;
            lock (_textHandlerLock)
            {
                _textHandlers.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));
            }
        }

        public static void RegisterGetter(Func<Avalonia.Input.Platform.IClipboard> getClipboard) => _getClipboard = getClipboard;
        public static Avalonia.Input.Platform.IClipboard GetClipboard() => _getClipboard?.Invoke();

        public static Task SetTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

            Func<string, Task> handler;
            lock (_textHandlerLock)
            {
                handler = _textHandlers.Count > 0 ? _textHandlers[_textHandlers.Count - 1].SetTextAsync : null;
            }

            if (handler == null)
            {

                LogHelper.GetLogger(typeof(UiClipboard)).Warn("SetTextAsync dropped text: no open window registered a clipboard handler.");
                return Task.CompletedTask;
            }

            return Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await handler(text);
            });
        }


        private static int GetClipboardHistoryPromotionDelayMs()
        {
            try
            {
                var core = snapvox.foundation.IniFile.IniConfig.GetIniSection<CoreConfiguration>();
                if (core != null && core.ClipboardHistoryPromotionDelayMs >= 0) return core.ClipboardHistoryPromotionDelayMs;
            }
            catch { }
            return DefaultClipboardHistoryPromotionDelayMs;
        }

        public static async Task SetFilePathThenImageAsync(string filePath, Image image, bool markSnapVoxEditorImage = false)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                await SetTextAsync(Path.GetFullPath(filePath)).ConfigureAwait(false);
                int promotionDelayMs = GetClipboardHistoryPromotionDelayMs();
                if (promotionDelayMs > 0) await Task.Delay(promotionDelayMs).ConfigureAwait(false);
            }

            await SetImageAsync(image, markSnapVoxEditorImage).ConfigureAwait(false);
        }

        public static async Task<bool> HasSnapVoxEditorImageAsync()
        {
            try
            {
                var clipboard = GetClipboard();
                if (clipboard != null)
                {
                    bool hasAvaloniaFormat = await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var formats = await clipboard.GetFormatsAsync();
                        return formats.Contains(SnapVoxEditorImageFormat);
                    });
                    if (hasAvaloniaFormat)
                    {
                        return true;
                    }
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    uint format = RegisterClipboardFormat(SnapVoxEditorImageFormat);
                    return format != 0 && IsClipboardFormatAvailable(format);
                }
            }
            catch (Exception ex)
            {
                LogHelper.GetLogger(typeof(UiClipboard)).Error("Failed to inspect SnapVox clipboard marker", ex);
            }

            return false;
        }

        public static async Task<Image> GetImageAsync()
        {
            try
            {
                var clipboard = GetClipboard();
                if (clipboard != null)
                {
                    var avaloniaImage = await TryGetAvaloniaClipboardImageAsync(clipboard).ConfigureAwait(false);
                    if (avaloniaImage != null)
                    {
                        return avaloniaImage;
                    }
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return TryGetWin32ClipboardImage();
                }
            }
            catch (Exception ex)
            {
                LogHelper.GetLogger(typeof(UiClipboard)).Error("Failed to get image from clipboard", ex);
            }
            return null;
        }

        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr hMem);

        private static async Task<Image> TryGetAvaloniaClipboardImageAsync(Avalonia.Input.Platform.IClipboard clipboard)
        {
            string[] candidateFormats = null;
            object[] candidateData = null;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var formats = await clipboard.GetFormatsAsync();
                var fmtList = new System.Collections.Generic.List<string>();
                var dataList = new System.Collections.Generic.List<object>();
                foreach (string format in new[] { "PNG", "image/png", "JPEG", "JPG", "JFIF", "image/jpeg", "image/jpg", "Bitmap", "CF_DIB", "DeviceIndependentBitmap" })
                {
                    if (!formats.Contains(format))
                    {
                        continue;
                    }

                    var data = await clipboard.GetDataAsync(format);
                    if (data != null)
                    {
                        fmtList.Add(format);
                        dataList.Add(data);
                    }
                }
                candidateFormats = fmtList.ToArray();
                candidateData = dataList.ToArray();
            });

            if (candidateFormats == null || candidateFormats.Length == 0)
            {
                return null;
            }

            string[] formatsCopy = candidateFormats;
            object[] dataCopy = candidateData;

            return await Task.Run(() =>
            {
                for (int i = 0; i < formatsCopy.Length; i++)
                {
                    var image = TryLoadImageFromData(dataCopy[i]);
                    if (image == null && dataCopy[i] is byte[] dibBytes && (formatsCopy[i].Contains("DIB", StringComparison.OrdinalIgnoreCase) || formatsCopy[i].Contains("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            image = LoadDibImage(dibBytes);
                        }
                        catch
                        {
                        }
                    }

                    if (image != null)
                    {
                        return image;
                    }
                }

                return null;
            }).ConfigureAwait(false);
        }

        private static Image TryLoadImageFromData(object data)
        {
            try
            {
                if (data is byte[] bytes && bytes.Length > 0)
                {
                    using var ms = new MemoryStream(bytes);
                    return Image.Load(ms);
                }

                if (data is Stream stream)
                {
                    using var ms = new MemoryStream();
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    stream.CopyTo(ms);
                    ms.Position = 0;
                    return Image.Load(ms);
                }

                if (data is Avalonia.Media.Imaging.Bitmap bitmap)
                {
                    using var ms = new MemoryStream();
                    bitmap.Save(ms);
                    ms.Position = 0;
                    return Image.Load(ms);
                }
            }
            catch
            {
            }

            return null;
        }

        private static Image TryGetWin32ClipboardImage()
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return null;
            }

            try
            {
                foreach (string formatName in new[] { "PNG", "image/png", "JPEG", "JPG", "JFIF", "image/jpeg", "image/jpg" })
                {
                    uint format = RegisterClipboardFormat(formatName);
                    if (format == 0 || !IsClipboardFormatAvailable(format))
                    {
                        continue;
                    }

                    byte[] bytes = CopyClipboardGlobalBytes(format);
                    if (bytes == null || bytes.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        return Image.Load(ms);
                    }
                    catch
                    {
                    }
                }

                if (IsClipboardFormatAvailable(CF_DIB))
                {
                    byte[] dibData = CopyClipboardGlobalBytes(CF_DIB);
                    if (dibData != null && dibData.Length > 0)
                    {
                        return LoadDibImage(dibData);
                    }
                }
            }
            finally
            {
                CloseClipboard();
            }

            return null;
        }

        private static byte[] CopyClipboardGlobalBytes(uint format)
        {
            IntPtr hGlobal = GetClipboardData(format);
            if (hGlobal == IntPtr.Zero)
            {
                return null;
            }

            IntPtr lpGlobal = GlobalLock(hGlobal);
            if (lpGlobal == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                ulong size = GlobalSize(hGlobal).ToUInt64();
                if (size == 0 || size > int.MaxValue)
                {
                    return null;
                }

                byte[] data = new byte[(int)size];
                Marshal.Copy(lpGlobal, data, 0, data.Length);
                return data;
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
        }

        private static Image LoadDibImage(byte[] dibData)
        {
            int pixelOffset = CalculateDibPixelOffset(dibData);
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((ushort)0x4D42);
            bw.Write((uint)(14 + dibData.Length));
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((uint)(14 + pixelOffset));
            bw.Write(dibData);
            ms.Position = 0;
            return Image.Load(ms);
        }

        private static int CalculateDibPixelOffset(byte[] dibData)
        {
            if (dibData == null || dibData.Length < 4)
            {
                return 0;
            }

            int headerSize = BitConverter.ToInt32(dibData, 0);
            if (headerSize <= 0 || headerSize > dibData.Length)
            {
                return 0;
            }

            if (headerSize < 40 || dibData.Length < 40)
            {
                return headerSize;
            }

            ushort bitCount = BitConverter.ToUInt16(dibData, 14);
            uint compression = BitConverter.ToUInt32(dibData, 16);
            uint colorsUsed = BitConverter.ToUInt32(dibData, 32);
            int colorCount = colorsUsed > 0 ? (int)Math.Min(colorsUsed, 256u) : bitCount <= 8 ? 1 << bitCount : 0;
            int maskBytes = 0;
            if (headerSize == 40)
            {
                if (compression == 3 && (bitCount == 16 || bitCount == 32))
                {
                    maskBytes = 12;
                }
                else if (compression == 6 && bitCount == 32)
                {
                    maskBytes = 16;
                }
            }

            int offset = headerSize + colorCount * 4 + maskBytes;
            if (offset < 0 || offset > dibData.Length)
            {
                return headerSize;
            }

            return offset;
        }

        private static bool SetClipboardBytes(uint format, byte[] bytes)
        {
            if (format == 0 || bytes == null || bytes.Length == 0) return false;

            IntPtr hGlobal = GlobalAlloc(GHND, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero) return false;

            bool ownershipTransferred = false;
            try
            {
                IntPtr lpGlobal = GlobalLock(hGlobal);
                if (lpGlobal == IntPtr.Zero) return false;

                try
                {
                    Marshal.Copy(bytes, 0, lpGlobal, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                if (SetClipboardData(format, hGlobal) != IntPtr.Zero)
                {
                    ownershipTransferred = true;
                    return true;
                }

                return false;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    GlobalFree(hGlobal);
                }
            }
        }

        public static async Task SetImageAsync(Image image, bool markSnapVoxEditorImage = false)
        {
            if (image == null) return;

            try
            {
                var encoded = await Task.Run(() => EncodeClipboardImage(image)).ConfigureAwait(false);
                byte[] dibBytes = encoded.DibBytes;
                byte[] bmpFullBytes = encoded.BmpFullBytes;
                byte[] pngBytes = encoded.PngBytes;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && dibBytes != null)
                {
                    bool success = false;
                    uint snapVoxFormat = markSnapVoxEditorImage ? RegisterClipboardFormat(SnapVoxEditorImageFormat) : 0;
                    for (int i = 0; i < 5; i++)
                    {
                        if (OpenClipboard(IntPtr.Zero))
                        {
                            try
                            {
                                EmptyClipboard();
                                IntPtr hGlobal = GlobalAlloc(GHND, (UIntPtr)dibBytes.Length);
                                bool dibTransferred = false;
                                if (hGlobal != IntPtr.Zero)
                                {
                                    bool ownershipTransferred = false;
                                    try
                                    {
                                        IntPtr lpGlobal = GlobalLock(hGlobal);
                                        if (lpGlobal != IntPtr.Zero)
                                        {
                                            try
                                            {
                                                Marshal.Copy(dibBytes, 0, lpGlobal, dibBytes.Length);
                                            }
                                            finally
                                            {
                                                GlobalUnlock(hGlobal);
                                            }

                                            if (SetClipboardData(CF_DIB, hGlobal) != IntPtr.Zero)
                                            {
                                                ownershipTransferred = true;
                                                dibTransferred = true;
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        if (!ownershipTransferred)
                                        {
                                            GlobalFree(hGlobal);
                                        }
                                    }
                                }
                                if (dibTransferred && snapVoxFormat != 0)
                                {
                                    SetClipboardBytes(snapVoxFormat, SnapVoxEditorImageBytes);
                                }
                                success = dibTransferred;
                                break;
                            }
                            finally { CloseClipboard(); }
                        }
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    if (success) return;
                }

                var clipboard = GetClipboard();
                if (clipboard != null)
                {
                    var dataObject = new DataObject();
                    dataObject.Set("PNG", pngBytes);
                    dataObject.Set("image/png", pngBytes);
                    dataObject.Set("Bitmap", bmpFullBytes);
                    if (markSnapVoxEditorImage)
                    {
                        dataObject.Set(SnapVoxEditorImageFormat, SnapVoxEditorImageBytes);
                    }
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await clipboard.SetDataObjectAsync(dataObject);
                    });
                }
            }
            catch (Exception ex)
            {
                LogHelper.GetLogger(typeof(UiClipboard)).Error("Failed to set image to clipboard", ex);
            }
        }

        private static (byte[] DibBytes, byte[] BmpFullBytes, byte[] PngBytes) EncodeClipboardImage(Image image)
        {
            byte[] dibBytes = null;
            byte[] bmpFullBytes;
            byte[] pngBytes;

            using (var ms = new MemoryStream())
            {
                image.Save(ms, new PngEncoder());
                pngBytes = ms.ToArray();

                ms.SetLength(0);
                image.Save(ms, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel24 });
                bmpFullBytes = ms.ToArray();

                if (bmpFullBytes.Length > 14)
                {
                    dibBytes = new byte[bmpFullBytes.Length - 14];
                    Buffer.BlockCopy(bmpFullBytes, 14, dibBytes, 0, dibBytes.Length);
                }
            }

            return (dibBytes, bmpFullBytes, pngBytes);
        }
    }
}
