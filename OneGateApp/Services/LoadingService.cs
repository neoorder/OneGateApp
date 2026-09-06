using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NeoOrder.OneGate.Services;

public partial class LoadingService(params Func<Task>[] loadActions) : ICommand, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CanExecuteChanged;
    public event EventHandler? Loaded;

    public bool IsLoading { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsReloading { get; private set { field = value; OnPropertyChanged(); } }
    public bool HasError { get; private set { field = value; OnPropertyChanged(); } }

    void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async void BeginLoad(bool reload = false)
    {
        if (IsLoading) return;
        HasError = false;
        IsLoading = true;
        if (reload) IsReloading = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await Task.WhenAll(loadActions.Select(p => p()));
            Loaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HasError = true;
            await Toast.Show(ex.Message);
        }
        IsLoading = false;
        if (reload) IsReloading = false;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    bool ICommand.CanExecute(object? parameter)
    {
        return !IsLoading;
    }

    void ICommand.Execute(object? parameter)
    {
        BeginLoad(true);
    }
}
