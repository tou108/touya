using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Services.Imaging
{
    // ─────────────────────────────────────────────────────────────────────
    //  画面キャプチャサービス
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch の画面をキャプチャするサービス
    /// キャプチャボード (DirectShow) またはウィンドウキャプチャに対応
    /// </summary>
    public class ScreenCaptureService : IDisposable
    {
        private VideoCapture? _capture;
        private int           _deviceIndex = 0;
        private bool          _disposed;

        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool   ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] static extern bool   GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("gdi32.dll")]  static extern bool   BitBlt(IntPtr hdcDest, int nXDest, int nYDest,
            int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// キャプチャボード (DirectShow デバイス) に接続する
        /// </summary>
        public bool OpenCaptureDevice(int deviceIndex = 0)
        {
            _capture?.Dispose();
            _deviceIndex = deviceIndex;
            _capture = new VideoCapture(deviceIndex);
            _capture.Set(VideoCaptureProperties.FrameWidth,  1920);
            _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
            return _capture.IsOpened();
        }

        /// <summary>
        /// 利用可能なキャプチャデバイス数を返す
        /// </summary>
        public static int GetDeviceCount()
        {
            int count = 0;
            for (int i = 0; i < 10; i++)
            {
                using var cap = new VideoCapture(i);
                if (!cap.IsOpened()) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// 現在のフレームをキャプチャして Mat で返す
        /// </summary>
        public Mat? CaptureFrame()
        {
            if (_capture == null || !_capture.IsOpened()) return null;
            var mat = new Mat();
            return _capture.Read(mat) && !mat.Empty() ? mat : null;
        }

        /// <summary>
        /// デスクトップ全体をキャプチャする (キャプチャボードなしの場合)
        /// </summary>
        public static Mat CaptureDesktop(Rectangle? region = null)
        {
            var screen = region ?? new Rectangle(0, 0,
                (int)System.Windows.SystemParameters.PrimaryScreenWidth,
                (int)System.Windows.SystemParameters.PrimaryScreenHeight);

            using var bmp = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using var gfx = Graphics.FromImage(bmp);
            gfx.CopyFromScreen(screen.Location, System.Drawing.Point.Empty, screen.Size);
            return BitmapConverter.ToMat(bmp);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _capture?.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  画像認識サービス
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// OpenCV を使用した画像認識・テンプレートマッチングサービス
    /// </summary>
    public class ImageRecognitionService : IDisposable
    {
        private readonly ScreenCaptureService _captureService;
        private bool _disposed;

        public ImageRecognitionService(ScreenCaptureService captureService)
            => _captureService = captureService;

        // ─────────────────────────────────────────────────────────
        //  テンプレートマッチング
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 現在の画面にテンプレート画像が存在するか確認する
        /// </summary>
        public async Task<bool> MatchAsync(
            string          templatePath,
            double          confidence = 0.9,
            ImageRegion?    region     = null,
            CancellationToken ct       = default)
        {
            var result = await GetMatchResultAsync(templatePath, region, ct);
            return result >= confidence;
        }

        /// <summary>
        /// マッチング信頼度 (0.0〜1.0) を返す
        /// </summary>
        public async Task<double> GetMatchResultAsync(
            string          templatePath,
            ImageRegion?    region  = null,
            CancellationToken ct    = default)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(templatePath)) return 0.0;

                Mat? screen = _captureService.CaptureFrame()
                              ?? ScreenCaptureService.CaptureDesktop();

                using var tmpl   = Cv2.ImRead(templatePath, ImreadModes.Color);
                using var source = region != null
                    ? new Mat(screen, new OpenCvSharp.Rect(region.X, region.Y, region.Width, region.Height))
                    : screen;

                if (source.Empty() || tmpl.Empty()) return 0.0;

                using var result = new Mat();
                Cv2.MatchTemplate(source, tmpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);

                screen.Dispose();
                return maxVal;
            }, ct);
        }

        /// <summary>
        /// テンプレートが見つかった位置を返す (見つからない場合は null)
        /// </summary>
        public async Task<System.Drawing.Point?> FindTemplateAsync(
            string          templatePath,
            double          threshold = 0.8,
            ImageRegion?    region    = null,
            CancellationToken ct      = default)
        {
            return await Task.Run<System.Drawing.Point?>(() =>
            {
                if (!File.Exists(templatePath)) return null;

                Mat? screen = _captureService.CaptureFrame()
                              ?? ScreenCaptureService.CaptureDesktop();

                using var tmpl   = Cv2.ImRead(templatePath, ImreadModes.Color);
                using var source = region != null
                    ? new Mat(screen, new OpenCvSharp.Rect(region.X, region.Y, region.Width, region.Height))
                    : screen;

                if (source.Empty() || tmpl.Empty()) { screen.Dispose(); return null; }

                using var result = new Mat();
                Cv2.MatchTemplate(source, tmpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
                screen.Dispose();

                if (maxVal < threshold) return null;

                int baseX = region?.X ?? 0;
                int baseY = region?.Y ?? 0;
                return new System.Drawing.Point(baseX + maxLoc.X, baseY + maxLoc.Y);
            }, ct);
        }

        /// <summary>
        /// テンプレートが出現するまで待機する
        /// </summary>
        public async Task<bool> WaitForMatchAsync(
            string          templatePath,
            double          confidence = 0.9,
            int             timeoutMs  = 10000,
            ImageRegion?    region     = null,
            CancellationToken ct       = default)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline && !ct.IsCancellationRequested)
            {
                if (await MatchAsync(templatePath, confidence, region, ct))
                    return true;
                await Task.Delay(200, ct);
            }
            return false;
        }

        /// <summary>
        /// スクリーンショットをファイルに保存する
        /// </summary>
        public async Task SaveScreenshotAsync(string outputPath, CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                string? dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Mat? frame = _captureService.CaptureFrame()
                             ?? ScreenCaptureService.CaptureDesktop();
                Cv2.ImWrite(outputPath, frame);
                frame.Dispose();
            }, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  OCR サービス
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tesseract OCR を使用した文字認識サービス
    /// </summary>
    public class OcrService : IDisposable
    {
        private readonly ScreenCaptureService _captureService;
        private Tesseract.TesseractEngine?    _engine;
        private bool _disposed;

        private const string TESSDATA_PATH = "tessdata";
        private const string LANGUAGE      = "jpn+eng";  // 日本語 + 英語

        public OcrService(ScreenCaptureService captureService)
        {
            _captureService = captureService;
            TryInitEngine();
        }

        private void TryInitEngine()
        {
            try
            {
                if (Directory.Exists(TESSDATA_PATH))
                    _engine = new Tesseract.TesseractEngine(TESSDATA_PATH, LANGUAGE,
                                  Tesseract.EngineMode.Default);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OCR 初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 指定領域の文字を認識して返す
        /// </summary>
        public async Task<string> RecognizeAsync(
            ImageRegion?      region = null,
            CancellationToken ct     = default)
        {
            if (_engine == null) return "";

            return await Task.Run(() =>
            {
                Mat? frame = _captureService.CaptureFrame()
                             ?? ScreenCaptureService.CaptureDesktop();

                if (frame == null || frame.Empty()) return "";

                Mat roi = region != null
                    ? new Mat(frame, new OpenCvSharp.Rect(region.X, region.Y, region.Width, region.Height))
                    : frame;

                try
                {
                    // Mat → Bitmap → Pix → Tesseract
                    using var bmp  = BitmapConverter.ToBitmap(roi);
                    using var pix  = Tesseract.PixConverter.ToPix(bmp);
                    using var page = _engine.Process(pix);
                    string text    = page.GetText().Trim();
                    frame.Dispose();
                    return text;
                }
                catch
                {
                    frame.Dispose();
                    return "";
                }
            }, ct);
        }

        /// <summary>
        /// 指定領域に特定テキストが含まれるか確認する
        /// </summary>
        public async Task<bool> ContainsTextAsync(
            string            searchText,
            ImageRegion?      region       = null,
            StringComparison  comparison   = StringComparison.OrdinalIgnoreCase,
            CancellationToken ct           = default)
        {
            string recognized = await RecognizeAsync(region, ct);
            return recognized.Contains(searchText, comparison);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine?.Dispose();
        }
    }
}
