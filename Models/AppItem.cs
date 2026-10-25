using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Apathy.Models;

public enum InstallState { None, Waiting, Working, Done, AlreadyInstalled, Saved, Opened, Failed }

/// <summary>One checkbox in the menu, and its row in the "Selected programs" list.</summary>
public sealed class AppItem : INotifyPropertyChanged
{
    private bool _isChecked;
    private InstallState _state;
    private string? _version;

    public AppItem(Program program) => Program = program;

    public Program Program { get; }
    public string Name => Program.Name;

    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked != value) { _isChecked = value; OnChanged(); } }
    }

    public InstallState State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnChanged(); OnChanged(nameof(StateText)); } }
    }

    public string StateText => State switch
    {
        InstallState.Waiting => "waiting",
        InstallState.Working => "working...",
        InstallState.Done => "OK",
        InstallState.AlreadyInstalled => "already there",
        InstallState.Saved => "saved",
        InstallState.Opened => "opened in browser",
        InstallState.Failed => "failed",
        _ => "",
    };

    /// <summary>winget version to install; null means the latest.</summary>
    public string? Version
    {
        get => _version;
        set { if (_version != value) { _version = value; OnChanged(); OnChanged(nameof(VersionText)); } }
    }

    public string VersionText => (Version ?? "latest") + " ▾";

    /// <summary>Only winget packages have a list of versions to pick from.</summary>
    public bool CanPickVersion => !Program.IsDirect && string.IsNullOrEmpty(Program.Url);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
