using System;
using System.Collections.Generic;
using LMTodo.Models;

namespace LMTodo.Repositories;

/// <summary>磁盘上的完整数据快照。</summary>
public sealed class TodoStore
{
    /// <summary>存储格式版本，用于以后做数据迁移。</summary>
    public int Version { get; set; } = 2;

    public List<TodoItem> Tasks { get; set; } = [];

    public List<TaskList> Lists { get; set; } = [];

    public DateTime SavedAt { get; set; } = DateTime.Now;
}

public interface ITodoRepository
{
    string FilePath { get; }

    /// <summary>最近一次读写的错误信息，正常时为 null。</summary>
    string? LastError { get; }

    TodoStore Load();

    void Save(TodoStore store);
}
