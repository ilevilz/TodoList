using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTodo.Models;
using LMTodo.Repositories;
using LMTodo.Services;

namespace LMTodo.ViewModels;

using TodoTaskStatus = LMTodo.Models.TaskStatus;

/// <summary>
/// 所有用户操作入口。
/// </summary>
public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _newListName = string.Empty;

    // =============================================================
    // 快速添加（需求 5）
    // =============================================================

    [RelayCommand]
    private void QuickAdd()
    {
        var input = QuickAddText.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        QuickAddResult parsed;

        try
        {
            parsed = QuickAddParser.Parse(input, DateTime.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = $"解析失败：{ex.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            StatusMessage = "请至少输入任务标题";
            return;
        }

        var task = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = parsed.Title,
            Notes = parsed.Notes,
            DueDate = parsed.DueDate,
            Priority = parsed.Priority,
            Project = parsed.Project,
            Repeat = parsed.Repeat,
            Status = TodoTaskStatus.NotStarted,
            Archived = false,
            ReminderMinutesBefore = parsed.ReminderMinutes >= 0
                ? parsed.ReminderMinutes
                : _settings.DefaultReminderMinutes,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        foreach (var tag in parsed.Tags)
        {
            task.Tags.Add(tag);
        }

        foreach (var title in parsed.SubTasks)
        {
            task.SubTasks.Add(
                new SubTask
                {
                    Id = Guid.NewGuid(),
                    Title = title
                });
        }

        AttachTask(task);

        AllTasks.Insert(0, task);

        // 清单下拉里补上新出现的清单
        if (!string.IsNullOrWhiteSpace(task.Project))
        {
            EnsureListDefinitionsFromTasks();
        }

        QuickAddText = string.Empty;
        QuickAddPreview = string.Empty;

        SelectedTask = task;

        Persist();
        RefreshEverything();

        StatusMessage = parsed.DueDate.HasValue
            ? $"已创建：{task.Title} · {task.DueDateLabel}"
            : $"已创建：{task.Title}";
    }

    // =============================================================
    // 状态流转（需求 2）
    // =============================================================

    /// <summary>点卡片主体 → 在右侧详情面板里打开它。</summary>
    [RelayCommand]
    private void SelectTask(TodoItem? task)
    {
        SelectedTask = task;
    }

    [RelayCommand]
    private void StartTask(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.Archived = false;
        task.Status = TodoTaskStatus.InProgress;

        Persist();
        RefreshEverything();

        SelectedTask = task;

        StatusMessage = $"已开始：{task.Title}";
    }

    [RelayCommand]
    private void CompleteTask(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.Archived = false;

        if (task.IsCompleted)
        {
            // 再点一次就是取消完成
            CancelComplete(task);
            return;
        }

        task.RecordCompletion();

        if (task.Repeat == RepeatRule.None)
        {
            task.Status = TodoTaskStatus.Completed;
        }
        else
        {
            // 重复任务：立刻顺延到下一个周期，并保持未完成
            var next = ReminderService.NextOccurrence(
                task.DueDate ?? DateTime.Now,
                task.Repeat);

            task.Status = TodoTaskStatus.NotStarted;
            task.DueDate = next;
            task.LastReminderAt = null;

            StatusMessage = $"已完成一次，下次：{task.DueDateLabel}";
        }

        Persist();
        RefreshEverything();

        if (task.Repeat == RepeatRule.None)
        {
            StatusMessage = $"已完成：{task.Title}";
        }
    }

    [RelayCommand]
    private void CancelComplete(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.RemoveCompletion();
        task.Status = TodoTaskStatus.InProgress;
        task.CompletedAt = null;

        Persist();
        RefreshEverything();

        StatusMessage = $"已取消完成：{task.Title}";
    }

    [RelayCommand]
    private void ToggleComplete(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        if (task.IsCompleted)
        {
            CancelComplete(task);
        }
        else
        {
            CompleteTask(task);
        }
    }

    /// <summary>拖动看板卡片时调用。</summary>
    public void MoveTaskToStatus(TodoItem task, TodoTaskStatus status)
    {
        if (task.Status == status)
        {
            return;
        }

        task.Archived = false;

        // 拖到「已完成」也记一次打卡，拖走则撤销
        if (status == TodoTaskStatus.Completed)
        {
            task.RecordCompletion();
        }
        else if (task.IsCompleted)
        {
            task.RemoveCompletion();
            task.CompletedAt = null;
        }

        task.Status = status;

        Persist();
        RefreshEverything();

        StatusMessage = $"{task.Title} → {task.StatusLabel}";
    }

    [RelayCommand]
    private void ArchiveTask(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.Archived = true;

        if (ReferenceEquals(SelectedTask, task))
        {
            SelectedTask = null;
        }

        Persist();
        RefreshEverything();

        StatusMessage = $"已归档：{task.Title}";
    }

    [RelayCommand]
    private void RestoreTask(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.Archived = false;

        if (CurrentView == ViewMode.Archived)
        {
            CurrentView = ViewMode.All;
        }

        SelectedTask = task;

        Persist();
        RefreshEverything();

        StatusMessage = $"已恢复：{task.Title}";
    }

    [RelayCommand]
    private void DeleteTask(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        var title = task.Title;

        DetachTask(task);
        AllTasks.Remove(task);

        if (ReferenceEquals(SelectedTask, task))
        {
            SelectedTask = null;
        }

        Persist();
        RefreshEverything();

        StatusMessage = $"已删除：{title}";
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        var completed = AllTasks
            .Where(x => !x.Archived && x.IsCompleted)
            .ToList();

        if (completed.Count == 0)
        {
            StatusMessage = "没有已完成的任务需要归档";
            return;
        }

        foreach (var task in completed)
        {
            task.Archived = true;
        }

        Persist();
        RefreshEverything();

        StatusMessage = $"已归档 {completed.Count} 个已完成任务";
    }

    // =============================================================
    // 截止时间快捷操作
    // =============================================================

    /// <summary>参数：today / tomorrow / nextweek / clear</summary>
    [RelayCommand]
    private void SetDuePreset(string? preset)
    {
        if (SelectedTask is null)
        {
            return;
        }

        var task = SelectedTask;

        // 保留原有时分，没有就用 09:00
        var time = task.DueDate?.TimeOfDay ?? new TimeSpan(9, 0, 0);

        switch (preset)
        {
            case "today":
                task.DueDate = DateTime.Today.Add(time);
                break;

            case "tomorrow":
                task.DueDate = DateTime.Today.AddDays(1).Add(time);
                break;

            case "nextweek":
                task.DueDate = DateTime.Today.AddDays(7).Add(time);
                break;

            case "clear":
                task.DueDate = null;
                break;
        }

        Persist();
        RefreshEverything();
    }

    /// <summary>稍后提醒：把提醒时间推后 15 分钟。</summary>
    [RelayCommand]
    private void Snooze(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.LastReminderAt = DateTime.Now.AddMinutes(15);

        Persist();

        StatusMessage = $"「{task.Title}」15 分钟后再提醒";
    }

    // =============================================================
    // 子任务（需求 7）
    // =============================================================

    [RelayCommand]
    private void AddSubTask()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var title = NewSubTaskTitle.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        // 一行里用逗号 / 分号分隔可以一次加多条
        var titles = title
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        foreach (var item in titles)
        {
            var sub = new SubTask
            {
                Id = Guid.NewGuid(),
                Title = item
            };

            AttachSubTask(sub);

            SelectedTask.SubTasks.Add(sub);
        }

        SelectedTask.RefreshDerivedProperties();

        NewSubTaskTitle = string.Empty;

        Persist();
        RefreshEverything();
    }

    [RelayCommand]
    private void DeleteSubTask(SubTask? subTask)
    {
        if (SelectedTask is null || subTask is null)
        {
            return;
        }

        subTask.PropertyChanged -= SubTask_PropertyChanged;

        SelectedTask.SubTasks.Remove(subTask);
        SelectedTask.RefreshDerivedProperties();

        Persist();
        RefreshEverything();
    }

    [RelayCommand]
    private void ToggleSubTask(SubTask? subTask)
    {
        if (subTask is null)
        {
            return;
        }

        subTask.IsCompleted = !subTask.IsCompleted;

        FindOwner(subTask)?.RefreshDerivedProperties();

        Persist();
        RefreshEverything();
    }

    // =============================================================
    // 评论 / 协作（需求 10 的接口预留）
    // =============================================================

    [RelayCommand]
    private void AddComment()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var text = NewCommentText.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            Author = CurrentUser,
            Text = text,
            CreatedAt = DateTime.Now
        };

        SelectedTask.Comments.Add(comment);
        SelectedTask.RefreshDerivedProperties();

        NewCommentText = string.Empty;

        Persist();
        RefreshEverything();

        StatusMessage = comment.HasMentions
            ? $"已评论，提及 {string.Join(" ", comment.Mentions.Select(x => "@" + x))}"
            : "已添加评论";
    }

    [RelayCommand]
    private void DeleteComment(TaskComment? comment)
    {
        if (SelectedTask is null || comment is null)
        {
            return;
        }

        SelectedTask.Comments.Remove(comment);
        SelectedTask.RefreshDerivedProperties();

        Persist();
        RefreshEverything();
    }

    /// <summary>把任务分配给自己。</summary>
    [RelayCommand]
    private void AssignToMe(TodoItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.Assignee = CurrentUser;

        Persist();
        RefreshEverything();

        StatusMessage = $"已分配给 {CurrentUser}";
    }

    // =============================================================
    // 清单管理
    // =============================================================

    [RelayCommand]
    private void CreateList()
    {
        var name = NewListName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (Lists.Any(x => string.Equals(
                x.Name,
                name,
                StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"清单「{name}」已存在";
            return;
        }

        var palette = new[]
        {
            "#ACE2F1", "#FFCCD5", "#B9E5D3", "#FFE3B0", "#D6D3F5", "#FFD9C0"
        };

        Lists.Add(
            new TaskList
            {
                Name = name,
                Color = palette[Lists.Count % palette.Length]
            });

        NewListName = string.Empty;

        Persist();
        RefreshEverything();

        StatusMessage = $"已创建清单：{name}";
    }

    [RelayCommand]
    private void DeleteList(TaskList? list)
    {
        if (list is null)
        {
            return;
        }

        // 清单里的任务不删，只把归属清空
        foreach (var task in AllTasks.Where(
                     x => string.Equals(
                         x.Project,
                         list.Name,
                         StringComparison.OrdinalIgnoreCase)))
        {
            task.Project = string.Empty;
        }

        Lists.Remove(list);

        Persist();
        RefreshEverything();

        StatusMessage = $"已删除清单：{list.Name}";
    }

    /// <summary>把当前选中的任务挪到指定清单。</summary>
    [RelayCommand]
    private void MoveToList(string? listName)
    {
        if (SelectedTask is null)
        {
            return;
        }

        SelectedTask.Project = listName ?? string.Empty;

        Persist();
        RefreshEverything();
    }

    /// <summary>清空搜索框（顶栏那个 × 按钮）。</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    // =============================================================
    // 视图切换
    // =============================================================

    [RelayCommand]
    private void ShowToday() => CurrentView = ViewMode.Today;

    [RelayCommand]
    private void ShowUpcoming() => CurrentView = ViewMode.Upcoming;

    [RelayCommand]
    private void ShowAll() => CurrentView = ViewMode.All;

    [RelayCommand]
    private void ShowCalendar() => CurrentView = ViewMode.Calendar;

    [RelayCommand]
    private void ShowBoard() => CurrentView = ViewMode.Board;

    [RelayCommand]
    private void ShowHabits() => CurrentView = ViewMode.Habits;

    [RelayCommand]
    private void ShowArchived() => CurrentView = ViewMode.Archived;

    // =============================================================
    // 日历导航
    // =============================================================

    [RelayCommand]
    private void PreviousCalendarMonth() =>
        CalendarMonth = CalendarMonth.AddMonths(-1);

    [RelayCommand]
    private void NextCalendarMonth() =>
        CalendarMonth = CalendarMonth.AddMonths(1);

    [RelayCommand]
    private void GoCalendarToday()
    {
        CalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        CalendarDate = DateTime.Today;
    }

    [RelayCommand]
    private void SelectCalendarDay(CalendarDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        CalendarDate = day.Date;

        // 点到上/下月的补白格时，把视图一起翻过去
        if (day.Date.Month != CalendarMonth.Month ||
            day.Date.Year != CalendarMonth.Year)
        {
            CalendarMonth = new DateTime(day.Date.Year, day.Date.Month, 1);
        }
    }

    [RelayCommand]
    private void PreviousCalendarDay() => CalendarDate = CalendarDate.AddDays(-1);

    [RelayCommand]
    private void NextCalendarDay() => CalendarDate = CalendarDate.AddDays(1);

    // =============================================================
    // 导入导出（需求 11）
    // =============================================================

    public string ExportCsv(string path)
    {
        try
        {
            ExportService.ExportCsv(path, AllTasks);

            StatusMessage = $"CSV 已导出：{Path.GetFileName(path)}";

            return StatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV 导出失败：{ex.Message}";
            return StatusMessage;
        }
    }

    public string ExportIcs(string path)
    {
        try
        {
            ExportService.ExportIcs(path, AllTasks);

            StatusMessage = $"日历已导出：{Path.GetFileName(path)}";
            return StatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"日历导出失败：{ex.Message}";
            return StatusMessage;
        }
    }

    public string ExportMarkdown(string path)
    {
        try
        {
            ExportService.ExportMarkdown(path, AllTasks);

            StatusMessage = $"Markdown 已导出：{Path.GetFileName(path)}";
            return StatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Markdown 导出失败：{ex.Message}";
            return StatusMessage;
        }
    }

    public string BackupJson(string path)
    {
        try
        {
            ExportService.ExportJson(path, BuildStore());

            StatusMessage = $"备份完成：{Path.GetFileName(path)}";
            return StatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"备份失败：{ex.Message}";
            return StatusMessage;
        }
    }

    /// <summary>mode: merge = 合并导入；replace = 覆盖导入。</summary>
    public string ImportJson(string path, bool replace)
    {
        try
        {
            var store = ExportService.ImportJson(path);

            if (store is null)
            {
                StatusMessage = "无法解析备份文件";
                return StatusMessage;
            }

            if (replace)
            {
                ApplyStore(store, keepSelection: false);

                StatusMessage =
                    $"已恢复备份：{store.Tasks.Count} 个任务（覆盖）";
            }
            else
            {
                var added = MergeIn(store);

                StatusMessage = $"已合并备份：新增 {added} 个任务";
            }

            Persist();
            RefreshEverything();

            return StatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败：{ex.Message}";
            return StatusMessage;
        }
    }

    /// <summary>按 Id 合并，已存在的取 UpdatedAt 较新的那份。</summary>
    private int MergeIn(TodoStore store)
    {
        var existing = AllTasks.ToDictionary(x => x.Id);

        var added = 0;

        foreach (var task in store.Tasks)
        {
            if (existing.TryGetValue(task.Id, out var current))
            {
                if (task.UpdatedAt > current.UpdatedAt)
                {
                    var index = AllTasks.IndexOf(current);

                    DetachTask(current);

                    AttachTask(task);
                    task.RefreshDerivedProperties();

                    AllTasks[index] = task;
                }

                continue;
            }

            AttachTask(task);
            task.RefreshDerivedProperties();

            AllTasks.Add(task);
            added++;
        }

        foreach (var list in store.Lists)
        {
            if (Lists.All(x => x.Id != list.Id))
            {
                Lists.Add(list);
            }
        }

        EnsureListDefinitionsFromTasks();

        return added;
    }

}
