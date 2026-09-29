using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTodo.Models;

namespace LMTodo.Repositories;

/// <summary>
/// 导出 / 导入。
///
/// 覆盖需求 11：
///   CSV   —— 给 Excel / 表格工具
///   ICS   —— iCalendar，可导入 Outlook / Google 日历 / 手机自带日历
///   JSON  —— 完整备份，可原样恢复
///   Markdown —— 便于贴到笔记软件
/// </summary>
public static class ExportService
{
    // =============================================================
    // CSV
    // =============================================================

    public static void ExportCsv(string path, IEnumerable<TodoItem> items)
    {
        var list = items.ToList();

        var builder = new StringBuilder();

        // UTF-8 BOM：Excel 打开中文不乱码
        builder.Append('﻿');

        builder.AppendLine(string.Join(",",
            "Id", "标题", "备注", "状态", "优先级", "截止时间", "是否逾期",
            "逾期天数", "清单", "标签", "重复", "提醒", "负责人",
            "子任务进度", "子任务", "评论数", "打卡次数", "连续打卡",
            "已归档", "创建时间", "完成时间", "更新时间"));

        foreach (var item in list)
        {
            builder.AppendLine(string.Join(",",
                Csv(item.Id.ToString()),
                Csv(item.Title),
                Csv(item.Notes),
                Csv(item.StatusLabel),
                Csv(item.PriorityLabel),
                Csv(item.DueDateLabel),
                Csv(item.IsOverdue ? "是" : "否"),
                Csv(item.DaysOverdue.ToString()),
                Csv(item.Project),
                Csv(string.Join(" ", item.Tags)),
                Csv(item.RepeatLabel),
                Csv(item.ReminderLabel),
                Csv(item.Assignee),
                Csv(item.SubTaskProgressLabel),
                Csv(string.Join(" | ", item.SubTasks.Select(
                    s => (s.IsCompleted ? "[x] " : "[ ] ") + s.Title))),
                Csv(item.Comments.Count.ToString()),
                Csv(item.TotalCompletions.ToString()),
                Csv(item.CurrentStreak.ToString()),
                Csv(item.Archived ? "是" : "否"),
                Csv(item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                Csv(item.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"))));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    // =============================================================
    // iCalendar
    // =============================================================

    public static void ExportIcs(string path, IEnumerable<TodoItem> items)
    {
        var builder = new StringBuilder();

        builder.AppendLine("BEGIN:VCALENDAR");
        builder.AppendLine("VERSION:2.0");
        builder.AppendLine("PRODID:-//LMTodo//Todo//CN");
        builder.AppendLine("CALSCALE:GREGORIAN");
        builder.AppendLine("METHOD:PUBLISH");
        builder.AppendLine("X-WR-CALNAME:LMTodo");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

        foreach (var item in items.Where(x => x.DueDate.HasValue))
        {
            var due = item.DueDate!.Value;

            builder.AppendLine("BEGIN:VTODO");
            builder.AppendLine($"UID:{item.Id:N}@lmtodo");
            builder.AppendLine($"DTSTAMP:{stamp}");

            // 用浮动时间（不带 Z），这样导入后按本地时间显示
            builder.AppendLine(
                $"DUE:{due:yyyyMMdd'T'HHmmss}");

            builder.AppendLine(
                $"SUMMARY:{EscapeIcs(item.Title)}");

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                builder.AppendLine(
                    $"DESCRIPTION:{EscapeIcs(item.Notes)}");
            }

            builder.AppendLine(
                $"PRIORITY:{MapIcsPriority(item.Priority)}");

            builder.AppendLine(
                $"STATUS:{(item.IsCompleted ? "COMPLETED" : "NEEDS-ACTION")}");

            if (item.IsCompleted && item.CompletedAt.HasValue)
            {
                builder.AppendLine(
                    $"COMPLETED:{item.CompletedAt.Value:yyyyMMdd'T'HHmmss}");
            }

            if (!string.IsNullOrWhiteSpace(item.Project))
            {
                builder.AppendLine(
                    $"CATEGORIES:{EscapeIcs(item.Project)}");
            }

            if (item.Tags.Count > 0)
            {
                builder.AppendLine(
                    $"CATEGORIES:{EscapeIcs(string.Join(",", item.Tags))}");
            }

            if (item.Repeat != RepeatRule.None)
            {
                builder.AppendLine(
                    $"RRULE:{MapIcsRepeat(item.Repeat)}");
            }

            if (item.ReminderEnabled && item.IsActive)
            {
                builder.AppendLine("BEGIN:VALARM");
                builder.AppendLine("ACTION:DISPLAY");
                builder.AppendLine(
                    $"DESCRIPTION:{EscapeIcs(item.Title)}");
                builder.AppendLine(
                    $"TRIGGER:-PT{item.ReminderMinutesBefore}M");
                builder.AppendLine("END:VALARM");
            }

            builder.AppendLine("END:VTODO");
        }

        builder.AppendLine("END:VCALENDAR");

        // iCalendar 规定用 CRLF
        var content = builder
            .ToString()
            .Replace("\r\n", "\n")
            .Replace("\n", "\r\n");

        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static int MapIcsPriority(TodoPriority priority)
    {
        return priority switch
        {
            TodoPriority.Urgent => 1,
            TodoPriority.High => 3,
            TodoPriority.Medium => 5,
            _ => 9
        };
    }

    private static string MapIcsRepeat(RepeatRule repeat)
    {
        return repeat switch
        {
            RepeatRule.Daily => "FREQ=DAILY",
            RepeatRule.Weekly => "FREQ=WEEKLY",
            RepeatRule.Monthly => "FREQ=MONTHLY",
            RepeatRule.Yearly => "FREQ=YEARLY",
            RepeatRule.Weekdays => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
            _ => "FREQ=DAILY"
        };
    }

    /// <summary>iCalendar 转义：反斜杠、分号、逗号、换行。</summary>
    private static string EscapeIcs(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");
    }

    // =============================================================
    // Markdown
    // =============================================================

    public static void ExportMarkdown(string path, IEnumerable<TodoItem> items)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# LMTodo 任务导出");
        builder.AppendLine();
        builder.AppendLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        builder.AppendLine();

        var groups = items
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Project)
                ? "未分类"
                : x.Project)
            .OrderBy(x => x.Key);

        foreach (var group in groups)
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();

            foreach (var item in group.OrderBy(x => x.DueDate ?? DateTime.MaxValue))
            {
                var box = item.IsCompleted ? "x" : " ";

                var meta = new List<string>();

                if (item.DueDate.HasValue)
                {
                    meta.Add(item.DueDateLabel);
                }

                meta.Add(item.PriorityLabel);

                if (item.IsOverdue)
                {
                    meta.Add("⚠ 已逾期");
                }

                builder.AppendLine(
                    $"- [{box}] **{item.Title}** · {string.Join(" · ", meta)}");

                if (!string.IsNullOrWhiteSpace(item.Notes))
                {
                    builder.AppendLine($"  - 备注：{item.Notes}");
                }

                foreach (var tag in item.Tags)
                {
                    builder.AppendLine($"  - 标签：#{tag}");
                }

                foreach (var sub in item.SubTasks)
                {
                    builder.AppendLine(
                        $"  - [{(sub.IsCompleted ? "x" : " ")}] {sub.Title}");
                }
            }

            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    // =============================================================
    // JSON 备份 / 恢复
    // =============================================================

    private static readonly JsonSerializerOptions BackupOptions =
        new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    public static void ExportJson(string path, TodoStore store)
    {
        var json = JsonSerializer.Serialize(store, BackupOptions);

        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public static void ExportJson(string path, IEnumerable<TodoItem> items)
    {
        ExportJson(
            path,
            new TodoStore { Tasks = items.ToList() });
    }

    /// <summary>
    /// 从备份文件读回。同时兼容：
    ///   1) 完整 TodoStore
    ///   2) 裸 TodoItem 数组
    /// </summary>
    public static TodoStore? ImportJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var store = JsonSerializer.Deserialize<TodoStore>(
                json,
                BackupOptions);

            if (store is not null && store.Tasks.Count > 0)
            {
                Migration.FillMissingIds(store);
                return store;
            }
        }
        catch
        {
            // 落到下面按裸数组再试一次
        }

        try
        {
            var legacy = JsonSerializer.Deserialize<List<TodoItem>>(
                json,
                BackupOptions);

            if (legacy is not null && legacy.Count > 0)
            {
                var store = new TodoStore { Version = 1 };

                store.Tasks.AddRange(legacy);
                Migration.FillMissingIds(store);

                return store;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    // =============================================================

    private static string Csv(string? value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
