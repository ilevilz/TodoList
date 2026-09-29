using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LMTodo.ViewModels;
using LMTodo.Views;
using LMTodo.Repositories;

namespace LMTodo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ViewModel 必须在窗口加载 XAML 之前注入，
            // 否则所有绑定会以 null 解析一遍（界面空白且不报错）。
            desktop.MainWindow =
                new MainWindow(new MainWindowViewModel());
        }

        base.OnFrameworkInitializationCompleted();
    }
}