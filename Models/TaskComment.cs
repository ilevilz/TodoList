using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace LMTodo.Models;

/// <summary>
/// 任务评论（协作功能）。
///
/// 本轮不接服务端，但字段与 @提及 已经落地，
/// 目前只存本地，将来要做协作时可直接复用。
/// </summary>
public sealed class TaskComment : ObservableModel
{
    private string _text = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>评论作者。</summary>
    public string Author { get; set; } = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                OnPropertiesChanged(
                    nameof(Mentions),
                    nameof(MentionsLabel));
            }
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>解析出的 @某人 列表。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Mentions =>
        Regex
            .Matches(
                Text ?? string.Empty,
                @"@([\p{L}\p{N}_\-]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [JsonIgnore]
    public bool HasMentions =>
        Mentions.Count > 0;

    [JsonIgnore]
    public string MentionsLabel =>
        Mentions.Count == 0
            ? string.Empty
            : "提及 " +
              string.Join(
                  " ",
                  Mentions.Select(x => "@" + x));

    [JsonIgnore]
    public string CreatedAtLabel =>
        CreatedAt.Date == DateTime.Today
            ? $"今天 {CreatedAt:HH:mm}"
            : CreatedAt.Date == DateTime.Today.AddDays(-1)
                ? $"昨天 {CreatedAt:HH:mm}"
                : CreatedAt.ToString("yyyy-MM-dd HH:mm");

    public void RefreshLabels()
    {
        OnPropertiesChanged(
            nameof(Mentions),
            nameof(HasMentions),
            nameof(MentionsLabel),
            nameof(CreatedAtLabel));
    }
}
