using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LMTodo.Models;

namespace LMTodo.Converters;

// =============================================================
// 说明：
// 这里的枚举文案刻意与 Models/TodoItem.cs 里的
// StatusLabel / PriorityLabel / RepeatLabel 保持一致，
// 卡片上的短文案和下拉框里的长文案只是详略不同，用词不打架。
// =============================================================

/// <summary>逾期 → 红，正常 → 灰。用在截止时间文字上。</summary>
public sealed class DueDateBrushConverter : IValueConverter
{
    private static readonly IBrush Overdue =
        new SolidColorBrush(Color.Parse("#E0506E"));

    private static readonly IBrush Normal =
        new SolidColorBrush(Color.Parse("#8FA6B0"));

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is true ? Overdue : Normal;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// 枚举 → 中文。用于详情面板的下拉框（配合 x:CompileBindings="False"，
/// 这样 {Binding} 拿到的是枚举值本身）。
/// </summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value switch
        {
            TodoPriority p => PriorityText(p),

            // 下拉框里用完整说法，卡片上才用「高 / 中 / 低」这种短标签
            TaskStatus s => s switch
            {
                Models.TaskStatus.InProgress => "进行中",
                Models.TaskStatus.Completed => "已完成",
                _ => "待办"
            },

            RepeatRule r => r switch
            {
                RepeatRule.Daily => "每天",
                RepeatRule.Weekly => "每周",
                RepeatRule.Monthly => "每月",
                RepeatRule.Weekdays => "工作日",
                RepeatRule.Yearly => "每年",
                _ => "不重复"
            },

            int minutes => ReminderText(minutes),

            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }

    private static string PriorityText(TodoPriority priority)
    {
        return priority switch
        {
            TodoPriority.Urgent => "紧急",
            TodoPriority.High => "高优先级",
            TodoPriority.Low => "低优先级",
            _ => "中优先级"
        };
    }

    /// <summary>把分钟数说成人话：1440 → 提前 1 天。</summary>
    private static string ReminderText(int minutes)
    {
        if (minutes <= 0)
        {
            return "到点提醒";
        }

        if (minutes % 1440 == 0)
        {
            return $"提前 {minutes / 1440} 天";
        }

        if (minutes % 60 == 0)
        {
            return $"提前 {minutes / 60} 小时";
        }

        return $"提前 {minutes} 分钟";
    }
}

/// <summary>筛选 / 排序枚举 → 中文。</summary>
public sealed class FilterLabelConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value switch
        {
            ViewModels.TaskStatusFilter f => f switch
            {
                ViewModels.TaskStatusFilter.NotStarted => "待办",
                ViewModels.TaskStatusFilter.InProgress => "进行中",
                ViewModels.TaskStatusFilter.Completed => "已完成",
                _ => "全部状态"
            },

            ViewModels.SortOption s => s switch
            {
                ViewModels.SortOption.Priority => "按优先级",
                ViewModels.SortOption.CreatedTime => "按创建时间",
                ViewModels.SortOption.Title => "按标题",
                _ => "按截止时间"
            },

            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>整数计数 → 布尔。ConverterParameter=invert 时取反（空状态提示用）。</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var hasItems = value is int count && count > 0;

        return parameter is "invert" ? !hasItems : hasItems;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>非空字符串 → true。</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// 优先级 → 颜色。紧急红、高粉、中蓝、低绿。
/// 色值比 App.axaml 里的装饰色更深一档 —— 这里是当文字色用的，太浅会看不清。
/// </summary>
public sealed class PriorityBrushConverter : IValueConverter
{
    private static readonly IBrush Urgent =
        new SolidColorBrush(Color.Parse("#D9435F"));

    private static readonly IBrush High =
        new SolidColorBrush(Color.Parse("#D9738E"));

    private static readonly IBrush Medium =
        new SolidColorBrush(Color.Parse("#4E9DB8"));

    private static readonly IBrush Low =
        new SolidColorBrush(Color.Parse("#5E9E85"));

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value switch
        {
            TodoPriority.Urgent => Urgent,
            TodoPriority.High => High,
            TodoPriority.Low => Low,
            _ => Medium
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>"#ACE2F1" 这类字符串 → 画刷。清单自定义色用。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    // 同一批清单每次重算都新建画刷太浪费，缓存一下
    private static readonly Dictionary<string, IBrush> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly IBrush Fallback =
        new SolidColorBrush(Color.Parse("#ACE2F1"));

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var hex = value as string;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return Fallback;
        }

        if (Cache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        IBrush brush;

        try
        {
            brush = new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            // 清单颜色是用户可改的，脏数据不能让界面崩掉
            brush = Fallback;
        }

        Cache[hex] = brush;

        return brush;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>习惯打卡格：已完成 → 主色，未完成 → 浅灰。ConverterParameter=border 取描边色。</summary>
public sealed class HabitBrushConverter : IValueConverter
{
    private static readonly IBrush DoneFill =
        new SolidColorBrush(Color.Parse("#ACE2F1"));

    private static readonly IBrush DoneBorder =
        new SolidColorBrush(Color.Parse("#7FC7DE"));

    private static readonly IBrush EmptyFill =
        new SolidColorBrush(Color.Parse("#F2F7F9"));

    private static readonly IBrush EmptyBorder =
        new SolidColorBrush(Color.Parse("#E1EDF2"));

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var done = value is true;

        var wantBorder = parameter is "border";

        return (done, wantBorder) switch
        {
            (true, false) => DoneFill,
            (true, true) => DoneBorder,
            (false, true) => EmptyBorder,
            _ => EmptyFill
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

