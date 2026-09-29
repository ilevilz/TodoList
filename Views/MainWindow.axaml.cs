using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LMTodo.Models;
using LMTodo.Services;
using LMTodo.ViewModels;

namespace LMTodo.Views;

using TodoTaskStatus = LMTodo.Models.TaskStatus;

/// <summary>
/// 视图层只做三件事：文件对话框、拖拽、通知弹窗。
/// 其余业务逻辑都在 ViewModel 里。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly DataFormat<TodoItem> TaskDragFormat =
        DataFormat.CreateInProcessFormat<TodoItem>("LMTodo-task");

    private readonly WindowNotificationManager _notificationManager;

    private readonly DispatcherTimer _reminderTimer;

    /// <summary>
    /// 已经提醒过的任务，避免同一条消息反复弹。
    /// 重复任务顺延截止时间后会换一个 key，从而重新提醒。
    /// </summary>
    private readonly HashSet<string> _shownReminders = [];

    private bool _reminderBusy;

    /// <summary>
    /// DataContext 必须在 InitializeComponent 之前挂上，
    /// 否则每个绑定都会先以 null 解析一次并在日志里留一堆噪音。
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;

        InitializeComponent();

        _notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3
        };

        _reminderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };

        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();

        // 关窗前把待保存的状态刷盘
        Closing += (_, _) => ViewModel?.Persist();
    }

    /// <summary>仅供设计器预览使用。</summary>
    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    // =========================================================
    // 选中：点卡片主体 → 右侧详情面板打开这一条
    // =========================================================

    private void TaskCard_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (ViewModel is null ||
            sender is not Control { DataContext: TodoItem task })
        {
            return;
        }

        // 卡片里的勾选框和三个图标按钮自己会处理点击，别抢它们的事件。
        // 看板的 ⋮⋮ 拖拽把手不是 Button，所以拖之前会顺带选中 —— 这是想要的行为。
        if (e.Source is Visual source &&
            source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        ViewModel.SelectTaskCommand.Execute(task);
    }

    // =========================================================
    // 拖拽（看板视图）
    // =========================================================

    private async void TaskDragHandle_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            control.DataContext is not TodoItem task)
        {
            return;
        }

        var data = new DataTransfer();

        data.Add(DataTransferItem.Create(TaskDragFormat, task));

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void Board_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(TaskDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void Board_Drop(object? sender, DragEventArgs e)
    {
        if (sender is not Control target)
        {
            return;
        }

        var task = e.DataTransfer.TryGetValue(TaskDragFormat);

        if (task is null || ViewModel is null)
        {
            return;
        }

        // 列上的 Tag 就是目标状态
        if (!Enum.TryParse<TodoTaskStatus>(
                target.Tag?.ToString(),
                out var status))
        {
            return;
        }

        ViewModel.MoveTaskToStatus(task, status);

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    // =========================================================
    // 提醒（需求 4）
    // =========================================================

    private void CheckReminders()
    {
        if (_reminderBusy)
        {
            return;
        }

        var vm = ViewModel;

        if (vm is null || !vm.NotificationsEnabled)
        {
            return;
        }

        _reminderBusy = true;

        try
        {
            var now = DateTime.Now;

            var events = ReminderService.Collect(vm.AllTasks, now);

            if (events.Count == 0)
            {
                return;
            }

            var dirty = false;

            foreach (var item in events)
            {
                var key = BuildReminderKey(item.Task, now);

                if (!_shownReminders.Contains(key))
                {
                    _notificationManager.Show(
                        new Notification(
                            item.Title,
                            item.Message,
                            item.IsOverdue
                                ? NotificationType.Error
                                : NotificationType.Warning,
                            TimeSpan.FromSeconds(7)));

                    _shownReminders.Add(key);
                }

                item.Task.LastReminderAt = now;
                dirty = true;
            }

            // LastReminderAt 由 VM 的变更处理器识别为「内部字段，不触发保存」，
            // 所以这里手动落一次盘，防止重启后又弹一遍。
            if (dirty)
            {
                vm.Persist();
            }
        }
        catch
        {
            // 提醒失败不该影响主流程
        }
        finally
        {
            _reminderBusy = false;
        }
    }

    private static string BuildReminderKey(TodoItem task, DateTime now)
    {
        // 逾期提醒 12 小时一轮，所以 key 里带上日期段
        var bucket = task.IsOverdue ? now.ToString("yyyyMMddHH") : "due";

        return $"{task.Id:N}:{task.DueDate?.Ticks ?? 0}:{bucket}";
    }

    // =========================================================
    // 导入导出（需求 11）
    // =========================================================

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveFileAsync(
            "导出 CSV",
            "tasks.csv",
            "CSV 文件");

        if (path is not null)
        {
            ViewModel?.ExportCsv(path);
        }
    }

    private async void ExportIcs_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveFileAsync(
            "导出日历",
            "tasks.ics",
            "iCalendar 文件");

        if (path is not null)
        {
            ViewModel?.ExportIcs(path);
        }
    }

    private async void ExportMarkdown_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveFileAsync(
            "导出 Markdown",
            "tasks.md",
            "Markdown 文件");

        if (path is not null)
        {
            ViewModel?.ExportMarkdown(path);
        }
    }

    private async void BackupJson_Click(object? sender, RoutedEventArgs e)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");

        var path = await PickSaveFileAsync(
            "备份数据",
            $"lmtodo-backup-{stamp}.json",
            "JSON 文件");

        if (path is not null)
        {
            ViewModel?.BackupJson(path);
        }
    }

    private async void ImportJsonMerge_Click(object? sender, RoutedEventArgs e)
    {
        await ImportAsync(replace: false);
    }

    private async void ImportJsonReplace_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmAsync(
            "覆盖导入",
            "这会用备份文件替换当前所有任务，且无法撤销。确定继续吗？");

        if (confirmed)
        {
            await ImportAsync(replace: true);
        }
    }

    private async Task ImportAsync(bool replace)
    {
        if (ViewModel is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = replace ? "覆盖导入备份" : "合并导入备份",
                AllowMultiple = false,
                FileTypeFilter = [JsonFileType]
            });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.ImportJson(path, replace);
        }
    }

    // =========================================================
    // 背景壁纸
    // =========================================================

    private async void PickWallpaper_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "选择背景壁纸",
                AllowMultiple = false,
                FileTypeFilter = [ImageFileType]
            });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.SetWallpaper(path);
        }
    }

    private void ClearWallpaper_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ClearWallpaper();
    }

    // =========================================================
    // 对话框辅助
    // =========================================================

    private static readonly FilePickerFileType JsonFileType = new("JSON 文件")
    {
        Patterns = ["*.json"]
    };

    private static readonly FilePickerFileType CsvFileType = new("CSV 文件")
    {
        Patterns = ["*.csv"]
    };

    private static readonly FilePickerFileType IcsFileType = new("iCalendar 文件")
    {
        Patterns = ["*.ics"]
    };

    private static readonly FilePickerFileType MarkdownFileType = new("Markdown 文件")
    {
        Patterns = ["*.md"]
    };

    private static readonly FilePickerFileType ImageFileType = new("图片")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"],
        AppleUniformTypeIdentifiers = ["public.image"],
        MimeTypes = ["image/*"]
    };

    private async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string typeName)
    {
        var type = typeName switch
        {
            "CSV 文件" => CsvFileType,
            "iCalendar 文件" => IcsFileType,
            "Markdown 文件" => MarkdownFileType,
            _ => JsonFileType
        };

        var file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
                ShowOverwritePrompt = true,
                FileTypeChoices = [type]
            });

        return file?.TryGetLocalPath();
    }

    /// <summary>覆盖导入这种不可撤销的操作，先问一句。</summary>
    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var confirm = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.White
        };

        var result = false;

        var ok = new Button
        {
            Content = "确定",
            Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };

        var cancel = new Button { Content = "取消" };

        ok.Click += (_, _) =>
        {
            result = true;
            confirm.Close();
        };

        cancel.Click += (_, _) => confirm.Close();

        confirm.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = 12.5
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { ok, cancel }
                }
            }
        };

        await confirm.ShowDialog(this);

        return result;
    }
}
