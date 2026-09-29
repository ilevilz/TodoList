using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LMTodo.ViewModels;

/// <summary>
/// 清单筛选下拉框的一项。
/// Key 用哨兵值区分「全部」和「未分类」，避免和真实清单重名冲突。
/// </summary>
public partial class ListFilterOption : ObservableObject
{
    /// <summary>全部清单。</summary>
    public const string AllKey = "__all__";

    /// <summary>没有归属清单的任务。</summary>
    public const string NoneKey = "__none__";

    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>清单主色，普通项有效。</summary>
    public string Color { get; init; } = "#ACE2F1";

    [ObservableProperty]
    private int _count;

    /// <summary>「全部清单」这类项不显示计数徽章。</summary>
    public bool ShowCount => Key != AllKey;

    partial void OnCountChanged(int value)
    {
        OnPropertyChanged(nameof(CountLabel));
    }

    public string CountLabel => Count.ToString();
}

/// <summary>
/// 月历里的一格。
/// </summary>
public partial class CalendarDayViewModel : ObservableObject
{
    public required DateTime Date { get; init; }

    [ObservableProperty]
    private bool _isCurrentMonth = true;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    private int _overdueCount;

    [ObservableProperty]
    private int _completedCount;

    /// <summary>格子里最多显示前两条任务标题。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _preview = [];

    public string DayLabel =>
        Date.Day.ToString();

    public bool HasTasks => TaskCount > 0;

    public bool HasOverdue => OverdueCount > 0;

    public string Tooltip =>
        TaskCount == 0
            ? Date.ToString("yyyy-MM-dd")
            : $"{Date:yyyy-MM-dd} · {TaskCount} 项任务";

    partial void OnTaskCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasTasks));
    }

    partial void OnOverdueCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasOverdue));
    }

    /// <summary>把当天的任务塞进格子里。</summary>
    public void Load(IEnumerable<string> titles, int total, int overdue, int completed)
    {
        Preview = titles.Take(2).ToList();
        TaskCount = total;
        OverdueCount = overdue;
        CompletedCount = completed;
    }
}

/// <summary>
/// 看板的一列。用数据驱动渲染，避免在 XAML 里把三列抄三遍；
/// 列头上还挂了 Status，拖拽落点直接读它就知道该改成什么状态。
/// </summary>
public sealed class BoardColumnViewModel
{
    public required Models.TaskStatus Status { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required ObservableCollection<Models.TodoItem> Items { get; init; }

    public string AccentColor { get; init; } = "#ACE2F1";
}
