using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AcLayerStandardizer.Core;
using Nodify;

namespace AcLayerStandardizer.UI;

// One toggle in the "Target Filter" panel -- mirrors the left-side legend's
// toggles, but the set of them is computed per-template by LayerCategorizer
// rather than being a fixed list.
public class TargetFilterViewModel(string name, string sortGroup, Action onChanged) : ObservableObject
{
    public string Name { get; } = name;
    public string SortGroup { get; } = sortGroup;

    // Tone-only grouping (chris: "a simple stack of buttons" -- no header
    // text/dividers) -- Discipline blue, General teal-gray, Specific stays
    // the original neutral gray so today's single-tier dictionaries look
    // unchanged. Bound via the existing ColorStringToBrushConverter, same
    // pattern as LayerConnectionViewModel.Stroke.
    public string GroupColor => SortGroup switch
    {
        "Discipline" => "#4a7dc4",
        "General" => "#4a8577",
        _ => "#666",
    };

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            onChanged();
        }
    }
}

public class LayerEditorViewModel : ObservableObject
{
    private const double NodeWidth = 280;
    private const double NodeHeight = 30;
    private const double NodeSpacing = 44;
    private const double TopMargin = 40;
    private const double LeftColX = 50;
    private const double RightColX = 550;
    private const double TargetColumnGap = 40;
    private const int MaxTargetColumns = 5;
    private const int MinTargetColumnHeight = 20;

    // Padding around a header "container" box relative to the bounding box
    // of the nodes it fences -- chris: "it sort of fences around the
    // visible nodes with a light box," not the small fixed label it was
    // before.
    private const double HeaderPadding = 24;
    private const double HeaderTitleBarHeight = 64;

    // Height of the live text-filter box docked directly under the title
    // bar (Alpha 5) -- included in the header's total top-strip height so
    // ComputeContainerBounds fences the actual visible content below it,
    // not just below the title bar.
    private const double HeaderFilterBoxHeight = 32;
    private const double HeaderTopStripHeight = HeaderTitleBarHeight + HeaderFilterBoxHeight;

    public ObservableCollection<LayerNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<LayerConnectionViewModel> Connections { get; } = [];
    public ObservableCollection<TargetFilterViewModel> TargetFilters { get; } = [];

    public PendingConnectionViewModel PendingConnection { get; }

    public ICommand RemoveConnectionCommand { get; }
    public ICommand DisconnectConnectorCommand { get; }
    public ICommand ToggleFilterCommand { get; }
    public ICommand ToggleAllTargetFiltersCommand { get; }

    private ICommand _purgeUnusedCommand = new DelegateCommand<object>(_ => { });
    public ICommand PurgeUnusedCommand
    {
        get => _purgeUnusedCommand;
        set => SetProperty(ref _purgeUnusedCommand, value);
    }

