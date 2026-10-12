using BranchGrouper.BranchGroupsWindow;
using Microsoft.IO;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.PlatformUI;
using System.ComponentModel.Design;
using System.Runtime.Serialization;

namespace BranchGrouper;

[DataContract]
internal class RepoItem : NotifyPropertyChangedObject
{
    public RepoItem(
       string branchName,
       string name,
       bool isActive,
       string path,
       Action<object> select,
       Func<RepoItem, CancellationToken, Task> checkout,
       Func<RepoItem, CancellationToken, Task> openFolder,
       Func<RepoItem, CancellationToken, Task> copyPath)
    {
        BranchName = branchName;
        Name = name;
        IsActive = isActive;
        Path = path;

        SelectCommand = new((_, _) =>
        {
            select(this);
            return Task.CompletedTask;
        });

        CheckoutCommand = new((_, ct) => checkout(this, ct));
        OpenFolderCommand = new((_, ct) => openFolder(this, ct));
        CopyPathCommand = new((_, ct) => copyPath(this, ct));
    }

    [DataMember]
    public string BranchName { get; }
    [DataMember] public string Name { get; }
    [DataMember] public bool IsActive { get; }
    [DataMember] public string Path { get; }

    private bool _isSelected;

    [DataMember]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    [DataMember] public AsyncCommand SelectCommand { get; }
    [DataMember] public AsyncCommand CheckoutCommand { get; }
    [DataMember] public AsyncCommand OpenFolderCommand { get; }
    [DataMember] public AsyncCommand CopyPathCommand { get; }
}

[DataContract]
internal class BranchGroupItem : NotifyPropertyChangedObject
{
    private static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
    {
        {"feature", "#569CD6"},
        {"bug", "#CE9178" },
        {"dev", "#DCDCAA" },
        {"main", "#B5CEA8" }
    };

    //{ "#4FC1FF", "#C586C0", "#DCDCAA", "#4EC9B0", "#CE9178", "#B5CEA8", "#569CD6", "#D7BA7D" };
    private readonly Action<object> _select;
    public BranchGroupItem(
        string name,
        List<RepoItem> repos,
        Func<BranchGroupItem, CancellationToken, Task> checkout,
        Func<BranchGroupItem, CancellationToken, Task> pull,
        Func<BranchGroupItem, CancellationToken, Task> copyBranchName,
        Func<BranchGroupItem, CancellationToken, Task> delete,
        Action<object> select,
        bool isRemote)
    {
        Name = name;
        Repos = repos;
        Count = $"{repos.Count} repos";

        var key = Palette.Keys.FirstOrDefault(k =>
            name.Contains(k));

        Accent = key != null
            ? Palette[key]
            : "#e3dccc";

        IsActive = repos.All(r => r.IsActive);

        CheckoutCommand = new((_, ct) => checkout(this, ct));
        PullCommand = new((_, ct) => pull(this, ct));
        CopyBranchNameCommand = new((_, ct) => copyBranchName(this, ct));
        DeleteCommand = new((_, ct) => delete(this, ct));

        SelectCommand = new((_, _) =>
        {
            select(this);
            return Task.CompletedTask;
        });

        _select = select;

        ToggleCommand = new((_, _) =>
        {
            IsExpanded = !IsExpanded;
            return Task.CompletedTask;
        });
        IsRemote = isRemote;
    }


    [DataMember] public string Name { get; }
    [DataMember] public string Count { get; }
    [DataMember] public string Accent { get; }
    [DataMember] public bool IsActive { get; }
    [DataMember] public List<RepoItem> Repos { get; }
    [DataMember] public AsyncCommand CheckoutCommand { get; }
    [DataMember] public AsyncCommand PullCommand { get; }
    [DataMember] public AsyncCommand CopyBranchNameCommand { get; }
    [DataMember] public AsyncCommand DeleteCommand { get; }
    private bool _isSelected;
    [DataMember] public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    [DataMember] public AsyncCommand SelectCommand { get; }

    private bool _matches = true;
    [DataMember] public bool Matches { get => _matches; set => SetProperty(ref _matches, value); }
    private bool _isExpanded;
    [DataMember]
    public bool IsExpanded
    {
        get => _isExpanded;
        set { SetProperty(ref _isExpanded, value); _select(this); }
    }
    [DataMember] public AsyncCommand ToggleCommand { get; }
    public bool IsRemote { get; set; }
    [DataMember] public bool CanModify => !IsRemote;
    [DataMember] public string BranchIcon => IsRemote ? "KnownMonikers.BranchRemote" : "KnownMonikers.Branch";
}

[DataContract]
internal class BranchGroupsData : NotifyPropertyChangedObject
{
    public BranchGroupsData(Func<CancellationToken, Task> refresh, Func<bool, CancellationToken, Task> switchMode)
    {
        RefreshCommand = new((_, ct) =>
        {
            Filter = "";
            Groups.Clear();
            return refresh(ct); 
        });

        ShowLocalCommand = new((_, ct) =>
        {
            Filter = "";
            Groups.Clear();
            return switchMode(false, ct);
        });
        ShowRemoteCommand = new((_, ct) =>
        {
            Filter = "";
            Groups.Clear();
            return switchMode(true, ct);
        });
    }

    private object? _selected;

    public void Select(object item)
    {
        if (_selected is BranchGroupItem g) g.IsSelected = false;
        else if (_selected is RepoItem r) r.IsSelected = false;
        _selected = item;
        if (item is BranchGroupItem g2) g2.IsSelected = true;
        else if (item is RepoItem r2) r2.IsSelected = true;
    }

    [DataMember] public ObservableList<BranchGroupItem> Groups { get; } = new();
    [DataMember] public AsyncCommand RefreshCommand { get; }

    private string _filter = "";
    [DataMember]
    public string Filter
    {
        get => _filter;
        set { SetProperty(ref _filter, value); ApplyFilter(); }
    }

    public void SetGroups(IEnumerable<BranchGroupItem> groups)
    {
        _selected = null;
        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = _filter.Trim();
        foreach (var g in Groups)
            g.Matches = f.Length == 0 || g.Name.ToLower().Contains(f.ToLower());
    }

    private bool _isRemote;
    [DataMember]
    public bool IsRemote
    {
        get => _isRemote;
        set { SetProperty(ref _isRemote, value); RaiseNotifyPropertyChangedEvent(nameof(IsLocal)); }
    }
    [DataMember] public bool IsLocal => !_isRemote;

    [DataMember] public AsyncCommand ShowLocalCommand { get; }
    [DataMember] public AsyncCommand ShowRemoteCommand { get; }
}