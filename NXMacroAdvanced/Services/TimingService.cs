using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NXMacroAdvanced.Services
{
    /// <summary>
    /// フレーム単位の高精度タイミング制御サービス
    /// Nintendo Switch は 60fps で動作するため、1フレーム = 16.667ms の精度が必要
    /// Windows マルチメディアタイマー + Stopwatch で ±0.5ms 以下の精度を実現する
    /// </summary>
    public class TimingService : IDisposable
    {
        // ─────────────────────────────────────────────────────────
        //  Windows マルチメディアタイマー API (1ms 精度)
        // ─────────────────────────────────────────────────────────

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        // ─────────────────────────────────────────────────────────
        //  定数
        // ─────────────────────────────────────────────────────────

        /// <summary>60fps での1フレームの時間 (ティック単位)</summary>
        public static readonly long TicksPerFrame60 = Stopwatch.Frequency / 60;

        /// <summary>60fps での1フレームの時間 (ms)</summary>
        public const double MsPerFrame60 = 1000.0 / 60.0;  // ≈ 16.667ms

        // ─────────────────────────────────────────────────────────
        //  フィールド
        // ─────────────────────────────────────────────────────────

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private bool _disposed;

        public TimingService()
        {
            // Windows タイマー精度を 1ms に設定
            TimeBeginPeriod(1);
        }

        // ─────────────────────────────────────────────────────────
        //  精密スリープ
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 指定ミリ秒だけ精密に待機する
        /// Thread.Sleep より精度が高いが、CPU を少し消費する
        /// </summary>
        public static void PreciseSleep(int milliseconds, CancellationToken ct = default)
        {
            if (milliseconds <= 0) return;

            long targetTicks = Stopwatch.GetTimestamp()
                             + (long)(milliseconds * (Stopwatch.Frequency / 1000.0));

            // 残り時間が 2ms 以上ある間は Thread.Sleep でCPUを開放
            while (!ct.IsCancellationRequested)
            {
                long remaining = targetTicks - Stopwatch.GetTimestamp();
                double remainingMs = remaining * 1000.0 / Stopwatch.Frequency;

                if (remainingMs <= 0) break;
                if (remainingMs > 2.0)
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(10); // ビジーウェイト (最終1ms)
            }
        }

        /// <summary>
        /// 非同期版精密待機
        /// </summary>
        public static async Task PreciseSleepAsync(int milliseconds, CancellationToken ct = default)
        {
            if (milliseconds <= 0) return;
            await Task.Run(() => PreciseSleep(milliseconds, ct), ct);
        }

        // ─────────────────────────────────────────────────────────
        //  フレーム精密待機
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// N フレーム分だけ精密に待機する
        /// </summary>
        public static void WaitFrames(int frames, int fps = 60, CancellationToken ct = default)
        {
            if (frames <= 0) return;
            double msPerFrame = 1000.0 / fps;
            PreciseSleep((int)Math.Round(frames * msPerFrame), ct);
        }

        public static async Task WaitFramesAsync(int frames, int fps = 60, CancellationToken ct = default)
        {
            if (frames <= 0) return;
            double msPerFrame = 1000.0 / fps;
            await PreciseSleepAsync((int)Math.Round(frames * msPerFrame), ct);
        }

        // ─────────────────────────────────────────────────────────
        //  フレームレート制限 (定期送信ループ用)
        // ─────────────────────────────────────────────────────────

        private long _lastFrameTick;

        /// <summary>
        /// 60fps に合わせて次のフレームまで待機する
        /// (定期送信ループの先頭で呼び出す)
        /// </summary>
        public void WaitForNextFrame60()
        {
            long targetTick = _lastFrameTick + TicksPerFrame60;
            long now = Stopwatch.GetTimestamp();

            if (targetTick > now)
            {
                double remainingMs = (targetTick - now) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 2.0)
                    Thread.Sleep((int)(remainingMs - 1.5));
                while (Stopwatch.GetTimestamp() < targetTick)
                    Thread.SpinWait(10);
            }

            _lastFrameTick = Stopwatch.GetTimestamp();
        }

        public void ResetFrameTimer() => _lastFrameTick = Stopwatch.GetTimestamp();

        // ─────────────────────────────────────────────────────────
        //  経過時間計測
        // ─────────────────────────────────────────────────────────

        public void Restart() => _sw.Restart();
        public double ElapsedMs => _sw.Elapsed.TotalMilliseconds;
        public long   ElapsedTicks => _sw.ElapsedTicks;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            TimeEndPeriod(1);
        }
    }
}
