using PortablePlayer.Core;

namespace PortablePlayer.UI.ViewModels;

public sealed class PlaylistEditorItemViewModel : ObservableObject
{
    private string _loopsText = "1";
    private string _groupText = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public bool Missing { get; init; }

    public string LoopsText
    {
        get => _loopsText;
        set => SetProperty(ref _loopsText, value);
    }

    public string GroupText
    {
        get => _groupText;
        set => SetProperty(ref _groupText, value);
    }
}
