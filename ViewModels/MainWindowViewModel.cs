using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LMTodo.Models;
using LMTodo.Repositories;
using LMTodo.Services;

namespace LMTodo.ViewModels;

using TodoTaskStatus = LMTodo.Models.TaskStatus;

public enum ViewMode
{
    Today,
    Upcoming,
    All,
    Calendar,
    Board,
    Habits,
    Archived
}

public enum TaskStatusFilter
{
    All,
    NotStarted,
    InProgress,
    Completed
}

public enum SortOption
{
    DueDate,
    Priority,
    CreatedTime,
    Title
}

/// <summary>
/// 主界面的状态与业务中枢。
///
/// 拆成三个 partial 文件：
///   MainWindowViewModel.cs          状态、集合、持久化
///   MainWindowViewModel.Queries.cs  筛选 / 排序 / 看板 / 月历
///   MainWindowViewModel.Commands.cs 所有用户操作
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITodoRepository _repository;
    private readonly SettingsService _settingsService;

    private AppSettings _settings = new();

    /// <summary>批量装载数据时抑制保存与刷新，避免 N 次无谓重算。</summary>
    private bool _loading;

    /// <summary>在代码里改 SelectedTask 时抑制反向回写。</summary>
    private bool _updatingSelection;

    private string _wallpaperPath = string.Empty;
    private Bitmap? _wallpaperSource;

    // =============================================================
    // 集合
    // =============================================================

    public ObservableCollection<TodoItem> AllTasks { get; } = [];

    /// <summary>当前视图下经过筛选与排序的任务。</summary>
    public ObservableCollection<TodoItem> VisibleTasks { get; } = [];

    public ObservableCollection<BoardColumnViewModel> BoardColumns { get; } = [];

    public ObservableCollection<TodoItem> CalendarTasks { get; } = [];

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = [];

    /// <summary>有重复规则的任务，供习惯视图使用。</summary>
    public ObservableCollection<TodoItem> HabitTasks { get; } = [];

    public ObservableCollection<string> AvailableTags { get; } = ["全部标签"];

    public ObservableCollection<ListFilterOption> ListFilterOptions { get; } = [];

    /// <summary>所有清单定义（用于新建 / 重命名 / 删除）。</summary>
    public ObservableCollection<TaskList> Lists { get; } = [];

    // =============================================================
    // 选项源
    // =============================================================

    public IReadOnlyList<TodoPriority> Priorities { get; } =
        Enum.GetValues<TodoPriority>();

    public IReadOnlyList<TodoTaskStatus> Statuses { get; } =
        Enum.GetValues<TodoTaskStatus>();

    public IReadOnlyList<RepeatRule> RepeatRules { get; } =
        Enum.GetValues<RepeatRule>();

    public IReadOnlyList<int> ReminderOptions { get; } =
        [0, 5, 10, 15, 30, 60, 120, 1440];

    public IReadOnlyList<TaskStatusFilter> StatusFilters { get; } =
        Enum.GetValues<TaskStatusFilter>();

    public IReadOnlyList<SortOption> SortOptions { get; } =
        Enum.GetValues<SortOption>();

    // =============================================================
    // 可观察状态
    // =============================================================

    [ObservableProperty]
    private TodoItem? _selectedTask;

    [ObservableProperty]
    private string _quickAddText = string.Empty;

    /// <summary>快速添加的实时解析预览。</summary>
    [ObservableProperty]
    private string _quickAddPreview = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ViewMode _currentView = ViewMode.Today;

    [ObservableProperty]
    private TaskStatusFilter _statusFilter = TaskStatusFilter.All;

    [ObservableProperty]
    private SortOption _sortOption = SortOption.DueDate;

    [ObservableProperty]
    private string _selectedTagFilter = "全部标签";

    [ObservableProperty]
    private ListFilterOption? _selectedListFilter;

    /// <summary>只显示逾期任务。</summary>
    [ObservableProperty]
    private bool _overdueOnly;

    [ObservableProperty]
    private DateTime _calendarDate = DateTime.Today;

    /// <summary>月历当前显示的月份（当月 1 号）。</summary>
    [ObservableProperty]
    private DateTime _calendarMonth =
        new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private string _newSubTaskTitle = string.Empty;

    [ObservableProperty]
    private string _newCommentText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "准备就绪";

    [ObservableProperty]
    private string _currentUser = "我";

    // =============================================================
    // 壁纸
    // =============================================================

    public string WallpaperPath
    {
        get => _wallpaperPath;
        private set
        {
            if (_wallpaperPath == value)
            {
                return;
            }

            _wallpaperPath = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWallpaper));
        }
    }

    public Bitmap? WallpaperSource
    {
        get => _wallpaperSource;
        private set
        {
            if (ReferenceEquals(_wallpaperSource, value))
            {
                return;
            }

            _wallpaperSource?.Dispose();
            _wallpaperSource = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWallpaper));
        }
    }

    public bool HasWallpaper => WallpaperSource is not null;

    // =============================================================
    // 视图判定（导航栏选中态用）
    // =============================================================

    public bool HasSelection => SelectedTask is not null;

    public bool IsTodayView => CurrentView == ViewMode.Today;

    public bool IsUpcomingView => CurrentView == ViewMode.Upcoming;

    public bool IsAllView => CurrentView == ViewMode.All;

    public bool IsCalendarView => CurrentView == ViewMode.Calendar;

    public bool IsBoardView => CurrentView == ViewMode.Board;

    public bool IsHabitsView => CurrentView == ViewMode.Habits;

    public bool IsArchivedView => CurrentView == ViewMode.Archived;

    /// <summary>列表类视图（今日/即将/全部/归档）共用一个列表模板。</summary>
    public bool IsListView =>
        CurrentView is ViewMode.Today
            or ViewMode.Upcoming
            or ViewMode.All
            or ViewMode.Archived;

    public string ViewTitle =>
        CurrentView switch
        {
            ViewMode.Today => "今日",
            ViewMode.Upcoming => "即将到期",
            ViewMode.All => "所有任务",
            ViewMode.Calendar => "日历",
            ViewMode.Board => "看板",
            ViewMode.Habits => "习惯打卡",
            _ => "已归档"
        };

    public string ViewSubtitle =>
        CurrentView switch
        {
            ViewMode.Today => "处理今天以及已经逾期的任务",
            ViewMode.Upcoming => "提前规划未来任务",
            ViewMode.All => "统一管理所有未归档任务",
            ViewMode.Calendar => "按月查看任务分布",
            ViewMode.Board => "拖拽卡片即可调整状态",
            ViewMode.Habits => "重复任务的连续打卡记录",
            _ => "查看和恢复已归档的任务"
        };

    // =============================================================
    // 统计
    // =============================================================

    public int TotalCount =>
        AllTasks.Count(x => !x.Archived);

    public int ActiveCount =>
        AllTasks.Count(x => !x.Archived && x.Status != TodoTaskStatus.Completed);

    public int CompletedCount =>
        AllTasks.Count(x => !x.Archived && x.Status == TodoTaskStatus.Completed);

    public int OverdueCount =>
        AllTasks.Count(x => !x.Archived && x.IsOverdue);

    public int TodayCount =>
        AllTasks.Count(
            x =>
                !x.Archived &&
                !x.IsCompleted &&
                x.DueDate.HasValue &&
                x.DueDate.Value.Date <= DateTime.Today);

    public int UpcomingCount =>
        AllTasks.Count(
            x =>
                !x.Archived &&
                !x.IsCompleted &&
                x.DueDate.HasValue &&
                x.DueDate.Value.Date > DateTime.Today);

    public int ArchivedCount =>
        AllTasks.Count(x => x.Archived);

    public int HabitCount =>
        AllTasks.Count(x => !x.Archived && x.IsRepeating);

    /// <summary>今日完成数，用于统计卡片。</summary>
    public int TodayCompletedCount =>
        AllTasks.Count(
            x =>
                !x.Archived &&
                x.CompletionDates.Any(d => d.Date == DateTime.Today));

    /// <summary>完成率（0-100）。</summary>
    public int CompletionRate
    {
        get
        {
            var total = AllTasks.Count(x => !x.Archived);

            if (total == 0)
            {
                return 0;
            }

            return (int)Math.Round(CompletedCount * 100.0 / total);
        }
    }

    /// <summary>所有任务合并后的连续打卡天数。</summary>
    public int CurrentStreak
    {
        get
        {
            var days = AllTasks
                .SelectMany(x => x.CompletionDates)
                .Select(x => x.Date)
                .Distinct()
                .ToHashSet();

            if (days.Count == 0)
            {
                return 0;
            }

            var cursor = days.Contains(DateTime.Today)
                ? DateTime.Today
                : DateTime.Today.AddDays(-1);

            if (!days.Contains(cursor))
            {
                return 0;
            }

            var streak = 0;

            while (days.Contains(cursor))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }
    }

    // =============================================================
    // 构造
    // =============================================================

    /// <summary>设计时 / 无参构造。</summary>
    public MainWindowViewModel()
        : this(new JsonTodoRepository(), new SettingsService())
    {
    }

    public MainWindowViewModel(
        ITodoRepository repository,
        SettingsService settingsService)
    {
        _repository = repository;
        _settingsService = settingsService;

        LoadWallpaper();
        LoadAll();

        SelectedListFilter = ListFilterOptions.FirstOrDefault();
    }

    // =============================================================
    // 装载与持久化
    // =============================================================

    private void LoadAll()
    {
        _loading = true;

        try
        {
            _settings = _settingsService.Load();

            CurrentUser = string.IsNullOrWhiteSpace(_settings.UserName)
                ? "我"
                : _settings.UserName;

            var store = _repository.Load();

            if (!string.IsNullOrWhiteSpace(_repository.LastError))
            {
                StatusMessage = _repository.LastError!;
            }

            Lists.Clear();

            foreach (var list in store.Lists)
            {
                Lists.Add(list);
            }

            AllTasks.Clear();

            foreach (var item in store.Tasks)
            {
                AttachTask(item);
                item.RefreshDerivedProperties();
                AllTasks.Add(item);
            }

            // 数据里有清单但 Lists 里没有的，补一条定义，避免下拉框缺项
            EnsureListDefinitionsFromTasks();

            CurrentView = _settings.StartOnTodayView
                ? ViewMode.Today
                : ParseViewMode(_settings.LastView);

            BuildBoardColumns();
        }
        finally
        {
            _loading = false;
        }

        RefreshEverything();
    }

    /// <summary>把任务里出现过的清单名补进 Lists。</summary>
    private void EnsureListDefinitionsFromTasks()
    {
        var known = Lists
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var palette = new[]
        {
            "#ACE2F1", "#FFCCD5", "#B9E5D3", "#FFE3B0", "#D6D3F5", "#FFD9C0"
        };

        foreach (var name in AllTasks
                     .Where(x => !string.IsNullOrWhiteSpace(x.Project))
                     .Select(x => x.Project.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (known.Add(name))
            {
                Lists.Add(
                    new TaskList
                    {
                        Name = name,
                        Color = palette[Lists.Count % palette.Length]
                    });
            }
        }
    }

    /// <summary>把当前内存状态落盘。</summary>
    public void Persist()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            _repository.Save(BuildStore());

            StatusMessage = $"本地已保存 · {DateTime.Now:HH:mm:ss}";

            _settings.LastView = CurrentView.ToString();
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    private TodoStore BuildStore()
    {
        return new TodoStore
        {
            Version = 2,
            Tasks = AllTasks.ToList(),
            Lists = Lists.ToList()
        };
    }

    // =============================================================
    // 事件订阅
    // =============================================================

    private void AttachTask(TodoItem item)
    {
        item.PropertyChanged += Item_PropertyChanged;

        foreach (var sub in item.SubTasks)
        {
            sub.PropertyChanged += SubTask_PropertyChanged;
        }
    }

    private void DetachTask(TodoItem item)
    {
        item.PropertyChanged -= Item_PropertyChanged;

        foreach (var sub in item.SubTasks)
        {
            sub.PropertyChanged -= SubTask_PropertyChanged;
        }
    }

    /// <summary>新增子任务后要补订阅。</summary>
    private void AttachSubTask(SubTask sub)
    {
        sub.PropertyChanged -= SubTask_PropertyChanged;
        sub.PropertyChanged += SubTask_PropertyChanged;
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (e.PropertyName is
            nameof(TodoItem.Title) or
            nameof(TodoItem.Notes) or
            nameof(TodoItem.DueDate) or
            nameof(TodoItem.Priority) or
            nameof(TodoItem.Status) or
            nameof(TodoItem.Project) or
            nameof(TodoItem.Archived) or
            nameof(TodoItem.Repeat) or
            nameof(TodoItem.ReminderMinutesBefore) or
            nameof(TodoItem.ReminderEnabled) or
            nameof(TodoItem.Assignee))
        {
            SyncTimeFromTask();
            Persist();
            RefreshEverything();
            return;
        }

        // LastReminderAt 之类的内部字段：只刷新统计，不触发保存
        RaiseCounters();
    }

    /// <summary>
    /// 子任务变化。
    ///
    /// 这里必须从 sender 反查所属任务，而不是用 SelectedTask ——
    /// 否则在列表 / 看板里勾子任务时，进度条不会刷新。
    /// </summary>
    private void SubTask_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (sender is SubTask sub)
        {
            FindOwner(sub)?.RefreshDerivedProperties();
        }

        Persist();
        RefreshEverything();
    }

    private TodoItem? FindOwner(SubTask sub)
    {
        return AllTasks.FirstOrDefault(x => x.SubTasks.Contains(sub));
    }

    // =============================================================
    // 刷新
    // =============================================================

    private void RefreshEverything()
    {
        RefreshFilters();
        RefreshVisibleTasks();
        RefreshBoard();
        RefreshCalendar();
        RefreshHabits();
        RaiseCounters();
    }

    private void RaiseCounters()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(TodayCount));
        OnPropertyChanged(nameof(UpcomingCount));
        OnPropertyChanged(nameof(ArchivedCount));
        OnPropertyChanged(nameof(HabitCount));
        OnPropertyChanged(nameof(TodayCompletedCount));
        OnPropertyChanged(nameof(CompletionRate));
        OnPropertyChanged(nameof(CurrentStreak));
    }

    private void RefreshFilters()
    {
        var previousTag = SelectedTagFilter;
        var previousList = SelectedListFilter?.Key ?? ListFilterOption.AllKey;

        // --- 标签 ---
        AvailableTags.Clear();
        AvailableTags.Add("全部标签");

        foreach (var tag in AllTasks
                     .Where(x => !x.Archived)
                     .SelectMany(x => x.Tags)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.CurrentCulture))
        {
            AvailableTags.Add(tag);
        }

        SelectedTagFilter = AvailableTags.Contains(previousTag)
            ? previousTag
            : "全部标签";

        // --- 清单 ---
        ListFilterOptions.Clear();

        ListFilterOptions.Add(
            new ListFilterOption
            {
                Key = ListFilterOption.AllKey,
                Label = "全部清单"
            });

        ListFilterOptions.Add(
            new ListFilterOption
            {
                Key = ListFilterOption.NoneKey,
                Label = "未分类",
                Color = "#C9D6DC",
                Count = AllTasks.Count(
                    x => !x.Archived && string.IsNullOrWhiteSpace(x.Project))
            });

        foreach (var list in Lists.OrderBy(x => x.Name, StringComparer.CurrentCulture))
        {
            ListFilterOptions.Add(
                new ListFilterOption
                {
                    Key = list.Name,
                    Label = list.Name,
                    Color = list.Color,
                    Count = AllTasks.Count(
                        x =>
                            !x.Archived &&
                            string.Equals(
                                x.Project,
                                list.Name,
                                StringComparison.OrdinalIgnoreCase))
                });
        }

        ListFilterOptions[0].Count = TotalCount;

        SelectedListFilter = ListFilterOptions
            .FirstOrDefault(x => x.Key == previousList)
            ?? ListFilterOptions[0];
    }

    private void RefreshHabits()
    {
        HabitTasks.Clear();

        foreach (var task in AllTasks
                     .Where(x => !x.Archived && x.IsRepeating)
                     .OrderByDescending(x => x.CurrentStreak)
                     .ThenBy(x => x.Title, StringComparer.CurrentCulture))
        {
            HabitTasks.Add(task);
        }
    }

    // =============================================================
    // 属性变化钩子
    // =============================================================

    partial void OnSearchTextChanged(string value) => RefreshOnFilterChange();

    partial void OnStatusFilterChanged(TaskStatusFilter value) =>
        RefreshOnFilterChange();

    partial void OnSortOptionChanged(SortOption value) => RefreshOnFilterChange();

    partial void OnSelectedTagFilterChanged(string value) => RefreshOnFilterChange();

    partial void OnSelectedListFilterChanged(ListFilterOption? value) =>
        RefreshOnFilterChange();

    partial void OnOverdueOnlyChanged(bool value) => RefreshOnFilterChange();

    partial void OnCalendarMonthChanged(DateTime value) => RefreshCalendar();

    partial void OnCalendarDateChanged(DateTime value) => RefreshCalendar();

    private void RefreshOnFilterChange()
    {
        if (_loading)
        {
            return;
        }

        RefreshVisibleTasks();
        RefreshBoard();
        RefreshCalendar();
    }

    partial void OnCurrentViewChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(IsTodayView));
        OnPropertyChanged(nameof(IsUpcomingView));
        OnPropertyChanged(nameof(IsAllView));
        OnPropertyChanged(nameof(IsCalendarView));
        OnPropertyChanged(nameof(IsBoardView));
        OnPropertyChanged(nameof(IsHabitsView));
        OnPropertyChanged(nameof(IsArchivedView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(ViewSubtitle));

        if (_loading)
        {
            return;
        }

        RefreshEverything();

        _settings.LastView = value.ToString();
        _settingsService.Save(_settings);
    }

    partial void OnQuickAddTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            QuickAddPreview = string.Empty;
            return;
        }

        try
        {
            var parsed = QuickAddParser.Parse(value, DateTime.Now);

            QuickAddPreview = QuickAddParser.Describe(parsed);
        }
        catch
        {
            // 输入到一半解析失败很正常，静默忽略
            QuickAddPreview = string.Empty;
        }
    }

    /// <summary>
    /// 把卡片上的选中高亮跟着 SelectedTask 一起搬。
    /// 放在 Changing 里而不是 Changed，是为了在同一帧内先撤旧的再立新的，
    /// 避免出现两张卡同时高亮。
    /// </summary>
    partial void OnSelectedTaskChanging(
        TodoItem? oldValue,
        TodoItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnSelectedTaskChanged(TodoItem? value)
    {
        _updatingSelection = true;

        try
        {
            TagsText = value is null
                ? string.Empty
                : string.Join(", ", value.Tags);

            NewSubTaskTitle = string.Empty;
            NewCommentText = string.Empty;
        }
        finally
        {
            _updatingSelection = false;
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnTagsTextChanged(string value)
    {
        if (_updatingSelection || SelectedTask is null)
        {
            return;
        }

        var tags = value
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('#'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SelectedTask.Tags.Clear();

        foreach (var tag in tags)
        {
            SelectedTask.Tags.Add(tag);
        }

        SelectedTask.RefreshDerivedProperties();

        Persist();
        RefreshEverything();
    }

    // =============================================================
    // 壁纸 / 设置
    // =============================================================

    private void LoadWallpaper()
    {
        try
        {
            var settings = _settingsService.Load();

            if (string.IsNullOrWhiteSpace(settings.WallpaperPath) ||
                !File.Exists(settings.WallpaperPath))
            {
                return;
            }

            WallpaperPath = settings.WallpaperPath;
            WallpaperSource = new Bitmap(settings.WallpaperPath);
        }
        catch
        {
            WallpaperSource = null;
            WallpaperPath = string.Empty;
        }
    }

    /// <summary>设置壁纸。图片读不出来就如实报错，不写进设置里留个坏路径。</summary>
    public void SetWallpaper(string path)
    {
        if (!File.Exists(path))
        {
            StatusMessage = "找不到这个图片文件";
            return;
        }

        Bitmap bitmap;

        try
        {
            bitmap = new Bitmap(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"这张图片读不了：{ex.Message}";
            return;
        }

        WallpaperPath = path;
        WallpaperSource = bitmap;

        _settings.WallpaperPath = path;
        _settingsService.Save(_settings);

        StatusMessage = "壁纸已更新";
    }

    public void ClearWallpaper()
    {
        WallpaperSource = null;
        WallpaperPath = string.Empty;

        _settings.WallpaperPath = string.Empty;
        _settingsService.Save(_settings);

        StatusMessage = "已恢复默认壁纸";
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
    }

    public AppSettings Settings => _settings;

    public void SaveSettings()
    {
        _settings.UserName = CurrentUser;
        _settingsService.Save(_settings);
    }

    /// <summary>用外部数据整体替换内存状态（恢复备份 / 导入后调用）。</summary>
    public void ApplyStore(TodoStore store, bool keepSelection)
    {
        var selectedId = keepSelection ? SelectedTask?.Id : null;

        _loading = true;

        try
        {
            foreach (var task in AllTasks)
            {
                DetachTask(task);
            }

            AllTasks.Clear();

            foreach (var task in store.Tasks)
            {
                AttachTask(task);
                task.RefreshDerivedProperties();
                AllTasks.Add(task);
            }

            Lists.Clear();

            foreach (var list in store.Lists)
            {
                Lists.Add(list);
            }

            EnsureListDefinitionsFromTasks();
        }
        finally
        {
            _loading = false;
        }

        SelectedTask = selectedId.HasValue
            ? AllTasks.FirstOrDefault(x => x.Id == selectedId.Value)
            : null;

        RefreshEverything();

        Persist();
    }

    /// <summary>提醒弹窗总开关，由提醒服务在弹窗前检查。</summary>
    public bool NotificationsEnabled
    {
        get => _settings.NotificationsEnabled;
        set
        {
            _settings.NotificationsEnabled = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private static ViewMode ParseViewMode(string? text)
    {
        return Enum.TryParse<ViewMode>(text, true, out var mode)
            ? mode
            : ViewMode.Today;
    }
}
