using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace LMTodo.Models;

/// <summary>优先级。</summary>
public enum TodoPriority
{
    Low,
    Medium,
    High,
    Urgent
}

/// <summary>任务状态。</summary>
public enum TaskStatus
{
    NotStarted,
    InProgress,
    Completed
}

/// <summary>
/// 重复规则。
///
/// 注意：这些值会被序列化，新增项只能追加在末尾，
/// 否则旧数据会读错。
/// </summary>
public enum RepeatRule
{
    None,
    Daily,
    Weekly,
    Monthly,
    Weekdays,
    Yearly
}

/// <summary>
/// 一条待办任务。
/// </summary>
public sealed class TodoItem : ObservableModel
{
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private DateTime? _dueDate;
    private TodoPriority _priority = TodoPriority.Medium;
    private TaskStatus _status = TaskStatus.NotStarted;
    private string _project = string.Empty;
    private bool _archived;
    private RepeatRule _repeat = RepeatRule.None;
    private int _reminderMinutesBefore = 15;
    private bool _reminderEnabled = true;
    private string _assignee = string.Empty;
    private DateTime? _completedAt;
    private DateTime? _lastReminderAt;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
            {
                Touch();
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (SetField(ref _notes, value))
            {
                Touch();
                OnPropertyChanged(nameof(HasNotes));
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (SetField(ref _dueDate, value))
            {
                Touch();

                OnPropertiesChanged(
                    nameof(DueDateLabel),
                    nameof(DueShortLabel),
                    nameof(DueTime),
                    nameof(HasDueDate),
                    nameof(IsOverdue),
                    nameof(DaysOverdue),
                    nameof(OverdueLabel));
            }
        }
    }

    /// <summary>
    /// 只取时间部分，供 TimePicker 双向绑定。
    /// </summary>
    [JsonIgnore]
    public TimeSpan? DueTime
    {
        get => DueDate?.TimeOfDay;
        set
        {
            if (!DueDate.HasValue)
            {
                if (value.HasValue)
                {
                    // 没有日期时默认落到今天；如果那个点已经过了就顺延到明天
                    var candidate = DateTime.Today.Add(value.Value);

                    DueDate = candidate < DateTime.Now
                        ? candidate.AddDays(1)
                        : candidate;
                }

                return;
            }

            if (!value.HasValue)
            {
                DueDate = DueDate.Value.Date;
                return;
            }

            DueDate = DueDate.Value.Date.Add(value.Value);
        }
    }

    public TodoPriority Priority
    {
        get => _priority;
        set
        {
            if (SetField(ref _priority, value))
            {
                Touch();
                OnPropertyChanged(nameof(PriorityLabel));
            }
        }
    }

    public TaskStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                Touch();

                OnPropertiesChanged(
                    nameof(IsCompleted),
                    nameof(IsActive),
                    nameof(StatusLabel),
                    nameof(IsOverdue),
                    nameof(OverdueLabel));

