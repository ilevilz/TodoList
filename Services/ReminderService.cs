using System;
using System.Collections.Generic;
using LMTodo.Models;

namespace LMTodo.Services;

/// <summary>一条待弹出的提醒。</summary>
public sealed record ReminderEvent(
    TodoItem Task,
    string Title,
    string Message,
    bool IsOverdue);

/// <summary>
/// 到期提醒的判定逻辑（需求 4）。
///
/// 单独抽出来是为了让「什么时候该提醒」这件事可测、可复用，
/// UI 层只负责把返回的事件丢给通知管理器。
/// </summary>
public static class ReminderService
{
    /// <summary>已逾期的任务，每天最多再提醒一次。</summary>
    private static readonly TimeSpan OverdueRepeat = TimeSpan.FromHours(12);

    public static IReadOnlyList<ReminderEvent> Collect(
        IEnumerable<TodoItem> tasks,
        DateTime now)
    {
        var events = new List<ReminderEvent>();

        foreach (var task in tasks)
        {
            if (task.Archived ||
                task.IsCompleted ||
                !task.ReminderEnabled ||
                !task.DueDate.HasValue)
            {
                continue;
            }

            var due = task.DueDate.Value;

            // ---------------------------------------------
            // 1) 逾期提醒
            // ---------------------------------------------

            if (now > due.AddMinutes(1))
            {
                var lastOverdue = task.LastReminderAt;

                if (lastOverdue.HasValue &&
                    now - lastOverdue.Value < OverdueRepeat)
                {
                    continue;
                }

                var days = (now.Date - due.Date).Days;

                events.Add(
                    new ReminderEvent(
                        task,
                        "LMTodo · 任务已逾期",
                        days > 0
                            ? $"「{task.Title}」已逾期 {days} 天。"
                            : $"「{task.Title}」已经超过截止时间。",
                        IsOverdue: true));

                continue;
            }

            // ---------------------------------------------
            // 2) 到期前提醒
            // ---------------------------------------------

            var remindAt = due.AddMinutes(-task.ReminderMinutesBefore);

            if (now < remindAt)
            {
                continue;
            }

            // 已经为这一轮截止时间提醒过了就不再提醒；
            // 重复任务顺延后 DueDate 变了，自然会重新触发。
            if (task.LastReminderAt.HasValue &&
                task.LastReminderAt.Value >= remindAt)
            {
                continue;
            }

            var minutesLeft = (int)Math.Ceiling((due - now).TotalMinutes);

            var message = minutesLeft <= 0
                ? $"「{task.Title}」已到截止时间。"
                : minutesLeft < 60
                    ? $"「{task.Title}」将在 {minutesLeft} 分钟后到期。"
                    : $"「{task.Title}」将在 {minutesLeft / 60} 小时后到期。";

            events.Add(
                new ReminderEvent(
                    task,
                    "LMTodo · 到期提醒",
                    message,
                    IsOverdue: false));
        }

        return events;
    }

    /// <summary>
    /// 重复任务完成后算出下一次的截止时间。
    /// </summary>
    public static DateTime NextOccurrence(DateTime current, RepeatRule repeat)
    {
        return repeat switch
        {
            RepeatRule.Daily => current.AddDays(1),
            RepeatRule.Weekly => current.AddDays(7),
            RepeatRule.Monthly => current.AddMonths(1),
            RepeatRule.Yearly => current.AddYears(1),
            RepeatRule.Weekdays => NextWeekday(current),

            // 不重复的任务不该调用到这里，兜底返回原值
            _ => current
        };
    }

    /// <summary>下一个工作日（跳过周六周日）。</summary>
    private static DateTime NextWeekday(DateTime current)
    {
        var next = current.AddDays(1);

        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }

        return next;
    }
}
