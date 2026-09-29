using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LMTodo.Models;

namespace LMTodo.Repositories;

/// <summary>解析结果。</summary>
public sealed record QuickAddResult(
    string Title,
    string Notes,
    DateTime? DueDate,
    TodoPriority Priority,
    List<string> Tags,
    string Project,
    RepeatRule Repeat,
    int ReminderMinutes,
    List<string> SubTasks);

/// <summary>
/// 把一行自然语言变成结构化任务。
///
/// 支持的写法（可任意组合）：
///   明天下午3点开会 #会议 @工作 !高
///   每周一 上午10点 周会 提前30分钟提醒
///   9/30 交论文 [查资料][写大纲][定稿] //记得找导师签字
///   3天后 每晚复盘 每天
///   下周三 下午2点半 面试 紧急
/// </summary>
public static class QuickAddParser
{
    public static QuickAddResult Parse(string input, DateTime now)
    {
        var text = (input ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty();
        }

        var cuts = new List<(int Start, int Length)>();

        // ---------------------------------------------------------
        // 0. 备注：// 或 :: 之后的所有内容
        // ---------------------------------------------------------

        var notes = string.Empty;

        var noteMatch = Regex.Match(text, @"(?://|::)\s*(?<body>.+)$");

        if (noteMatch.Success)
        {
            notes = noteMatch.Groups["body"].Value.Trim();
            Cut(cuts, noteMatch.Index, noteMatch.Length);
        }

        // ---------------------------------------------------------
        // 1. 子任务：[步骤A][步骤B]
        // ---------------------------------------------------------

        var subTasks = new List<string>();

        foreach (Match m in Regex.Matches(text, @"\[(?<t>[^\[\]]+)\]"))
        {
            var step = m.Groups["t"].Value.Trim();

            if (step.Length > 0 && !subTasks.Contains(step))
            {
                subTasks.Add(step);
            }

            Cut(cuts, m.Index, m.Length);
        }

        // ---------------------------------------------------------
        // 2. 标签：#标签
        // ---------------------------------------------------------

        var tags = new List<string>();

        foreach (Match m in Regex.Matches(text, @"#(?<t>[^\s#,，]+)"))
        {
            var tag = m.Groups["t"].Value.Trim();

            if (tag.Length > 0 &&
                !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }

            Cut(cuts, m.Index, m.Length);
        }

        // ---------------------------------------------------------
        // 3. 清单：@清单
        // ---------------------------------------------------------

        var project = string.Empty;

        var projectMatch = Regex.Match(text, @"@(?<t>[^\s#@,，]+)");

        if (projectMatch.Success)
        {
            project = projectMatch.Groups["t"].Value.Trim();
            Cut(cuts, projectMatch.Index, projectMatch.Length);
        }

        // ---------------------------------------------------------
        // 4. 优先级
        //    !紧急/!高/!中/!低、!! !!! !!!!!、!1..!4
        // ---------------------------------------------------------

        var priority = TodoPriority.Medium;

        var priorityMatch = Regex.Match(
            text,
            @"!(?<v>紧急|最高|高|中|普通|低|highest|urgent|high|medium|normal|low|[1-4])\b?",
            RegexOptions.IgnoreCase);

        if (priorityMatch.Success)
        {
            priority = MapPriority(priorityMatch.Groups["v"].Value);
            Cut(cuts, priorityMatch.Index, priorityMatch.Length);
        }
        else
        {
            // 纯感叹号写法：! = 低, !! = 中, !!! = 高, !!!! = 紧急
            var bangMatch = Regex.Match(text, @"(?<![\w!])(?<b>!{1,4})(?![\w!])");

            if (bangMatch.Success)
            {
                priority = bangMatch.Groups["b"].Value.Length switch
                {
                    1 => TodoPriority.Low,
                    2 => TodoPriority.Medium,
                    3 => TodoPriority.High,
                    _ => TodoPriority.Urgent
                };

                Cut(cuts, bangMatch.Index, bangMatch.Length);
            }
        }

        // ---------------------------------------------------------
        // 5. 提醒：提前30分钟提醒 / 提前1小时提醒 / 提前2天提醒
        // ---------------------------------------------------------

        var reminderMinutes = -1;

        var reminderMatch = Regex.Match(
            text,
            @"提前\s*(?<n>\d+|[一二三四五六七八九十两]+)\s*(?<u>分钟|个小时|小时|天|周)\s*(?:提醒|通知)?");

        if (reminderMatch.Success)
        {
            var amount = ParseNumber(reminderMatch.Groups["n"].Value);

            if (amount > 0)
            {
                reminderMinutes = reminderMatch.Groups["u"].Value switch
                {
                    "分钟" => amount,
                    "小时" or "个小时" => amount * 60,
                    "天" => amount * 60 * 24,
                    "周" => amount * 60 * 24 * 7,
                    _ => amount
                };
            }

            Cut(cuts, reminderMatch.Index, reminderMatch.Length);
        }

        // ---------------------------------------------------------
        // 6. 重复规则
        // ---------------------------------------------------------

        var repeat = RepeatRule.None;

        var repeatMatch = Regex.Match(
            text,
            @"每\s*(?<n>\d+|[一二三四五六七八九十两]+)?\s*(?<u>天|日|周|星期|月|年|个工作日|工作日)");

        if (repeatMatch.Success)
        {
            var unit = repeatMatch.Groups["u"].Value;

            repeat = unit switch
            {
                "天" or "日" => RepeatRule.Daily,
                "周" or "星期" => RepeatRule.Weekly,
                "月" => RepeatRule.Monthly,
                "年" => RepeatRule.Yearly,
                "工作日" or "个工作日" => RepeatRule.Weekdays,
                _ => RepeatRule.None
            };

            // 「每2天」当前版本按每天处理（保留规则粒度简单）
            Cut(cuts, repeatMatch.Index, repeatMatch.Length);
        }
        else if (text.Contains("工作日"))
        {
            repeat = RepeatRule.Weekdays;
            CutFirst(cuts, text, "工作日");
        }

        // ---------------------------------------------------------
        // 7. 日期
        // ---------------------------------------------------------

        var date = ParseDate(text, now, cuts);

        // ---------------------------------------------------------
        // 8. 时间
        // ---------------------------------------------------------

        var time = ParseTime(text, now, cuts);

        // ---------------------------------------------------------
        // 9. 汇总
        // ---------------------------------------------------------

        var due = Combine(date, time, now);

        var title = ApplyCuts(text, cuts);

        return new QuickAddResult(
            title,
            notes,
            due,
            priority,
            tags,
            project,
            repeat,
            reminderMinutes,
            subTasks);
    }