                if (value == TaskStatus.Completed)
                {
                    _completedAt = DateTime.Now;
                    OnPropertyChanged(nameof(CompletedAtLabel));
                }
            }
        }
    }

    /// <summary>所属清单名（与 TaskList.Name 对应）。</summary>
    public string Project
    {
        get => _project;
        set
        {
            if (SetField(ref _project, value))
            {
                Touch();

                OnPropertiesChanged(
                    nameof(ProjectLabel),
                    nameof(HasProject));
            }
        }
    }

    public ObservableCollection<string> Tags { get; set; } = [];

    public ObservableCollection<SubTask> SubTasks { get; set; } = [];

    /// <summary>完成打卡记录（用于连续天数统计）。</summary>
    public ObservableCollection<DateTime> CompletionDates { get; set; } = [];

    /// <summary>评论（协作预留）。</summary>
    public ObservableCollection<TaskComment> Comments { get; set; } = [];

    public bool Archived
    {
        get => _archived;
        set
        {
            if (SetField(ref _archived, value))
            {
                Touch();
                OnPropertyChanged(nameof(IsOverdue));
            }
        }
    }

    public RepeatRule Repeat
    {
        get => _repeat;
        set
        {
            if (SetField(ref _repeat, value))
            {
                Touch();

                OnPropertiesChanged(
                    nameof(RepeatLabel),
                    nameof(IsRepeating),
                    nameof(CurrentStreak),
                    nameof(BestStreak));
            }
        }
    }

    public int ReminderMinutesBefore
    {
        get => _reminderMinutesBefore;
        set
        {
            if (SetField(ref _reminderMinutesBefore, Math.Max(0, value)))
            {
                Touch();
                OnPropertyChanged(nameof(ReminderLabel));
            }
        }
    }

    /// <summary>是否开启到期提醒。</summary>
    public bool ReminderEnabled
    {
        get => _reminderEnabled;
        set
        {
            if (SetField(ref _reminderEnabled, value))
            {
                Touch();
                OnPropertyChanged(nameof(ReminderLabel));
            }
        }
    }

    /// <summary>负责人（协作预留）。</summary>
    public string Assignee
    {
        get => _assignee;
        set
        {
            if (SetField(ref _assignee, value))
            {
                Touch();

                OnPropertiesChanged(
                    nameof(AssigneeLabel),
                    nameof(HasAssignee));
            }
        }
    }

    /// <summary>最近一次弹出提醒的时间，避免重启后重复提醒。</summary>
    public DateTime? LastReminderAt
    {
        get => _lastReminderAt;
        set => SetField(ref _lastReminderAt, value);
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (SetField(ref _completedAt, value))
            {
                OnPropertyChanged(nameof(CompletedAtLabel));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetField(ref _createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetField(ref _updatedAt, value);
    }

    private bool _isSelected;

    /// <summary>
    /// 是否正在右侧详情面板里被编辑。
    /// 纯界面状态，只用来给卡片打选中样式，不落盘。
    /// </summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    // =============================================================
    // 派生属性
    // =============================================================

    [JsonIgnore]
    public bool IsCompleted => Status == TaskStatus.Completed;

    [JsonIgnore]
    public bool IsActive => Status != TaskStatus.Completed;

    [JsonIgnore]
    public bool IsRepeating => Repeat != RepeatRule.None;

    [JsonIgnore]
    public bool HasDueDate => DueDate.HasValue;

    [JsonIgnore]
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    [JsonIgnore]
    public bool HasProject => !string.IsNullOrWhiteSpace(Project);

    [JsonIgnore]
    public bool HasAssignee => !string.IsNullOrWhiteSpace(Assignee);

    [JsonIgnore]
    public bool HasTags => Tags.Count > 0;

    [JsonIgnore]
    public bool HasSubTasks => SubTasks.Count > 0;

    [JsonIgnore]
    public bool HasComments => Comments.Count > 0;

    /// <summary>标题为空时给一个占位，避免列表里出现空白卡片。</summary>
    [JsonIgnore]
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Title)
            ? "(未命名任务)"
            : Title;

    [JsonIgnore]
    public string StatusLabel =>
        Status switch
        {
            TaskStatus.InProgress => "进行中",
            TaskStatus.Completed => "已完成",
            _ => "待办"
        };

    [JsonIgnore]
    public string PriorityLabel =>
        Priority switch
        {
            TodoPriority.Urgent => "紧急",
            TodoPriority.High => "高",
            TodoPriority.Low => "低",
            _ => "中"
        };

    [JsonIgnore]
    public string RepeatLabel =>
        Repeat switch
        {
            RepeatRule.Daily => "每天",
            RepeatRule.Weekly => "每周",
            RepeatRule.Monthly => "每月",
            RepeatRule.Weekdays => "工作日",
            RepeatRule.Yearly => "每年",
            _ => "不重复"
        };

    [JsonIgnore]
    public string AssigneeLabel =>
        HasAssignee
            ? Assignee
            : "未分配";

    [JsonIgnore]
    public string CompletedAtLabel =>
        CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    [JsonIgnore]
    public string DueDateLabel
    {
        get
        {
            if (!DueDate.HasValue)
            {
                return "无截止时间";
            }

            var due = DueDate.Value;
            var date = due.Date;
            var today = DateTime.Today;

            var dayPart = date == today
                ? "今天"
                : date == today.AddDays(1)
                    ? "明天"
                    : date == today.AddDays(-1)
                        ? "昨天"
                        : date == today.AddDays(2)
                            ? "后天"
                            : date.Year == today.Year
                                ? date.ToString("MM-dd")
                                : date.ToString("yyyy-MM-dd");

            // 整天任务（00:00）不显示时间
            return due.TimeOfDay == TimeSpan.Zero
                ? dayPart
                : $"{dayPart} {due:HH:mm}";
        }
    }

    /// <summary>紧凑标签，用于看板卡片等窄空间。</summary>
    [JsonIgnore]
    public string DueShortLabel
    {
        get
        {
            if (!DueDate.HasValue)
            {
                return "无期限";
            }

            var due = DueDate.Value;

            if (IsOverdue)
            {
                return DaysOverdue <= 0
                    ? "已逾期"
                    : $"逾期 {DaysOverdue} 天";
            }

            if (due.Date == DateTime.Today)
            {
                return due.TimeOfDay == TimeSpan.Zero
                    ? "今天"
                    : $"今天 {due:HH:mm}";
            }

            if (due.Date == DateTime.Today.AddDays(1))
            {
                return due.TimeOfDay == TimeSpan.Zero
                    ? "明天"
                    : $"明天 {due:HH:mm}";
            }

            return due.Year == DateTime.Today.Year
                ? due.ToString("MM-dd")
                : due.ToString("yyyy-MM-dd");
        }
    }

    [JsonIgnore]
    public string ProjectLabel =>
        string.IsNullOrWhiteSpace(Project)
            ? "未分类"
            : Project;

    [JsonIgnore]
    public string TagsLabel =>
        Tags.Count == 0
            ? string.Empty
            : string.Join("  ", Tags.Select(x => $"#{x}"));

    [JsonIgnore]
    public string ReminderLabel =>
        !ReminderEnabled
            ? "提醒已关闭"
            : ReminderMinutesBefore <= 0
                ? "到点提醒"
                : $"提前 {ReminderMinutesBefore} 分钟";

    /// <summary>是否逾期（未完成、未归档、已过截止时间）。</summary>
    [JsonIgnore]
    public bool IsOverdue =>
        DueDate.HasValue &&
        DueDate.Value < DateTime.Now &&
        !IsCompleted &&
        !Archived;

    /// <summary>逾期天数，未逾期为 0。</summary>
    [JsonIgnore]
    public int DaysOverdue
    {
        get
        {
            if (!DueDate.HasValue)
            {
                return 0;
            }

            var days = (DateTime.Today - DueDate.Value.Date).Days;

            return days > 0 ? days : 0;
        }
    }

    [JsonIgnore]
    public string OverdueLabel =>
        IsOverdue
            ? DaysOverdue > 0
                ? $"逾期 {DaysOverdue} 天"
                : "已逾期"
            : string.Empty;

    // =============================================================
    // 子任务进度
    // =============================================================

    [JsonIgnore]
    public int SubTaskCompletedCount =>
        SubTasks.Count(x => x.IsCompleted);

    [JsonIgnore]
    public int SubTaskTotalCount =>
        SubTasks.Count;

    [JsonIgnore]
    public int ProgressPercent
    {
        get
        {
            if (SubTaskTotalCount == 0)
            {
                return IsCompleted ? 100 : 0;
            }

            return (int)Math.Round(
                SubTaskCompletedCount * 100.0 / SubTaskTotalCount);
        }
    }

    [JsonIgnore]
    public string SubTaskProgressLabel =>
        SubTaskTotalCount == 0
            ? "无子任务"
            : $"{SubTaskCompletedCount}/{SubTaskTotalCount} · {ProgressPercent}%";

    // =============================================================
    // 打卡 / 习惯统计
    // =============================================================

    [JsonIgnore]
    public int TotalCompletions =>
        CompletionDates.Count;

    /// <summary>当前连续周期数（天数 / 周数 / 月数，取决于重复规则）。</summary>
    [JsonIgnore]
    public int CurrentStreak
    {
        get
        {
            var periods = BuildPeriodSet();

            if (periods.Count == 0)
            {
                return 0;
            }

            var today = ToPeriod(DateTime.Today);

            var cursor = periods.Contains(today)
                ? today
                : PreviousPeriod(today);

            if (!periods.Contains(cursor))
            {
                return 0;
            }

            var streak = 0;

            while (periods.Contains(cursor))
            {
                streak++;
                cursor = PreviousPeriod(cursor);
            }

            return streak;
        }
    }

    /// <summary>历史最长连续周期数。</summary>
    [JsonIgnore]
    public int BestStreak
    {
        get
        {
            var periods = BuildPeriodSet();

            if (periods.Count == 0)
            {
                return 0;
            }

            var ordered = periods
                .OrderBy(x => x)
                .ToList();

            var best = 1;
            var run = 1;

            for (var i = 1; i < ordered.Count; i++)
            {
                // 只有当前周期正好等于上一个周期的下一周期时才算连续
                if (NextPeriod(ordered[i - 1]) == ordered[i])
                {
                    run++;
                }
                else
                {
                    run = 1;
                }

                best = Math.Max(best, run);
            }

            return best;
        }
    }

    [JsonIgnore]
    public string StreakUnit =>
        Repeat switch
        {
            RepeatRule.Weekly => "周",
            RepeatRule.Monthly => "个月",
            RepeatRule.Yearly => "年",
            _ => "天"
        };

    [JsonIgnore]
    public string StreakLabel =>
        $"{CurrentStreak} {StreakUnit}";

    /// <summary>最近 30 天的打卡点阵，供习惯视图使用。</summary>
    [JsonIgnore]
    public ObservableCollection<HabitDay> HabitDays { get; } = [];

    // =============================================================
    // 行为
    // =============================================================

    /// <summary>
    /// 记录一次完成。重复任务按"每个周期只记一次"来记，
    /// 避免同一天点两次变成两次打卡。
    /// </summary>
    public void RecordCompletion(DateTime? when = null)
    {
        var moment = when ?? DateTime.Now;

        if (Repeat == RepeatRule.None)
        {
            // 一次性任务只记第一次完成
            if (CompletionDates.Count == 0)
            {
                CompletionDates.Add(moment);
            }
        }
        else
        {
            var period = ToPeriod(moment.Date);

            if (!CompletionDates.Any(x => ToPeriod(x.Date) == period))
            {
                CompletionDates.Add(moment);
            }
        }

        RefreshDerivedProperties();
        Touch();
    }

    /// <summary>撤销一次打卡（取消完成时调用）。</summary>
    public void RemoveCompletion()
    {
        if (CompletionDates.Count == 0)
        {
            return;
        }

        if (Repeat == RepeatRule.None)
        {
            CompletionDates.Clear();
        }
        else
        {
            var period = ToPeriod(DateTime.Today);

            var target = CompletionDates
                .FirstOrDefault(x => ToPeriod(x.Date) == period);

            if (target != default)
            {
                CompletionDates.Remove(target);
            }
            else
            {
                // 取消的不是"今天"那一笔，就退掉最近一次
                var latest = CompletionDates
                    .OrderByDescending(x => x)
                    .FirstOrDefault();

                if (latest != default)
                {
                    CompletionDates.Remove(latest);
                }
            }
        }

        RefreshDerivedProperties();
        Touch();
    }

    /// <summary>刷新所有派生属性（改动子任务 / 打卡后调用）。</summary>
    public void RefreshDerivedProperties()
    {
        RebuildHabitDays();

        OnPropertiesChanged(
            nameof(IsOverdue),
            nameof(DaysOverdue),
            nameof(OverdueLabel),
            nameof(SubTaskCompletedCount),
            nameof(SubTaskTotalCount),
            nameof(ProgressPercent),
            nameof(SubTaskProgressLabel),
            nameof(CurrentStreak),
            nameof(BestStreak),
            nameof(StreakLabel),
            nameof(TotalCompletions),
            nameof(TagsLabel),
            nameof(HasTags),
            nameof(HasSubTasks),
            nameof(HasComments),
            nameof(CommentsLabel),
            nameof(DueDateLabel),
            nameof(DueShortLabel));
    }

    [JsonIgnore]
    public string CommentsLabel =>
        Comments.Count == 0
            ? "还没有评论"
            : $"{Comments.Count} 条评论";

    private void RebuildHabitDays()
    {
        var done = CompletionDates
            .Select(x => x.Date)
            .ToHashSet();

        HabitDays.Clear();

        for (var i = 29; i >= 0; i--)
        {
            var date = DateTime.Today.AddDays(-i);

            HabitDays.Add(
                new HabitDay
                {
                    Date = date,
                    Done = done.Contains(date)
                });
        }

        OnPropertyChanged(nameof(HabitDays));
    }

    /// <summary>把某一天映射到它所属的"统计周期"。</summary>
    private DateTime ToPeriod(DateTime date)
    {
        return Repeat switch
        {
            RepeatRule.Weekly => StartOfWeek(date),

            RepeatRule.Monthly =>
                new DateTime(date.Year, date.Month, 1),

            RepeatRule.Yearly =>
                new DateTime(date.Year, 1, 1),

            _ => date.Date
        };
    }

    private DateTime NextPeriod(DateTime period)
    {
        return Repeat switch
        {
            RepeatRule.Weekly => period.AddDays(7),
            RepeatRule.Monthly => period.AddMonths(1),
            RepeatRule.Yearly => period.AddYears(1),
            _ => period.AddDays(1)
        };
    }

    private DateTime PreviousPeriod(DateTime period)
    {
        return Repeat switch
        {
            RepeatRule.Weekly => period.AddDays(-7),
            RepeatRule.Monthly => period.AddMonths(-1),
            RepeatRule.Yearly => period.AddYears(-1),
            _ => period.AddDays(-1)
        };
    }

    private HashSet<DateTime> BuildPeriodSet()
    {
        return CompletionDates
            .Select(x => ToPeriod(x.Date))
            .ToHashSet();
    }

    /// <summary>周一为一周的开始（中文习惯）。</summary>
    public static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;

        return date.Date.AddDays(-offset);
    }

    private void Touch()
    {
        _updatedAt = DateTime.Now;

        OnPropertyChanged(nameof(UpdatedAt));
    }
}

/// <summary>习惯视图里的一个打卡点。</summary>
public sealed class HabitDay
{
    public DateTime Date { get; init; }

    public bool Done { get; init; }

    public string Label =>
        Date.ToString("MM-dd");

    public string Tooltip =>
        $"{Date:yyyy-MM-dd} {(Done ? "已打卡" : "未打卡")}";
}
