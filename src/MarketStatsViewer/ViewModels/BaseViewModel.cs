using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MarketStatsViewer.ViewModels;

/// <summary>
/// Базовый класс для ViewModel, реализующий интерфейс INotifyPropertyChanged для реактивного связывания данных с UI в WPF.
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Событие уведомления об изменении свойства модели.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Вызывает событие PropertyChanged для указанного свойства.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Вспомогательный метод установки значения поля с автоматической проверкой на равенство и вызовом события.
    /// </summary>
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Реализация команды ICommand для привязки действий кнопок и элементов управления из XAML.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    /// <summary>
    /// Создает команду с делегатом действия и опциональным условием выполнения.
    /// </summary>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// Создает простую команду без параметров.
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    /// <summary>
    /// Событие запроса на перепроверку доступности команды.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Проверяет, может ли команда выполниться в текущем состоянии.
    /// </summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// Выполняет действие команды.
    /// </summary>
    public void Execute(object? parameter) => _execute(parameter);
}