    // =============================================================
    // 日期
    // =============================================================

    private static DateTime? ParseDate(
        string text,
        DateTime now,
        List<(int Start, int Length)> cuts)
    {
        var today = now.Date;

        // --- 明确的三字词，先匹配长的 ---

        foreach (var (word, offset) in new (string, int)[]
                 {
                     ("大后天", 3),
                     ("后天", 2),
                     ("明天", 1),
                     ("明日", 1),
                     ("今天", 0),
                     ("今日", 0),
                     ("昨天", -1),
                     ("昨晚", -1)
                 })
        {
            var index = text.IndexOf(word, StringComparison.Ordinal);

            if (index >= 0)
            {
                Cut(cuts, index, word.Length);

                return today.AddDays(offset);
            }
        }

        // --- 今晚 / 明晚 这类词只影响时间，这里先摘掉日期部分 ---

        if (text.Contains("今晚") || text.Contains("今夜"))
        {
            CutFirst(cuts, text, text.Contains("今晚") ? "今晚" : "今夜");

            return today;
        }

        // --- N 天后 / N 周后 / N 个月后 ---

        var afterMatch = Regex.Match(
            text,
            @"(?<n>\d+|[一二三四五六七八九十两]+)\s*(?<u>个)?\s*(?<unit>天|日|周|星期|月|年)\s*后");

        if (afterMatch.Success)
        {
            var amount = ParseNumber(afterMatch.Groups["n"].Value);

            if (amount > 0)
            {
                var result = afterMatch.Groups["unit"].Value switch
                {
                    "天" or "日" => today.AddDays(amount),
                    "周" or "星期" => today.AddDays(amount * 7),
                    "月" => today.AddMonths(amount),
                    "年" => today.AddYears(amount),
                    _ => today
                };

                Cut(cuts, afterMatch.Index, afterMatch.Length);

                return result;
            }
        }

        // --- 下个月 N 号 ---

        var nextMonthMatch = Regex.Match(
            text,
            @"下个?月\s*(?<d>\d{1,2})\s*[日号]");

        if (nextMonthMatch.Success)
        {
            var day = int.Parse(
                nextMonthMatch.Groups["d"].Value,
                CultureInfo.InvariantCulture);

            var first = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            var maxDay = DateTime.DaysInMonth(first.Year, first.Month);

            Cut(cuts, nextMonthMatch.Index, nextMonthMatch.Length);

            return new DateTime(
                first.Year,
                first.Month,
                Math.Clamp(day, 1, maxDay));
        }

        // --- 周几：本周三 / 下周三 / 下下周一 / 周日 ---

        var weekdayMatch = Regex.Match(
            text,
            @"(?<prefix>下下|下个|下|这|本|上)?\s*(?:周|星期|礼拜)\s*(?<d>[一二三四五六日天1-7])");

        if (weekdayMatch.Success)
        {
            var target = ParseWeekday(weekdayMatch.Groups["d"].Value);

            if (target >= 0)
            {
                var prefix = weekdayMatch.Groups["prefix"].Value;

                // 以周一为一周起点
                var thisMonday = TodoItem.StartOfWeek(today);

                var weekOffset = prefix switch
                {
                    "下" or "下个" => 1,
                    "下下" => 2,
                    "上" => -1,
                    _ => 0
                };

                var result = thisMonday
                    .AddDays(weekOffset * 7)
                    .AddDays(target - 1);

                // 「周三」而今天已经周四 → 顺延到下周
                if (weekOffset == 0 && result < today)
                {
                    result = result.AddDays(7);
                }

                Cut(cuts, weekdayMatch.Index, weekdayMatch.Length);

                return result;
            }
        }

        // --- 绝对日期：2026-09-30 / 2026年9月30日 / 9月30日 / 9/30 ---

        var isoMatch = Regex.Match(
            text,
            @"(?<y>\d{4})\s*[-/年]\s*(?<m>\d{1,2})\s*[-/月]\s*(?<d>\d{1,2})\s*[日号]?");

        if (isoMatch.Success)
        {
            var result = BuildDate(
                int.Parse(isoMatch.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(isoMatch.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(isoMatch.Groups["d"].Value, CultureInfo.InvariantCulture));

            Cut(cuts, isoMatch.Index, isoMatch.Length);

            return result;
        }

        var mdMatch = Regex.Match(
            text,
            @"(?<![\d:])(?<m>\d{1,2})\s*(?:月|[-/])\s*(?<d>\d{1,2})\s*[日号]?(?![\d:])");

        if (mdMatch.Success)
        {
            var first = int.Parse(
                mdMatch.Groups["m"].Value,
                CultureInfo.InvariantCulture);

            var second = int.Parse(
                mdMatch.Groups["d"].Value,
                CultureInfo.InvariantCulture);

            // 「15/9」这种把大的当月份是错的，自动换位
            var month = first;
            var day = second;

            if (month > 12 && day <= 12)
            {
                (month, day) = (day, month);
            }

            if (month is >= 1 and <= 12 && day is >= 1 and <= 31)
            {
                var result = BuildDate(today.Year, month, day);

                // 已经过去的日期顺延一年
                if (result < today)
                {
                    result = BuildDate(today.Year + 1, month, day);
                }

                Cut(cuts, mdMatch.Index, mdMatch.Length);

                return result;
            }
        }

        return null;
    }

    private static DateTime BuildDate(int year, int month, int day)
    {
        month = Math.Clamp(month, 1, 12);

        var maxDay = DateTime.DaysInMonth(year, month);

        return new DateTime(year, month, Math.Clamp(day, 1, maxDay));
    }

    private static int ParseWeekday(string value)
    {
        return value switch
        {
            "一" or "1" => 1,
            "二" or "2" => 2,
            "三" or "3" => 3,
            "四" or "4" => 4,
            "五" or "5" => 5,
            "六" or "6" => 6,
            "日" or "天" or "7" => 7,
            _ => -1
        };
    }

    // =============================================================
    // 时间
    // =============================================================

    private static TimeSpan? ParseTime(
        string text,
        DateTime now,
        List<(int Start, int Length)> cuts)
    {
        var isEvening = false;

        foreach (var word in new[] { "今晚", "今夜", "明晚", "昨晚" })
        {
            if (text.Contains(word))
            {
                isEvening = true;
                break;
            }
        }

        var match = Regex.Match(
            text,
            @"(?<period>凌晨|清晨|早上|早晨|上午|中午|下午|傍晚|晚上)?\s*" +
            @"(?<h>\d{1,2})\s*(?:[:：点时])\s*" +
            @"(?<m>\d{1,2}|半|一刻)?\s*分?");

        if (match.Success)
        {
            var hour = int.Parse(
                match.Groups["h"].Value,
                CultureInfo.InvariantCulture);

            var minuteText = match.Groups["m"].Value;

            var minute = minuteText switch
            {
                "" => 0,
                "半" => 30,
                "一刻" => 15,
                _ => int.TryParse(
                        minuteText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                    ? parsed
                    : 0
            };

            // 「14:30」这种带冒号的直接当 24 小时制，不做上下午推断
            var explicit24 =
                match.Value.Contains(':') ||
                match.Value.Contains('：');

            var period = match.Groups["period"].Value;

            if (period.Length == 0 && isEvening)
            {
                period = "晚上";
            }

            hour = ApplyPeriod(hour, period, explicit24);

            Cut(cuts, match.Index, match.Length);

            return new TimeSpan(
                Math.Clamp(hour, 0, 23),
                Math.Clamp(minute, 0, 59),
                0);
        }

        // 没有具体数字，但说了「下午」这类词 → 给个默认时段
        foreach (var (word, hour) in new (string, int)[]
                 {
                     ("凌晨", 6),
                     ("清晨", 7),
                     ("早上", 8),
                     ("早晨", 8),
                     ("上午", 9),
                     ("中午", 12),
                     ("下午", 14),
                     ("傍晚", 18),
                     ("晚上", 20),
                     ("今晚", 20),
                     ("明晚", 20),
                     ("昨晚", 20)
                 })
        {
            var index = text.IndexOf(word, StringComparison.Ordinal);

            if (index >= 0)
            {
                Cut(cuts, index, word.Length);

                return new TimeSpan(hour, 0, 0);
            }
        }

        return null;
    }

    private static int ApplyPeriod(int hour, string period, bool explicit24)
    {
        if (explicit24)
        {
            return hour;
        }

        return period switch
        {
            "凌晨" => hour == 12 ? 0 : hour,

            "清晨" or "早上" or "早晨" or "上午" =>
                hour == 12 ? 12 : hour,

            "中午" => hour < 11 ? hour + 12 : hour,

            "下午" or "傍晚" or "晚上" =>
                hour < 12 ? hour + 12 : hour,

            // 没写时段：1~6 点按下午/晚上理解，7~11 按上午
            _ => hour switch
            {
                >= 1 and <= 6 => hour + 12,
                _ => hour
            }
        };
    }

    // =============================================================
    // 合并日期与时间
    // =============================================================

    private static DateTime? Combine(
        DateTime? date,
        TimeSpan? time,
        DateTime now)
    {
        if (!date.HasValue && !time.HasValue)
        {
            return null;
        }

        var day = date ?? now.Date;
        var clock = time ?? new TimeSpan(9, 0, 0);

        // 没写日期但时间已过 → 顺延到明天
        if (!date.HasValue && time.HasValue)
        {
            var candidate = day.Add(clock);

            if (candidate <= now)
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        // 只写了时段没写具体时间（如「下午」）→ 已经过了就顺延
        if (date.HasValue && !time.HasValue)
        {
            var fallback = day.AddHours(9);

            if (day == now.Date && fallback <= now)
            {
                fallback = day.AddHours(23).AddMinutes(59);
            }

            return fallback;
        }

        return day.Add(clock);
    }

    // =============================================================
    // 工具
    // =============================================================

    private static TodoPriority MapPriority(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "紧急" or "最高" or "urgent" or "highest" or "1" =>
                TodoPriority.Urgent,

            "高" or "high" or "2" => TodoPriority.High,

            "低" or "low" or "4" => TodoPriority.Low,

            _ => TodoPriority.Medium
        };
    }

    private static int ParseNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var numeric))
        {
            return numeric;
        }

        // 中文数字：十二 / 二十 / 两 / 三
        var digits = new Dictionary<char, int>
        {
            ['零'] = 0, ['一'] = 1, ['两'] = 2, ['二'] = 2, ['三'] = 3,
            ['四'] = 4, ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8,
            ['九'] = 9
        };

        var total = 0;
        var current = 0;

        foreach (var ch in value)
        {
            if (ch == '十')
            {
                current = current == 0 ? 10 : current * 10;
                total += current;
                current = 0;
            }
            else if (digits.TryGetValue(ch, out var digit))
            {
                current = current * 10 + digit;
            }
        }

        return total + current;
    }