    private bool _isExactNameVisible = true;
    public bool IsExactNameVisible
    {
        get => _isExactNameVisible;
        set
        {
            if (_isExactNameVisible == value) return;
            _isExactNameVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isMemoryMatchVisible = true;
    public bool IsMemoryMatchVisible
    {
        get => _isMemoryMatchVisible;
        set
        {
            if (_isMemoryMatchVisible == value) return;
            _isMemoryMatchVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isHeuristicMatchVisible = true;
    public bool IsHeuristicMatchVisible
    {
        get => _isHeuristicMatchVisible;
        set
        {
            if (_isHeuristicMatchVisible == value) return;
            _isHeuristicMatchVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isManualMatchVisible = true;
    public bool IsManualMatchVisible
    {
        get => _isManualMatchVisible;
        set
        {
            if (_isManualMatchVisible == value) return;
            _isManualMatchVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isUnmatchedVisible = true;
    public bool IsUnmatchedVisible
    {
        get => _isUnmatchedVisible;
        set
        {
            if (_isUnmatchedVisible == value) return;
            _isUnmatchedVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    // Live text filter (chris, Alpha 5 "big move"): typing in the box under
    // the Source/Target frame headers narrows visible nodes to those whose
    // name contains the typed string. Debounced rather than reapplying
    // ApplyFilters on every keystroke -- redraw only once typing pauses, and
    // grey out the toggle-button filters meanwhile (AreFilterButtonsEnabled)
    // since the text filter and the toggle filters both drive the same
    // IsVisible computation and mixing "still typing" state with a redraw
    // mid-keystroke would be confusing.
    private readonly DispatcherTimer _textFilterDebounce;
    private bool _isTextFiltering;
    public bool AreFilterButtonsEnabled => !_isTextFiltering;

    private void OnTextFilterChanged()
    {
        if (!_isTextFiltering)
        {
            _isTextFiltering = true;
            OnPropertyChanged(nameof(AreFilterButtonsEnabled));
        }
        _textFilterDebounce.Stop();
        _textFilterDebounce.Start();
    }

    // While a text filter is active, it should surface every name match
    // regardless of which toggle buttons are on/off (chris, 2026-07-12) --
    // so typing snaps every relevant toggle to "on" (and greys them out via
    // AreFilterButtonsEnabled) for the duration, then restores exactly the
    // state they were in before typing started once the box is cleared back
    // to empty. Snapshotting only on the empty-to-non-empty transition (not
    // every keystroke) is what makes "restore" mean "before this search",
    // not "one keystroke ago".
    private bool[]? _sourceToggleSnapshot;
    private Dictionary<TargetFilterViewModel, bool>? _targetToggleSnapshot;

    private string _sourceFilterText = "";
    public string SourceFilterText
    {
        get => _sourceFilterText;
        set
        {
            if (_sourceFilterText == value) return;
            bool wasEmpty = string.IsNullOrEmpty(_sourceFilterText);
            bool isEmpty = string.IsNullOrEmpty(value);
            _sourceFilterText = value;
            OnPropertyChanged();

            if (wasEmpty && !isEmpty)
            {
                _sourceToggleSnapshot =
                [
                    IsExactNameVisible, IsMemoryMatchVisible, IsHeuristicMatchVisible,
                    IsManualMatchVisible, IsUnmatchedVisible,
                ];
                IsExactNameVisible = true;
                IsMemoryMatchVisible = true;
                IsHeuristicMatchVisible = true;
                IsManualMatchVisible = true;
                IsUnmatchedVisible = true;
            }
            else if (!wasEmpty && isEmpty && _sourceToggleSnapshot is { Length: 5 } snap)
            {
                IsExactNameVisible = snap[0];
                IsMemoryMatchVisible = snap[1];
                IsHeuristicMatchVisible = snap[2];
                IsManualMatchVisible = snap[3];
                IsUnmatchedVisible = snap[4];
                _sourceToggleSnapshot = null;
            }

            OnTextFilterChanged();
        }
    }

    private string _targetFilterText = "";
    public string TargetFilterText
    {
        get => _targetFilterText;
        set
        {
            if (_targetFilterText == value) return;
            bool wasEmpty = string.IsNullOrEmpty(_targetFilterText);
            bool isEmpty = string.IsNullOrEmpty(value);
            _targetFilterText = value;
            OnPropertyChanged();

            if (wasEmpty && !isEmpty)
            {
                _targetToggleSnapshot = TargetFilters.ToDictionary(f => f, f => f.IsChecked);
                foreach (var f in TargetFilters)
                    f.IsChecked = true;
            }
            else if (!wasEmpty && isEmpty && _targetToggleSnapshot is { } snap)
            {
                foreach (var f in TargetFilters)
                    if (snap.TryGetValue(f, out var was))
                        f.IsChecked = was;
                _targetToggleSnapshot = null;
            }

            OnTextFilterChanged();
        }
    }

    // string.Contains(string, StringComparison) isn't available on net48
    // (this project still targets it -- see AGENTS.md), hence IndexOf here.
    private static bool MatchesTextFilter(string name, string filter) =>
        string.IsNullOrWhiteSpace(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    // Global UI preference, not a per-mapping setting like the others below
    // -- gates NodeGraphWindow.StartLocationAnimation (the node-position
    // tween) and persists via UserPreferences.AnimationsEnabled, seeded from
    // there by the window at construction (chris asked for an on/off switch
    // 2026-07-12 but wasn't sure where it belonged; parked here in the
    // Legend panel's own "Display" row since there's no other settings
    // surface in this window yet).
    private bool _areAnimationsEnabled = true;
    public bool AreAnimationsEnabled
    {
        get => _areAnimationsEnabled;
        set
        {
            if (_areAnimationsEnabled == value) return;
            _areAnimationsEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isMatchColorEnabled = true;
    public bool IsMatchColorEnabled
    {
        get => _isMatchColorEnabled;
        set
        {
            if (_isMatchColorEnabled == value) return;
            _isMatchColorEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isMatchLinetypeEnabled = true;
    public bool IsMatchLinetypeEnabled
    {
        get => _isMatchLinetypeEnabled;
        set
        {
            if (_isMatchLinetypeEnabled == value) return;
            _isMatchLinetypeEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isMatchLineweightEnabled = true;
    public bool IsMatchLineweightEnabled
    {
        get => _isMatchLineweightEnabled;
        set
        {
            if (_isMatchLineweightEnabled == value) return;
            _isMatchLineweightEnabled = value;
            OnPropertyChanged();
        }
    }

    // Default off (chris, 2026-07-12): unlike Match Color/Linetype/Lineweight
    // (which only sync the LAYER's own default properties), this rewrites
    // every element's own per-entity overrides to ByLayer -- a much more
    // invasive, one-way operation, so it shouldn't be on by default.
    private bool _isMakeByLayerEnabled;
    public bool IsMakeByLayerEnabled
    {
        get => _isMakeByLayerEnabled;
        set
        {
            if (_isMakeByLayerEnabled == value) return;
            _isMakeByLayerEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isEmptyHighlighted;
    public bool IsEmptyHighlighted
    {
        get => _isEmptyHighlighted;
        set
        {
            if (_isEmptyHighlighted == value) return;
            _isEmptyHighlighted = value;
            OnPropertyChanged();
            UpdateNodeColors();
        }
    }

    public IReadOnlyDictionary<string, string> CurrentMappings
    {
        get
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Connections)
                dict[c.Source.Name] = c.Target.Name;
            return dict;
        }
    }

    // Every source layer name loaded into this session, matched or not --
    // callers need this (not just CurrentMappings) to know which memory
    // entries this session actually has an opinion about, so unrelated
    // entries from other drawings aren't touched on save.
    public IReadOnlyList<string> SourceLayerNames =>
        Nodes.Where(n => n.IsSource && !n.IsHeader).Select(n => n.Name).ToList();

    // Canvas-space labels (chris: "underlays below the nodes in space," not
    // fixed screen chrome) -- created once, then just have their Subtitle
    // pushed on file-name changes rather than being torn down/rebuilt.
    private LayerNodeViewModel? _sourceHeader;
    private LayerNodeViewModel? _targetHeader;

    private string _sourceFileName = "";
    public string SourceFileName
    {
        get => _sourceFileName;
        set
        {
            SetProperty(ref _sourceFileName, value);
            if (_sourceHeader is not null) _sourceHeader.Subtitle = value;
        }
    }

    private string _targetFileName = "";
    public string TargetFileName
    {
        get => _targetFileName;
        set
        {
            SetProperty(ref _targetFileName, value);
            if (_targetHeader is not null) _targetHeader.Subtitle = value;
        }
    }

    public LayerEditorViewModel(
        List<string> sourceLayers,
        List<string> standardLayers,
        Dictionary<string, string>? memoryMappings = null,
        List<Matching.MatchResult>? heuristicResults = null,
        HashSet<string>? emptyLayers = null,
        string sourceFileName = "",
        string targetFileName = "")
    {
        _sourceFileName = sourceFileName;
        _targetFileName = targetFileName;
        var sourceModels = new List<LayerNodeViewModel>();
        var standardModels = new List<LayerNodeViewModel>();

        _textFilterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _textFilterDebounce.Tick += (_, _) =>
        {
            _textFilterDebounce.Stop();
            _isTextFiltering = false;
            OnPropertyChanged(nameof(AreFilterButtonsEnabled));
            ApplyFilters();
        };

        // Placeholder Location/Size -- UpdateHeaderBounds (called via
        // ApplyFilters at the end of this constructor) immediately
        // recomputes both to actually fence the visible nodes.
        _sourceHeader = new LayerNodeViewModel("Source", false, new Point(LeftColX, TopMargin))
        {
            IsHeader = true,
            Subtitle = sourceFileName,
        };
        _targetHeader = new LayerNodeViewModel("Target", false, new Point(RightColX, TopMargin))
        {
            IsHeader = true,
            Subtitle = targetFileName,
        };
        Nodes.Add(_sourceHeader);
        Nodes.Add(_targetHeader);

        for (int i = 0; i < sourceLayers.Count; i++)
        {
            var loc = new Point(LeftColX, TopMargin + i * NodeSpacing);
            var vm = new LayerNodeViewModel(sourceLayers[i], true, loc);
            sourceModels.Add(vm);
            Nodes.Add(vm);
        }

        for (int i = 0; i < standardLayers.Count; i++)
        {
            var vm = new LayerNodeViewModel(standardLayers[i], false, new Point(0, 0));
            standardModels.Add(vm);
            Nodes.Add(vm);
        }

        ApplyLayerDictionary(standardModels);

        var sourceMap = sourceModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        var standardMap = standardModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);

        RunMatchingTiers(sourceModels, sourceMap, standardMap, memoryMappings, heuristicResults);

        if (emptyLayers is not null)
        {
            foreach (var node in Nodes)
            {
                if (node.IsSource && emptyLayers.Contains(node.Name))
                    node.IsEmpty = true;
            }
        }

        PendingConnection = new PendingConnectionViewModel(this, sourceMap, standardMap);

        RemoveConnectionCommand = new DelegateCommand<LayerConnectionViewModel>(conn =>
        {
            if (conn is null) return;
            PushUndoSnapshot();
            conn.Source.IsMapped = false;
            if (!Connections.Any(c => c != conn && c.Target == conn.Target))
                conn.Target.IsMapped = false;
            Connections.Remove(conn);
        });

        // Pushes one undo snapshot per invocation, including calls Nodify
        // itself makes directly (e.g. its native drag-a-connector-off
        // gesture) rather than only the ones NodeGraphWindow's context menu
        // makes -- a multi-select "Un-match (N)" therefore undoes as N
        // separate Ctrl+Z steps rather than one, which is an acceptable
        // trade for guaranteeing every mutation path is covered.
        DisconnectConnectorCommand = new DelegateCommand<LayerNodeViewModel>(node =>
        {
            if (node is null || !Connections.Any(c => c.Source == node || c.Target == node)) return;
            PushUndoSnapshot();
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var c = Connections[i];
                if (c.Source == node || c.Target == node)
                {
                    c.Source.IsMapped = false;
                    if (!Connections.Any(other => other != c && other.Target == c.Target))
                        c.Target.IsMapped = false;
                    Connections.RemoveAt(i);
                }
            }
        });

        ToggleFilterCommand = new DelegateCommand<ConnectionMatchSource>(source =>
        {
            switch (source)
            {
                case ConnectionMatchSource.ExactName: IsExactNameVisible = !IsExactNameVisible; break;
                case ConnectionMatchSource.Memory: IsMemoryMatchVisible = !IsMemoryMatchVisible; break;
                case ConnectionMatchSource.Heuristic: IsHeuristicMatchVisible = !IsHeuristicMatchVisible; break;
                case ConnectionMatchSource.Manual: IsManualMatchVisible = !IsManualMatchVisible; break;
                case ConnectionMatchSource.Unmatched: IsUnmatchedVisible = !IsUnmatchedVisible; break;
            }
        });

        ToggleAllTargetFiltersCommand = new DelegateCommand<object>(_ =>
        {
            bool allOn = TargetFilters.All(f => f.IsChecked);
            foreach (var f in TargetFilters)
                f.IsChecked = !allOn;
        });

        // Covers every add/remove uniformly -- explicit connect (drag a new
        // mapping), RemoveConnectionCommand, DisconnectConnectorCommand, and
        // the initial construction below all funnel through here, so the
        // "front line" reflow (item 3) reacts to every matching-activity
        // change, not just Target Filter toggles.
        Connections.CollectionChanged += (_, _) =>
        {
            ApplyFilters();
        };

        ApplyFilters();
    }

    private void ApplyLayerDictionary(List<LayerNodeViewModel> standardModels)
    {
        var dict = LayerDictionaryDefinition.Load();
        if (dict.Categories.Count == 0) return; // no dictionary installed -- leave Target Filter empty, everything visible

        var result = LayerCategorizer.Classify(standardModels.Select(n => n.Name), dict);

        foreach (var node in standardModels)
        {
            if (result.AlwaysHidden.Contains(node.Name))
            {
                node.IsAlwaysHiddenTarget = true;
                continue;
            }
            if (result.LayerTags.TryGetValue(node.Name, out var tags))
            {
                foreach (var tag in tags)
                    node.TargetTags.Add(tag);
            }
        }

        // Primary tag = the node's most specific SPECIFIC-group tag, judged
        // by which of its Specific tags has the fewest members in THIS
        // template (population is a better specificity signal than any
        // hardcoded hierarchy). Scoped to Specific-group tags only --
        // Discipline/General tags are inclusive and handled separately in
        // IsTargetNodeVisible, so a node with zero Specific tags simply
        // isn't constrained by that group (PrimaryTargetTag stays null).
        // Ties break alphabetically for determinism.
        var specificTagCounts = standardModels
            .Where(n => !n.IsAlwaysHiddenTarget)
            .SelectMany(n => n.TargetTags.Where(t => result.SortGroupByTag.GetValueOrDefault(t, "Specific") == "Specific"))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var node in standardModels)
        {
            if (node.IsAlwaysHiddenTarget) continue;
            var specificTags = node.TargetTags
                .Where(t => result.SortGroupByTag.GetValueOrDefault(t, "Specific") == "Specific")
                .ToList();
            if (specificTags.Count == 0) continue;

            node.PrimaryTargetTag = specificTags
                .OrderBy(t => specificTagCounts.TryGetValue(t, out var c) ? c : int.MaxValue)
                .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        foreach (var category in result.VisibleCategories)
            TargetFilters.Add(new TargetFilterViewModel(category, result.SortGroupByTag.GetValueOrDefault(category, "Specific"), ApplyFilters));

        OnPropertyChanged(nameof(HasTargetFilters));
    }

    public bool HasTargetFilters => TargetFilters.Count > 0;

    // Fired at the top of every ApplyFilters pass, before any Location gets
    // reassigned -- NodeGraphWindow uses it to snap any still-running
    // node-position tween straight to its target first (see
    // NodeGraphWindow.SettleLocationAnimations). Without this, a filter
    // change (including the debounced text filter) landing WHILE a previous
    // pass's tween is still mid-flight lets RepositionVisibleNodes/
    // ArrangeTargetsInColumns compute the new layout off of a node's
    // currently-interpolated (not yet settled) position, and the two
    // animations racing/compounding could leave a node's Anchor stuck
    // between positions -- the reported "connections sometimes disappear or
    // don't draw for newly added mappings while typing in the filter, fixed
    // by toggling a filter again" bug. Toggling a filter "fixed" it only
    // because that happened to trigger one more full pass; this makes every
    // pass start from a clean, fully-settled state instead of relying on
    // that.
    public event Action? BeforeApplyFilters;

    private void ApplyFilters()
    {
        BeforeApplyFilters?.Invoke();

        foreach (var node in Nodes)
        {
            if (node.IsHeader)
            {
                node.IsVisible = true; // always shown, never filtered
                continue;
            }

            if (!node.IsSource)
            {
                node.IsVisible = IsTargetNodeVisible(node);
                continue;
            }

            var conn = Connections.FirstOrDefault(c => c.Source == node);
            ConnectionMatchSource matchType;

            if (conn is null)
                matchType = ConnectionMatchSource.Unmatched;
            else
                matchType = conn.MatchSource;

            node.IsVisible = matchType switch
            {
                ConnectionMatchSource.ExactName => IsExactNameVisible,
                ConnectionMatchSource.Memory => IsMemoryMatchVisible,
                ConnectionMatchSource.Heuristic => IsHeuristicMatchVisible,
                ConnectionMatchSource.Manual => IsManualMatchVisible,
                ConnectionMatchSource.Unmatched => IsUnmatchedVisible,
                _ => true,
            } && MatchesTextFilter(node.Name, SourceFilterText);
        }

        foreach (var conn in Connections)
        {
            // A connection needs both ends visible -- previously only the
            // source side was checked, so a connection whose target had been
            // hidden by the Target Filter stayed drawn to a node that wasn't
            // there anymore.
            conn.IsVisible = conn.Source.IsVisible && conn.Target.IsVisible;
        }

        RepositionVisibleNodes();
        ArrangeTargetsInColumns();
        UpdateHeaderBounds();
        UpdateNodeColors();
    }

    private bool IsTargetNodeVisible(LayerNodeViewModel node)
    {
        if (node.IsAlwaysHiddenTarget) return false;
        if (!MatchesTextFilter(node.Name, TargetFilterText)) return false;

        // No dictionary installed, or this layer got no tags at all (should
        // only happen if the dictionary was empty) -- default to visible
        // rather than silently hiding content.
        if (TargetFilters.Count == 0 || node.TargetTags.Count == 0) return true;

        // UNION semantics (chris, 2026-07-11, after live-testing the
        // AND-across-tiers version and finding it useless in practice:
        // "all off + Architectural on" showed ~5 layers instead of every
        // A- layer, because every node also had to pass its General and
        // Specific tiers). A checked toggle SHOWS every layer carrying its
        // tag; a node is visible if ANY checked filter covers it. The tiers
        // still differ in HOW they cover:
        //  - Discipline/General filters cover a node through plain tag
        //    membership (multi-membership is fine).
        //  - A Specific filter covers a node only when it IS the node's
        //    PrimaryTargetTag (its rarest Specific tag -- see
        //    ApplyLayerDictionary), preserving the one-toggle-per-node
        //    behavior within the Specific tier.
        // Consequence to know about: with everything ON, unchecking one
        // toggle only hides a node if NO other checked filter still covers
        // it -- the isolate-something workflow is All Off + check what you
        // want, which is exactly how chris uses it.
        foreach (var filter in TargetFilters)
        {
            if (!filter.IsChecked) continue;

            bool covers = filter.SortGroup == "Specific"
                ? string.Equals(filter.Name, node.PrimaryTargetTag, StringComparison.OrdinalIgnoreCase)
                : node.TargetTags.Contains(filter.Name);

            if (covers) return true;
        }
        return false;
    }

    // Source stays a single alphabetized column always (chris's explicit
    // call, 2026-07-10) -- explicitly sorted here rather than relying on
    // Nodes' insertion order (which happens to already be alphabetical
    // today since callers pre-sort, but that's an assumption worth not
    // depending on silently).
    private void RepositionVisibleNodes()
    {
        var visibleSources = Nodes.Where(n => n.IsSource && n.IsVisible).OrderBy(n => n.Name).ToList();

        for (int i = 0; i < visibleSources.Count; i++)
        {
            visibleSources[i].Location = new Point(LeftColX, TopMargin + i * NodeSpacing);
        }
    }

    private void UpdateNodeColors()
    {
        foreach (var node in Nodes)
        {
            if (node.IsHeader) continue; // fixed appearance, not match-driven

            if (node.IsSource)
            {
                var conn = Connections.FirstOrDefault(c => c.Source == node);
                node.BackgroundColor = conn switch
                {
                    { MatchSource: ConnectionMatchSource.ExactName } => "#8bc34a",
                    { MatchSource: ConnectionMatchSource.Memory } => "#448aff",
                    { MatchSource: ConnectionMatchSource.Heuristic } => "#ffd740",
                    { MatchSource: ConnectionMatchSource.Manual } => "#7c4dff",
                    _ => "#424242",
                };
                if (node.IsEmpty && IsEmptyHighlighted)
                    node.BackgroundColor = "#b71c1c";
            }
            else
            {
                bool inUse = Connections.Any(c => c.Target == node);
                node.BackgroundColor = inUse ? "#4682b4" : "#424242";
            }
        }
    }

    // Target is flexible/multi-column (chris's correction, 2026-07-10: the
    // "single column" call last round was a miscommunication -- Target will
    // always have far more items than Source, so it needs to flow into
    // several columns to keep panning reasonably proportioned).
    // Connected-first clustering (chris, 2026-07-10): targets with a
    // currently-visible connection are placed FIRST, so they land in the
    // column(s) nearest Source -- less scrolling/panning to drag a new
    // mapping when most of the action is near an existing match. Each group
    // (connected, then unconnected) is independently re-sorted alphabetically
    // every time this runs, so the ordering never drifts from a stale state.
    // Column count/height rules: at most 5 columns (hard cap); each column
    // is as close as possible to the Source column's current height, with a
    // floor of 20 rows -- but if 5 columns still isn't enough to fit
    // everyone at that height, columns grow taller rather than adding a 6th
    // column, since the column-count cap is the hard constraint and the
    // height matching is the soft one.
    private void ArrangeTargetsInColumns()
    {
        var visibleTargets = Nodes.Where(n => !n.IsSource && !n.IsHeader && n.IsVisible).ToList();
        if (visibleTargets.Count == 0) return;

        var connected = visibleTargets
            .Where(t => Connections.Any(c => c.Target == t && c.IsVisible))
            .OrderBy(t => t.Name)
            .ToList();
        var unconnected = visibleTargets
            .Where(t => !Connections.Any(c => c.Target == t && c.IsVisible))
            .OrderBy(t => t.Name)
            .ToList();
        var targets = connected.Concat(unconnected).ToList();

        int sourceVisibleCount = Nodes.Count(n => n.IsSource && n.IsVisible);
        int maxColumnHeight = Math.Max(sourceVisibleCount, MinTargetColumnHeight);

        // Math.Min/Max instead of Math.Clamp: the latter doesn't exist on net48
        int numColumns = Math.Min(Math.Max(
            (int)Math.Ceiling((double)targets.Count / maxColumnHeight),
            1), MaxTargetColumns);
        int perColumn = (int)Math.Ceiling((double)targets.Count / numColumns);

        double x = RightColX;
        int idx = 0;
        for (int c = 0; c < numColumns; c++)
        {
            double y = TopMargin;
            for (int i = 0; i < perColumn && idx < targets.Count; i++, idx++)
            {
                targets[idx].Location = new Point(x, y);
                y += NodeSpacing;
            }
            x += NodeWidth + TargetColumnGap;
        }
    }

    // "The header is more like a Container... it sort of fences around the
    // visible nodes with a light box" (chris, 2026-07-10) -- not a small
    // fixed label anymore. Recomputes both header items' Location/Size to
    // bound whatever's currently visible on their side, every time
    // visibility/layout changes.
    private void UpdateHeaderBounds()
    {
        if (_sourceHeader is not null)
        {
            var visible = Nodes.Where(n => n.IsSource && n.IsVisible).ToList();
            var (loc, size) = ComputeContainerBounds(visible, LeftColX);
            _sourceHeader.Location = loc;
            _sourceHeader.Size = size;
        }
        if (_targetHeader is not null)
        {
            var visible = Nodes.Where(n => !n.IsSource && !n.IsHeader && n.IsVisible).ToList();
            var (loc, size) = ComputeContainerBounds(visible, RightColX);
            _targetHeader.Location = loc;
            _targetHeader.Size = size;
        }
    }

    private static (Point Location, Size Size) ComputeContainerBounds(List<LayerNodeViewModel> nodes, double emptyFallbackX)
    {
        // Empty side: shrink to a title-bar-sized box anchored at that
        // side's own column X -- previously fell back to (0,0), which put
        // the Target container's title on top of the Source area whenever
        // every target was filtered out.
        if (nodes.Count == 0)
            return (new Point(emptyFallbackX - HeaderPadding, TopMargin - HeaderPadding - HeaderTopStripHeight),
                    new Size(NodeWidth + HeaderPadding * 2, HeaderTopStripHeight + NodeHeight + HeaderPadding * 2));

        double minX = nodes.Min(n => n.Location.X);
        double minY = nodes.Min(n => n.Location.Y);
        double maxX = nodes.Max(n => n.Location.X) + NodeWidth;
        double maxY = nodes.Max(n => n.Location.Y) + NodeHeight;

        var location = new Point(minX - HeaderPadding, minY - HeaderPadding - HeaderTopStripHeight);
        var size = new Size(
            (maxX - minX) + HeaderPadding * 2,
            (maxY - minY) + HeaderPadding * 2 + HeaderTopStripHeight);
        return (location, size);
    }

    // Undo/redo (chris, Alpha 7): snapshots capture Connections as
    // (source name, target name, match source) triples rather than object
    // references, since a snapshot taken before a node is torn down (e.g.
    // an earlier SwitchTemplate/RemoveEmptyLayers pass) would otherwise
    // hold dangling LayerNodeViewModel references. Restoring re-resolves
    // names against the CURRENT Nodes collection, so history is cleared
    // outright by both of those structural operations below rather than
    // risking a restore that silently drops entries whose nodes no longer
    // exist.
    private readonly Stack<List<(string Source, string Target, ConnectionMatchSource MatchSource)>> _undoStack = new();
    private readonly Stack<List<(string Source, string Target, ConnectionMatchSource MatchSource)>> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    private List<(string Source, string Target, ConnectionMatchSource MatchSource)> CaptureSnapshot() =>
        Connections.Select(c => (c.Source.Name, c.Target.Name, c.MatchSource)).ToList();

    // Called once by the caller BEFORE each user-initiated mapping change
    // (connect, disconnect, un-match) -- callers that mutate several
    // connections in one user gesture (multi-select un-match, multi-source
    // drag-connect) call this once up front so the whole gesture undoes in
    // a single Ctrl+Z, not one step per connection touched.
    public void PushUndoSnapshot()
    {
        _undoStack.Push(CaptureSnapshot());
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void ClearHistory()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0) return;
        _undoStack.Clear();
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RestoreSnapshot(List<(string Source, string Target, ConnectionMatchSource MatchSource)> snapshot)
    {
        foreach (var node in Nodes)
            if (!node.IsHeader) node.IsMapped = false;

        Connections.Clear();

        var sourceMap = Nodes.Where(n => n.IsSource && !n.IsHeader)
            .ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        var targetMap = Nodes.Where(n => !n.IsSource && !n.IsHeader)
            .ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (sourceName, targetName, matchSource) in snapshot)
        {
            if (!sourceMap.TryGetValue(sourceName, out var src)) continue;
            if (!targetMap.TryGetValue(targetName, out var tgt)) continue;
            Connections.Add(new LayerConnectionViewModel(src, tgt, matchSource));
        }
    }

    public void RemoveEmptyLayers()
    {
        ClearHistory();
        var empty = Nodes.Where(n => n.IsSource && n.IsEmpty).ToList();
        foreach (var node in empty)
        {
            var conns = Connections.Where(c => c.Source == node || c.Target == node).ToList();
            foreach (var c in conns)
            {
                c.Source.IsMapped = false;
                if (!Connections.Any(other => other != c && other.Target == c.Target))
                    c.Target.IsMapped = false;
                Connections.Remove(c);
            }
            Nodes.Remove(node);
        }
        ApplyFilters();
    }

    // Shared between the constructor's initial load and SwitchTemplate --
    // exact-name, then translation-memory, then heuristic matches, each
    // tier skipping any source already claimed by an earlier one.
    private void RunMatchingTiers(
        List<LayerNodeViewModel> sourceModels,
        Dictionary<string, LayerNodeViewModel> sourceMap,
        Dictionary<string, LayerNodeViewModel> standardMap,
        Dictionary<string, string>? memoryMappings,
        List<Matching.MatchResult>? heuristicResults)
    {
        // Tier 1: exact name matches — solid green
        foreach (var src in sourceModels)
        {
            if (standardMap.TryGetValue(src.Name, out var tgt))
            {
                Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.ExactName));
            }
        }

        // Tier 2: memory-sourced mappings — blue (skip if already exact-matched)
        if (memoryMappings != null)
        {
            foreach (var kvp in memoryMappings)
            {
                if (sourceMap.TryGetValue(kvp.Key, out var src)
                    && standardMap.TryGetValue(kvp.Value, out var tgt))
                {
                    if (Connections.Any(c => c.Source == src)) continue;
                    Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.Memory));
                }
            }
        }

        // Tier 3: heuristic matches — dashed yellow (skip if already connected)
        if (heuristicResults != null)
        {
            foreach (var result in heuristicResults)
            {
                if (sourceMap.TryGetValue(result.SourceLayer, out var src)
                    && result.TargetLayer is not null
                    && standardMap.TryGetValue(result.TargetLayer, out var tgt))
                {
                    if (Connections.Any(c => c.Source == src)) continue;
                    Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.Heuristic));
                }
            }
        }
    }

    // Rebuilds the entire Target side against a newly-picked template.
    // Source nodes/empty-layer flags are untouched -- only standard/target
    // nodes and every connection are torn down and rebuilt.
    // preserveMappings (source name -> target name), when supplied, is laid
    // on top of the fresh auto-match tiers afterward -- an explicit prior
    // user mapping wins over whatever the auto tiers picked for that source,
    // as long as its old target name still exists in the new template.
    public void SwitchTemplate(
        List<string> newStandardLayers,
        Dictionary<string, string>? memoryMappings,
        List<Matching.MatchResult>? heuristicResults,
        Dictionary<string, string>? preserveMappings)
    {
        ClearHistory();
        var oldTargets = Nodes.Where(n => !n.IsSource && !n.IsHeader).ToList();
        foreach (var n in oldTargets)
            Nodes.Remove(n);
        Connections.Clear();
        TargetFilters.Clear();

        var sourceModels = Nodes.Where(n => n.IsSource).ToList();
        foreach (var s in sourceModels)
            s.IsMapped = false;

        var standardModels = new List<LayerNodeViewModel>();
        for (int i = 0; i < newStandardLayers.Count; i++)
        {
            var vm = new LayerNodeViewModel(newStandardLayers[i], false, new Point(0, 0));
            standardModels.Add(vm);
            Nodes.Add(vm);
        }

        ApplyLayerDictionary(standardModels);

        var sourceMap = sourceModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        var standardMap = standardModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);

        RunMatchingTiers(sourceModels, sourceMap, standardMap, memoryMappings, heuristicResults);

        if (preserveMappings is not null)
        {
            foreach (var kvp in preserveMappings)
            {
                if (!sourceMap.TryGetValue(kvp.Key, out var src)) continue;
                if (!standardMap.TryGetValue(kvp.Value, out var tgt)) continue;

                var existing = Connections.FirstOrDefault(c => c.Source == src);
                if (existing is not null)
                {
                    if (existing.Target == tgt) continue; // already correct
                    existing.Source.IsMapped = false;
                    if (!Connections.Any(c => c != existing && c.Target == existing.Target))
                        existing.Target.IsMapped = false;
                    Connections.Remove(existing);
                }

                Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.Manual));
            }
        }

        ApplyFilters();
    }

}

public class PendingConnectionViewModel
{
    private readonly LayerEditorViewModel _editor;
    private readonly Dictionary<string, LayerNodeViewModel> _sourceMap;
    private readonly Dictionary<string, LayerNodeViewModel> _standardMap;
    private LayerNodeViewModel? _pendingSource;

    public ICommand StartCommand { get; }
    public ICommand FinishCommand { get; }

    public PendingConnectionViewModel(
        LayerEditorViewModel editor,
        Dictionary<string, LayerNodeViewModel> sourceMap,
        Dictionary<string, LayerNodeViewModel> standardMap)
    {
        _editor = editor;
        _sourceMap = sourceMap;
        _standardMap = standardMap;

        StartCommand = new DelegateCommand<object>(param =>
        {
            if (param is LayerNodeViewModel source)
                _pendingSource = source;
        });

        FinishCommand = new DelegateCommand<object>(param =>
        {
            if (param is not LayerNodeViewModel target || _pendingSource is null) return;
            if (_pendingSource == target) return;
            if (!_pendingSource.IsSource) return;
            if (target.IsSource) return;

            // Gather sources: all selected sources, or just the dragged one
            var sources = _editor.Nodes
                .Where(n => n.IsSource && n.IsSelected && n.IsVisible)
                .ToHashSet();
            if (sources.Count == 0)
                sources.Add(_pendingSource);
            else
                sources.Add(_pendingSource); // ensure the dragged source is included

            _editor.PushUndoSnapshot();
            foreach (var src in sources)
            {
                if (_editor.Connections.Any(c => c.Source == src))
                {
                    var old = _editor.Connections.First(c => c.Source == src);
                    old.Source.IsMapped = false;
                    if (!_editor.Connections.Any(other => other != old && other.Target == old.Target))
                        old.Target.IsMapped = false;
                    _editor.Connections.Remove(old);
                }

                _editor.Connections.Add(new LayerConnectionViewModel(src, target, ConnectionMatchSource.Manual));
            }

            // Deselect all sources after connecting
            foreach (var n in _editor.Nodes.Where(n => n.IsSelected))
                n.IsSelected = false;

            _pendingSource = null;
        });
    }
}

public class DelegateCommand<T> : ICommand
{
    private readonly Action<T?> _action;
    private readonly Func<T?, bool>? _condition;

    public event EventHandler? CanExecuteChanged;

    public DelegateCommand(Action<T?> action, Func<T?, bool>? condition = null)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _condition = condition;
    }

    public bool CanExecute(object? parameter)
        => _condition?.Invoke((T?)parameter) ?? true;

    public void Execute(object? parameter)
        => _action((T?)parameter);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
