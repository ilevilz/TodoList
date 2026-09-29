using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LMTodo.Models;

/// <summary>
/// 所有可序列化模型的公共基类。
///
/// 只做一件事：提供 INotifyPropertyChanged 的最小实现，
/// 避免每个模型都重复抄一遍 SetField。
/// </summary>
public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 一次性通知多个属性（派生属性通常要一起刷新）。
    /// 刻意不叫 OnPropertyChanged，避免与上面的单参数重载产生歧义。
    /// </summary>
    protected void OnPropertiesChanged(
        params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            OnPropertyChanged(name);
        }
    }

    protected bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;

        OnPropertyChanged(propertyName);

        return true;
    }
}
