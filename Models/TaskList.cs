using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace LMTodo.Models;

/// <summary>
/// 清单 / 项目。
///
/// 支持共享：Owner + Members 为后续协作预留，
/// 本地模式下 Members 通常为空。
/// </summary>
public sealed class TaskList : ObservableModel
{
    private string _name = string.Empty;
    private string _color = "#ACE2F1";
    private string _owner = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>清单主色，用于左侧色条。</summary>
    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    /// <summary>共享清单的所有者。</summary>
    public string Owner
    {
        get => _owner;
        set
        {
            if (SetField(ref _owner, value))
            {
                OnPropertyChanged(nameof(IsShared));
            }
        }
    }

    /// <summary>共享成员列表。</summary>
    public ObservableCollection<string> Members { get; set; } = [];

    [JsonIgnore]
    public bool IsShared =>
        !string.IsNullOrWhiteSpace(Owner) ||
        Members.Count > 0;

    [JsonIgnore]
    public string SharingLabel =>
        !IsShared
            ? "仅自己"
            : Members.Count == 0
                ? $"共享 · {Owner}"
                : $"共享 · {Members.Count} 人";

    public void RefreshLabels()
    {
        OnPropertiesChanged(
            nameof(IsShared),
            nameof(SharingLabel));
    }
}
