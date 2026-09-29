using System;
using System.Collections.Generic;
using System.Linq;
using LMTodo.Models;

namespace LMTodo.ViewModels;

using TodoTaskStatus = LMTodo.Models.TaskStatus;

/// <summary>
/// 筛选 / 排序 / 看板 / 月历的计算。
/// </summary>
public partial class MainWindowViewModel
{
    // =============================================================
    // 基础查询
    // =============================================================

    /// <summary>
    /// 应用搜索 + 标签 + 清单 + 状态 + 逾期 这一组通用筛选。
    /// 所有视图都从这里出发，保证筛选行为一致。
    /// </summary>
    private IEnumerable<TodoItem> BuildBaseQuery(bool includeArchived = false)
    {
        IEnumerable<TodoItem> query = includeArchived
            ? AllTasks
            : AllTasks.Where(x => !x.Archived);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();

            query = query.Where(
                x =>
                    Contains(x.Title, keyword) ||
                    Contains(x.Notes, keyword) ||
                    Contains(x.Project, keyword) ||
                    Contains(x.Assignee, keyword) ||
                    x.Tags.Any(t => Contains(t, keyword)) ||
                    x.SubTasks.Any(s => Contains(s.Title, keyword)) ||
                    x.Comments.Any(c => Contains(c.Text, keyword)));
        }

        if (SelectedTagFilter != "全部标签")
        {
            query = query.Where(
                x =>
                    x.Tags.Any(
                        t => string.Equals(
                            t,
                            SelectedTagFilter,
                            StringComparison.OrdinalIgnoreCase)));
        }

        var listKey = SelectedListFilter?.Key ?? ListFilterOption.AllKey;

