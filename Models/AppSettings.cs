namespace LMTodo.Models;

/// <summary>
/// 应用设置。整体序列化到 settings.json。
/// </summary>
public sealed class AppSettings
{
    public string WallpaperPath { get; set; } = string.Empty;

    /// <summary>当前用户名，用于协作场景下的评论作者 / 负责人。</summary>
    public string UserName { get; set; } = "我";

    /// <summary>新建任务的默认提醒提前量（分钟）。</summary>
    public int DefaultReminderMinutes { get; set; } = 15;

    /// <summary>是否开启桌面通知。</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>启动时把「今日」视图作为默认页。</summary>
    public bool StartOnTodayView { get; set; } = true;

    /// <summary>上次所在视图，重启后恢复。</summary>
    public string LastView { get; set; } = "Today";
}