    /// <summary>记录一段需要从标题里删掉的区间。</summary>
    private static void Cut(
        List<(int Start, int Length)> cuts,
        int start,
        int length)
    {
        if (length <= 0)
        {
            return;
        }

        var end = start + length;

        // 已有区间重叠就不再重复记录，否则会把标题切碎
        foreach (var (existingStart, existingLength) in cuts)
        {
            var existingEnd = existingStart + existingLength;

            if (start < existingEnd && existingStart < end)
            {
                return;
            }
        }

        cuts.Add((start, length));
    }

    private static void CutFirst(
        List<(int Start, int Length)> cuts,
        string text,
        string word)
    {
        var index = text.IndexOf(word, StringComparison.Ordinal);

        if (index >= 0)
        {
            Cut(cuts, index, word.Length);
        }
    }

    /// <summary>把所有标记区间剔除，剩下的就是标题。</summary>
    private static string ApplyCuts(
        string text,
        List<(int Start, int Length)> cuts)
    {
        var buffer = text.ToCharArray();

        foreach (var (start, length) in cuts)
        {
            for (var i = start; i < start + length && i < buffer.Length; i++)
            {
                buffer[i] = ' ';
            }
        }

        var result = new string(buffer);

        result = Regex.Replace(result, @"\s+", " ");

        result = result.Trim(
            ' ',
            ',', '，',
            '.', '。',
            '、',
            ';', '；',
            '-', '—',
            '/', '\\');

        return result.Trim();
    }

