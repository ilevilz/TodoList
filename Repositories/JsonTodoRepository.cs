using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTodo.Models;

namespace LMTodo.Repositories;

/// <summary>
/// 本地 JSON 存储。
///
/// 特点：
/// 1. 原子写入（先写 .tmp 再替换），避免中途崩溃写坏文件；
/// 2. 覆盖前保留一份 .bak；
/// 3. 兼容旧版「裸数组」格式；
/// 4. 文件损坏时不静默清空，而是另存为 .corrupt 并把错误暴露出来。
/// </summary>
public sealed class JsonTodoRepository : ITodoRepository
{
    private readonly JsonSerializerOptions _options;

    public string FilePath { get; }

    public string? LastError { get; private set; }

    public JsonTodoRepository(string? directory = null)
    {
        var root = directory;

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "LMTodo");
        }

        Directory.CreateDirectory(root);

        FilePath = Path.Combine(root, "tasks.json");

        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            // 枚举写成名字，方便人工查看和迁移；
            // JsonStringEnumConverter 同时也能读回旧文件里的数字。
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };
    }

    public TodoStore Load()
    {
        LastError = null;

        if (!File.Exists(FilePath))
        {
            return new TodoStore();
        }

        string json;

        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex)
        {
            LastError = $"读取失败：{ex.Message}";
            return new TodoStore();
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return new TodoStore();
        }

        // 1) 新格式
        var store = TryDeserialize<TodoStore>(json);

        if (store is not null && store.Tasks.Count > 0)
        {
            Migration.FillMissingIds(store);
            return store;
        }

        // 2) 旧格式：顶层就是 TodoItem 数组
        var legacy = TryDeserialize<List<TodoItem>>(json);

        if (legacy is not null && legacy.Count > 0)
        {
            var migrated = new TodoStore { Version = 1 };

            migrated.Tasks.AddRange(legacy);
            Migration.FillMissingIds(migrated);

            return migrated;
        }

        // 3) 空的新格式（合法但没数据）
        if (store is not null)
        {
            return store;
        }

        // 4) 真的解析不了 —— 保底不再清空用户数据
        QuarantineCorruptFile(json);

        return new TodoStore();
    }

    public void Save(TodoStore store)
    {
        LastError = null;

        store.SavedAt = DateTime.Now;

        var tempPath = FilePath + ".tmp";
        var backupPath = FilePath + ".bak";

        var json = JsonSerializer.Serialize(store, _options);

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, backupPath, true);
            }

            File.Move(tempPath, FilePath, true);
        }
        catch (Exception ex)
        {
            LastError = $"保存失败：{ex.Message}";

            TryDelete(tempPath);

            throw;
        }
    }

    private T? TryDeserialize<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        catch
        {
            return null;
        }
    }

    private void QuarantineCorruptFile(string json)
    {
        try
        {
            var target =
                FilePath +
                $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";

            File.WriteAllText(target, json);

            LastError =
                $"数据文件无法解析，已另存为 {Path.GetFileName(target)}，本次以空数据启动。";
        }
        catch (Exception ex)
        {
            LastError = $"数据文件无法解析，且备份失败：{ex.Message}";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败无所谓，下次保存会覆盖
        }
    }
}

/// <summary>一次性数据补齐，处理旧版本缺失的字段。</summary>
internal static class Migration
{
    public static void FillMissingIds(TodoStore store)
    {
        foreach (var task in store.Tasks)
        {
            if (task.Id == Guid.Empty)
            {
                task.Id = Guid.NewGuid();
            }

            if (task.SubTasks is null)
            {
                continue;
            }

            foreach (var sub in task.SubTasks.Where(x => x.Id == Guid.Empty))
            {
                sub.Id = Guid.NewGuid();
            }
        }

        store.Lists ??= [];

        foreach (var list in store.Lists.Where(x => x.Id == Guid.Empty))
        {
            list.Id = Guid.NewGuid();
        }
    }
}