        if (listKey == ListFilterOption.NoneKey)
        {
            query = query.Where(x => string.IsNullOrWhiteSpace(x.Project));
        }
        else if (listKey != ListFilterOption.AllKey)
        {
            query = query.Where(
                x => string.Equals(
                    x.Project,
                    listKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        query = StatusFilter switch
        {
            TaskStatusFilter.NotStarted =>
                query.Where(x => x.Status == TodoTaskStatus.NotStarted),

            TaskStatusFilter.InProgress =>
                query.Where(x => x.Status == TodoTaskStatus.InProgress),

            TaskStatusFilter.Completed =>
                query.Where(x => x.Status == TodoTaskStatus.Completed),

            _ => query
        };

        if (OverdueOnly)
        {
            query = query.Where(x => x.IsOverdue);
        }

        return query;
    }

    private static bool Contains(string? source, string keyword)
    {
        return !string.IsNullOrEmpty(source) &&
               source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 排序。所有分支都补了「已完成沉底」和稳定的次级键，
    /// 避免同一批数据每次刷新顺序乱跳。
    /// </summary>
    private static IEnumerable<TodoItem> ApplySort(
        IEnumerable<TodoItem> query,
        SortOption option)
    {
        // 已完成的永远排在后面
        var ordered = query
            .OrderBy(x => x.IsCompleted ? 1 : 0)
            .ThenByDescending(x => x.IsOverdue ? 1 : 0);

        return option switch
        {
            SortOption.Priority => ordered
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.DueDate ?? DateTime.MaxValue)
                .ThenBy(x => x.CreatedAt),

            SortOption.CreatedTime => ordered
                .ThenByDescending(x => x.CreatedAt),

            SortOption.Title => ordered
                .ThenBy(x => x.Title, StringComparer.CurrentCulture),

            _ => ordered
                .ThenBy(x => x.DueDate.HasValue ? 0 : 1)
                .ThenBy(x => x.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(x => x.Priority)
        };
    }

    // =============================================================
    // 列表视图
    // =============================================================

    private void RefreshVisibleTasks()
    {
        VisibleTasks.Clear();

        IEnumerable<TodoItem> query;

        if (CurrentView == ViewMode.Archived)
        {
            query = BuildBaseQuery(includeArchived: true)
                .Where(x => x.Archived);
        }
        else
        {
            query = BuildBaseQuery();

            switch (CurrentView)
            {
                case ViewMode.Today:
                    // 今日 = 今天到期的 + 已经逾期的
                    query = query.Where(
                        x =>
                            x.DueDate.HasValue &&
                            x.DueDate.Value.Date <= DateTime.Today);
                    break;

                case ViewMode.Upcoming:
                    query = query.Where(
                        x =>
                            x.DueDate.HasValue &&
                            x.DueDate.Value.Date > DateTime.Today);
                    break;

                case ViewMode.Habits:
                    query = query.Where(x => x.IsRepeating);
                    break;
            }
        }

        foreach (var task in ApplySort(query, SortOption))
        {
            VisibleTasks.Add(task);
        }
    }

    // =============================================================
    // 看板
    // =============================================================

    /// <summary>
    /// 看板列只在构造时建一次，之后只换内容。
    /// 这样拖动时列的视觉状态不会因为重建而丢失。
    /// </summary>
    private void BuildBoardColumns()
    {
        BoardColumns.Clear();

        BoardColumns.Add(
            new BoardColumnViewModel
            {
                Status = TodoTaskStatus.NotStarted,
                Title = "待办",
                Subtitle = "准备开始",
                AccentColor = "#ACE2F1",
                Items = []
            });

        BoardColumns.Add(
            new BoardColumnViewModel
            {
                Status = TodoTaskStatus.InProgress,
                Title = "进行中",
                Subtitle = "正在处理",
                AccentColor = "#FFCCD5",
                Items = []
            });

        BoardColumns.Add(
            new BoardColumnViewModel
            {
                Status = TodoTaskStatus.Completed,
                Title = "已完成",
                Subtitle = "已经完成的任务",
                AccentColor = "#B9E5D3",
                Items = []
            });
    }

    private void RefreshBoard()
    {
        if (BoardColumns.Count == 0)
        {
            BuildBoardColumns();
        }

        var query = ApplySort(BuildBaseQuery(), SortOption);

        // 先清空再填，Items 本身是 ObservableCollection，UI 会自动增量更新
        foreach (var column in BoardColumns)
        {
            column.Items.Clear();
        }

        foreach (var task in query)
        {
            var column = BoardColumns.FirstOrDefault(c => c.Status == task.Status);

            column?.Items.Add(task);
        }
    }

    // =============================================================
    // 月历
    // =============================================================

    private void RefreshCalendar()
    {
        BuildCalendarMonth();
        BuildCalendarDayList();
    }

    /// <summary>生成 6×7 的月份网格（含上下月补白）。</summary>
    private void BuildCalendarMonth()
    {
        var firstOfMonth = new DateTime(
            CalendarMonth.Year,
            CalendarMonth.Month,
            1);

        // 从所在周的周一开始铺，保证每行都是完整的一周
        var gridStart = TodoItem.StartOfWeek(firstOfMonth);

        var query = BuildBaseQuery().ToList();

        var byDay = query
            .Where(x => x.DueDate.HasValue)
            .GroupBy(x => x.DueDate!.Value.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        CalendarDays.Clear();

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);

            byDay.TryGetValue(date, out var tasks);

            tasks ??= [];

            var cell = new CalendarDayViewModel
            {
                Date = date,
                IsCurrentMonth = date.Month == firstOfMonth.Month &&
                                 date.Year == firstOfMonth.Year,
                IsToday = date == DateTime.Today,
                IsSelected = date == CalendarDate.Date
            };

            cell.Load(
                tasks
                    .OrderByDescending(x => x.Priority)
                    .Select(x => x.Title),
                tasks.Count,
                tasks.Count(x => x.IsOverdue),
                tasks.Count(x => x.IsCompleted));

            CalendarDays.Add(cell);
        }
    }

    /// <summary>选中那天的任务列表。</summary>
    private void BuildCalendarDayList()
    {
        var day = CalendarDate.Date;

        var query = ApplySort(
            BuildBaseQuery()
                .Where(x => x.DueDate.HasValue && x.DueDate.Value.Date == day),
            SortOption);

        CalendarTasks.Clear();

        foreach (var task in query)
        {
            CalendarTasks.Add(task);
        }
    }

    // =============================================================
    // 时间选择器同步
    // =============================================================

    /// <summary>
    /// 任务截止时间被代码改动后，把详情面板里的小时/分钟下拉同步过来。
    /// </summary>
    public void SyncTimeFromTask()
    {
        // 详情面板直接用 TimePicker 绑定 DueTime，
        // 这里只负责把解析结果画刷等派生状态刷新一下。
        SelectedTask?.RefreshDerivedProperties();
    }
}
