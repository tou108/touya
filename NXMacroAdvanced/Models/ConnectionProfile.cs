using System;
using Newtonsoft.Json;

namespace NXMacroAdvanced.Models
{
    /// <summary>
    /// Switch への接続方法
    /// </summary>
    public enum ConnectionType
    {
        UsbHid,       // Arduino/Teensy 経由 (COMポートシリアル)
        UsbGadget,    // Raspberry Pi Zero 経由 (USBガジェット)
        Bluetooth,    // Bluetooth 無線接続
    }

    /// <summary>
    /// 接続プロファイル（接続設定を保存）
    /// </summary>
    public class ConnectionProfile
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "デフォルト接続";

        [JsonProperty("type")]
        public ConnectionType Type { get; set; } = ConnectionType.UsbHid;

        // ---- USB HID (Arduino/Teensy) 設定 ----
        [JsonProperty("comPort")]
        public string ComPort { get; set; } = "COM3";

        [JsonProperty("baudRate")]
        public int BaudRate { get; set; } = 115200;

        // ---- USB Gadget (Raspberry Pi Zero) 設定 ----
        [JsonProperty("gadgetComPort")]
        public string GadgetComPort { get; set; } = "COM4";

        [JsonProperty("gadgetTcpIp")]
        public string GadgetTcpIp { get; set; } = "192.168.7.1";

        [JsonProperty("gadgetTcpPort")]
        public int GadgetTcpPort { get; set; } = 5000;

        [JsonProperty("gadgetUseTcp")]
        public bool GadgetUseTcp { get; set; } = false;

        // ---- Bluetooth 設定 ----
        [JsonProperty("btAddress")]
        public string BluetoothAddress { get; set; } = "";

        [JsonProperty("btDeviceName")]
        public string BluetoothDeviceName { get; set; } = "Pro Controller";

        // ---- 共通 ----
        [JsonProperty("sendIntervalMs")]
        public int SendIntervalMs { get; set; } = 16;  // ~60fps

        [JsonProperty("autoReconnect")]
        public bool AutoReconnect { get; set; } = true;

        [JsonProperty("reconnectDelayMs")]
        public int ReconnectDelayMs { get; set; } = 2000;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  スケジューラエントリ
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// スケジューラの繰り返し種別
    /// </summary>
    public enum ScheduleRepeatType
    {
        Once,       // 1回のみ
        Interval,   // 一定間隔で繰り返し
        Daily,      // 毎日同じ時刻
        Weekly,     // 毎週同じ曜日・時刻
    }

    /// <summary>
    /// スケジュールエントリ（いつ・どのマクロを実行するか）
    /// </summary>
    public class ScheduleEntry
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("name")]
        public string Name { get; set; } = "スケジュール";

        [JsonProperty("macroPath")]
        public string MacroFilePath { get; set; } = "";

        [JsonProperty("enabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonProperty("repeatType")]
        public ScheduleRepeatType RepeatType { get; set; } = ScheduleRepeatType.Once;

        // ---- 実行時刻 (Once / Daily / Weekly) ----
        [JsonProperty("scheduledTime")]
        public DateTime ScheduledTime { get; set; } = DateTime.Now.AddMinutes(5);

        // ---- 間隔実行 ----
        [JsonProperty("intervalMs")]
        public int IntervalMs { get; set; } = 60000;  // 1分

        // ---- 曜日 (Weekly用) ----
        [JsonProperty("dayOfWeek")]
        public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

        // ---- 実行回数制限 ----
        [JsonProperty("maxRunCount")]
        public int MaxRunCount { get; set; } = 0;  // 0=無制限

        [JsonIgnore]
        public int RunCount { get; set; } = 0;

        [JsonIgnore]
        public DateTime? LastRunTime { get; set; }

        [JsonIgnore]
        public DateTime? NextRunTime { get; set; }

        /// <summary>
        /// 次の実行時刻を計算
        /// </summary>
        public DateTime? CalculateNextRun(DateTime now)
        {
            if (!IsEnabled) return null;
            if (MaxRunCount > 0 && RunCount >= MaxRunCount) return null;

            return RepeatType switch
            {
                ScheduleRepeatType.Once =>
                    ScheduledTime > now ? ScheduledTime : null,

                ScheduleRepeatType.Interval =>
                    LastRunTime.HasValue
                        ? LastRunTime.Value.AddMilliseconds(IntervalMs)
                        : now.AddMilliseconds(IntervalMs),

                ScheduleRepeatType.Daily =>
                    GetNextDaily(now),

                ScheduleRepeatType.Weekly =>
                    GetNextWeekly(now),

                _ => null
            };
        }

        private DateTime GetNextDaily(DateTime now)
        {
            var target = now.Date.Add(ScheduledTime.TimeOfDay);
            if (target <= now) target = target.AddDays(1);
            return target;
        }

        private DateTime GetNextWeekly(DateTime now)
        {
            int daysUntil = ((int)DayOfWeek - (int)now.DayOfWeek + 7) % 7;
            var target = now.Date.AddDays(daysUntil).Add(ScheduledTime.TimeOfDay);
            if (target <= now) target = target.AddDays(7);
            return target;
        }
    }
}