    private static QuickAddResult Empty()
    {
        return new QuickAddResult(
            string.Empty,
            string.Empty,
            null,
            TodoPriority.Medium,
            [],
            string.Empty,
            RepeatRule.None,
            -1,
            []);
    }

    /// <summary>给界面做实时预览用：把解析结果翻译成人话。</summary>
    public static string Describe(QuickAddResult result)
    {
        var parts = new List<string>();

        if (result.DueDate.HasValue)
        {
            parts.Add(result.DueDate.Value.ToString("MM-dd HH:mm"));
        }

        if (result.Repeat != RepeatRule.None)
        {
            parts.Add(
                result.Repeat switch
                {
                    RepeatRule.Daily => "每天",
                    RepeatRule.Weekly => "每周",
                    RepeatRule.Monthly => "每月",
                    RepeatRule.Weekdays => "工作日",
                    RepeatRule.Yearly => "每年",
                    _ => string.Empty
                });
        }

        parts.Add(
            result.Priority switch
            {
                TodoPriority.Urgent => "紧急",
                TodoPriority.High => "高优先级",
                TodoPriority.Low => "低优先级",
                _ => "中优先级"
            });

        if (result.Project.Length > 0)
        {
            parts.Add("@" + result.Project);
        }

        if (result.Tags.Count > 0)
        {
            parts.Add(string.Join(" ", result.Tags.Select(x => "#" + x)));
        }

        if (result.ReminderMinutes >= 0)
        {
            parts.Add(
                result.ReminderMinutes == 0
                    ? "到点提醒"
                    : $"提前 {result.ReminderMinutes} 分钟提醒");
        }

        if (result.SubTasks.Count > 0)
        {
            parts.Add($"{result.SubTasks.Count} 个子任务");
        }

        return string.Join(" · ", parts);
    }
}
