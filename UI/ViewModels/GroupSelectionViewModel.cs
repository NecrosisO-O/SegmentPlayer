using System.Collections.ObjectModel;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Core;
using PortablePlayer.Domain.Models;

namespace PortablePlayer.UI.ViewModels;

public sealed class GroupSelectionViewModel : ObservableObject
{
    private readonly IGroupScanner _groupScanner;
    private readonly ILocalizationService _localization;
    private GroupItemViewModel? _selectedGroup;
    private bool _isLoading;
    private string _statusText = string.Empty;

    public GroupSelectionViewModel(IGroupScanner groupScanner, ILocalizationService localization)
    {
        _groupScanner = groupScanner;
        _localization = localization;
        Groups = [];
        ReloadCommand = new AsyncRelayCommand(LoadGroupsAsync);
        PlaySelectedCommand = new AsyncRelayCommand(
            async () =>
            {
                if (SelectedGroup is null || PlayRequested is null)
                {
                    return;
                }

                await PlayRequested.Invoke(SelectedGroup.Descriptor);
            },
            () => SelectedGroup?.IsValid == true);

        EditSelectedCommand = new AsyncRelayCommand(
            async () =>
            {
                if (SelectedGroup is null || EditRequested is null)
                {
                    return;
                }

                await EditRequested.Invoke(SelectedGroup.Descriptor);
            },
            () => SelectedGroup is not null);

        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());

    }

    public ObservableCollection<GroupItemViewModel> Groups { get; }

    public GroupItemViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string TitleText => _localization.Get("group.title");

    public string RefreshText => _localization.Get("btn.refresh");

    public string EditText => _localization.Get("btn.editPlaylist");

    public string SettingsText => _localization.Get("btn.settings");

    public string PlayText => _localization.Get("btn.play");

    public AsyncRelayCommand ReloadCommand { get; }

    public AsyncRelayCommand PlaySelectedCommand { get; }

    public AsyncRelayCommand EditSelectedCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public Func<GroupDescriptor, Task>? PlayRequested { get; set; }

    public Func<GroupDescriptor, Task>? EditRequested { get; set; }

    public Action? SettingsRequested { get; set; }

    public async Task LoadGroupsAsync()
    {
        IsLoading = true;
        try
        {
            var scanned = await _groupScanner.ScanAsync().ConfigureAwait(true);
            Groups.Clear();
            foreach (var group in scanned)
            {
                var issueText = group.IsValid
                    ? _localization.Get("group.status.valid")
                    : string.Join(" | ", group.Issues.Select(issue => issue.Message));

                Groups.Add(new GroupItemViewModel
                {
                    Descriptor = group,
                    Status = issueText,
                });
            }

            SelectedGroup = Groups.FirstOrDefault();
            StatusText = string.Format(_localization.Get("group.status.count"), Groups.Count);
        }
        finally
        {
            IsLoading = false;
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        PlaySelectedCommand.NotifyCanExecuteChanged();
        EditSelectedCommand.NotifyCanExecuteChanged();
    }
}
