using System.Collections.ObjectModel;
using System.Globalization;
using PortablePlayer.Application.Interfaces;
using PortablePlayer.Core;
using PortablePlayer.Domain.Enums;
using PortablePlayer.Domain.Models;
using PortablePlayer.Infrastructure.Services;

namespace PortablePlayer.UI.ViewModels;

public sealed class PlaylistEditorViewModel : ObservableObject
{
    private readonly GroupDescriptor _descriptor;
    private readonly IPlaylistService _playlistService;
    private readonly ILocalizationService _localizationService;
    private readonly MediaType _mediaType;
    private PlaylistEditorItemViewModel? _selectedItem;
    private string _statusText = string.Empty;

    public PlaylistEditorViewModel(
        GroupDescriptor descriptor,
        PlaylistDocument playlist,
        IPlaylistService playlistService,
        ILocalizationService localizationService)
    {
        _descriptor = descriptor;
        _playlistService = playlistService;
        _localizationService = localizationService;
        _mediaType = descriptor.MediaType;

        GroupName = descriptor.Name;
        Items = [];
        foreach (var item in playlist.Items)
        {
            Items.Add(new PlaylistEditorItemViewModel
            {
                FileName = item.File,
                LoopsText = item.Loops.ToString(CultureInfo.InvariantCulture),
                GroupText = item.Group?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Missing = item.Missing || !File.Exists(Path.Combine(descriptor.FullPath, item.File)),
            });
        }

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        NormalizeGroupsCommand = new RelayCommand(NormalizeGroups);
        RemoveMissingCommand = new RelayCommand(RemoveMissingItems);

        WindowTitle = _localizationService.Get("editor.title");
        NormalizeText = _localizationService.Get("editor.normalize");
        RemoveMissingText = _localizationService.Get("editor.removeMissing");
        CancelText = _localizationService.Get("btn.cancel");
        SaveText = _localizationService.Get("btn.save");
    }

    public string GroupName { get; }

    public string WindowTitle { get; }

    public string NormalizeText { get; }

    public string RemoveMissingText { get; }

    public string CancelText { get; }

    public string SaveText { get; }

    public ObservableCollection<PlaylistEditorItemViewModel> Items { get; }

    public PlaylistEditorItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand NormalizeGroupsCommand { get; }

    public RelayCommand RemoveMissingCommand { get; }

    public Action<bool>? RequestClose { get; set; }

    public void MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Items.Count || toIndex < 0 || toIndex >= Items.Count || fromIndex == toIndex)
        {
            return;
        }

        Items.Move(fromIndex, toIndex);
    }

    private void RemoveMissingItems()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Missing)
            {
                Items.RemoveAt(i);
            }
        }
    }

    private void NormalizeGroups()
    {
        var parsed = TryParseItems(out var records, out var error);
        if (!parsed || records is null)
        {
            StatusText = error ?? _localizationService.Get("editor.invalid");
            return;
        }

        var normalized = Normalize(records);
        ApplyNormalized(normalized);
        StatusText = _localizationService.Get("editor.normalized");
    }

    private async Task SaveAsync()
    {
        var parsed = TryParseItems(out var records, out var error);
        if (!parsed || records is null)
        {
            StatusText = error ?? _localizationService.Get("editor.invalid");
            return;
        }

        var normalized = Normalize(records);
        ApplyNormalized(normalized);

        var document = new PlaylistDocument
        {
            Version = 1,
            MediaType = MediaFileRules.ToPlaylistMediaType(_mediaType),
            Items = normalized.Select(item => new PlaylistItem
            {
                File = item.FileName,
                Loops = item.Loops,
                Group = item.Group,
                Missing = item.Missing,
            }).ToList(),
        };

        await _playlistService.SaveAsync(_descriptor.PlaylistPath, document).ConfigureAwait(true);
        StatusText = _localizationService.Get("editor.saved");
        RequestClose?.Invoke(true);
    }

    private bool TryParseItems(out List<EditorRecord>? records, out string? error)
    {
        records = new List<EditorRecord>();
        for (var i = 0; i < Items.Count; i++)
        {
            var row = Items[i];
            if (!TryParseDouble(row.LoopsText, out var loops))
            {
                error = $"Invalid loops at line {i + 1}.";
                records = null;
                return false;
            }

            if (!ValidateLoops(loops))
            {
                error = $"Loops out of range at line {i + 1}.";
                records = null;
                return false;
            }

            int? group = null;
            if (!string.IsNullOrWhiteSpace(row.GroupText))
            {
                if (!int.TryParse(row.GroupText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGroup) || parsedGroup <= 0)
                {
                    error = $"Invalid group at line {i + 1}.";
                    records = null;
                    return false;
                }

                group = parsedGroup;
            }

            records.Add(new EditorRecord
            {
                FileName = row.FileName,
                Loops = loops,
                Group = group,
                Missing = row.Missing,
            });
        }

        error = null;
        return true;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private bool ValidateLoops(double loops)
    {
        if (_mediaType == MediaType.Video)
        {
            if (loops == -1 || loops == 0)
            {
                return true;
            }

            if (loops < 1)
            {
                return false;
            }

            return Math.Abs(loops - Math.Round(loops)) < 0.0001;
        }

        if (loops == -1 || loops == 0)
        {
            return true;
        }

        if (loops < 0.1)
        {
            return false;
        }

        return Math.Abs(loops * 10 - Math.Round(loops * 10)) < 0.0001;
    }

    private static List<EditorRecord> Normalize(List<EditorRecord> source)
    {
        var buckets = new Dictionary<int, List<EditorRecord>>();
        foreach (var item in source.Where(item => item.Group is not null))
        {
            var key = item.Group!.Value;
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(item);
        }

        var emitted = new HashSet<int>();
        var reordered = new List<EditorRecord>();
        foreach (var item in source)
        {
            if (item.Group is null)
            {
                reordered.Add(item);
                continue;
            }

            var key = item.Group.Value;
            if (emitted.Contains(key))
            {
                continue;
            }

            reordered.AddRange(buckets[key]);
            emitted.Add(key);
        }

        var renumber = new Dictionary<int, int>();
        var nextGroup = 1;
        foreach (var item in reordered)
        {
            if (item.Group is null)
            {
                continue;
            }

            var key = item.Group.Value;
            if (!renumber.TryGetValue(key, out var mapped))
            {
                mapped = nextGroup++;
                renumber[key] = mapped;
            }

            item.Group = mapped;
        }

        return reordered;
    }

    private void ApplyNormalized(List<EditorRecord> normalized)
    {
        Items.Clear();
        foreach (var record in normalized)
        {
            Items.Add(new PlaylistEditorItemViewModel
            {
                FileName = record.FileName,
                LoopsText = record.Loops.ToString(CultureInfo.InvariantCulture),
                GroupText = record.Group?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Missing = record.Missing,
            });
        }
    }

    private sealed class EditorRecord
    {
        public string FileName { get; set; } = string.Empty;

        public double Loops { get; set; }

        public int? Group { get; set; }

        public bool Missing { get; set; }
    }
}
