using System;

namespace LMTodo.Models;

/// <summary>
/// 子任务 / 步骤。
/// </summary>
public sealed class SubTask : ObservableModel
{
    private string _title = string.Empty;
    private bool _isCompleted;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetField(ref _isCompleted, value);
    }
}
