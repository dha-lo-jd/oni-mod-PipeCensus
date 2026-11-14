using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PipeCensus;

// Full-screen modal summarizing the pipes found in the selected area, laid out like the skills screen: filters in the
// left pane, the list in the right pane. One row is one kind of pipe holding one thing in one pipe network; a row opens
// into its pipe tiles. The list is a snapshot taken when
// the screen opens; filters only narrow what is shown. It has its own root canvas set up like the skills screen's.
public sealed class CensusScreen : KModalScreen
{
    private const float Margin = 10f;
    private const float LeftWidth = 320f;
    private const float TitleHeight = 36f;
    private const float ToggleHeight = 28f;
    private const float SectionHeight = 22f;
    private const float RowHeight = 50f;
    private const float IconSize = 38f;
    private const float HeaderHeight = 24f;
    private const float BoxSize = 16f;
    private const float ExpanderSize = 20f;
    private const float TileRowHeight = 30f;
    private const float TileIndent = 30f;
    private const float Spacing = 2f;

    // More tiles than this scroll inside their own window.
    internal const int MaxTileRows = 20;

    // The management menu buttons sit over the top of the right pane.
    private const float TopReserved = 68f;

    private static readonly Color BoxColor = new Color(0.12f, 0.13f, 0.17f, 1f);
    private static readonly Color CheckColor = new Color(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color ProgressColor = new Color(0.35f, 0.55f, 0.75f, 0.4f);
    private static readonly Color ProgressTrackColor = new Color(0.12f, 0.18f, 0.28f, 0.6f);
    private static readonly Color DimColor = new Color(0.7f, 0.7f, 0.75f, 1f);
    private static readonly Color GermColor = new Color(1f, 0.6f, 0.6f, 1f);
    private static readonly Color RowOn = new Color(0.33f, 0.36f, 0.46f, 1f);
    private static readonly Color EmptyRowColor = new Color(0.2f, 0.215f, 0.28f, 1f);

    private const int KindColumn = 0;
    private const int ContentColumn = 1;
    private const int MassColumn = 2;
    private const int SegmentsColumn = 3;
    private const int FillColumn = 4;
    private const int TemperatureColumn = 5;
    private const int GermsColumn = 6;

    private static readonly float[] ColumnX = { 8f, 90f, 440f, 570f, 660f, 840f, 980f };
    private static readonly PipeKind[] AllKinds = { PipeKind.Liquid, PipeKind.Gas, PipeKind.Solid };
    private static string[] ColumnNames => new string[]
    {
        STRINGS.UI.COLUMNS.KIND, STRINGS.UI.COLUMNS.CONTENT, STRINGS.UI.COLUMNS.MASS, STRINGS.UI.COLUMNS.SEGMENTS,
        STRINGS.UI.COLUMNS.FILL, STRINGS.UI.COLUMNS.TEMPERATURE, STRINGS.UI.COLUMNS.GERMS,
    };

    private static CensusScreen current;

    // The open census, if any, and how many of its rows pass the filters (read by the in-game tests).
    internal static CensusScreen Current => current;
    internal int TotalRows => rows.Count;
    internal int ShownRows { get; private set; }
    internal PipeRow Row(int index) => rows[index];
    internal ScrollRect ListScroll => listScroll;
    internal bool IsExpanded(PipeRow row) => expanded.Contains(row);

    private sealed class ToggleView
    {
        public MultiToggle Vanilla;
        public Image Background;
        public GameObject Check;

        public void Set(bool on)
        {
            if (Vanilla != null)
            {
                Vanilla.ChangeState(on ? 1 : 0);
                return;
            }

            Background.color = on ? RowOn : UiStyle.Button;
            Check.SetActive(on);
        }
    }

    private RectTransform rect;
    private RectTransform content;
    private LocText countLabel;
    private RectTransform header;
    private readonly List<LocText> headerLabels = new List<LocText>();

    // Column being sorted (-1: snapshot order) and its direction; unknown values always sort last.
    private int sortColumn = -1;
    private bool sortDescending;
    private List<PipeRow> rows;
    private readonly HashSet<PipeKind> kinds = new HashSet<PipeKind>(AllKinds);
    private readonly HashSet<string> contents = new HashSet<string>();
    private readonly Dictionary<PipeKind, ToggleView> kindToggles = new Dictionary<PipeKind, ToggleView>();
    private readonly Dictionary<string, ToggleView> contentToggles = new Dictionary<string, ToggleView>();
    private ToggleView allToggle;
    private ScrollRect listScroll;
    private readonly HashSet<PipeRow> expanded = new HashSet<PipeRow>();

    public static void Open(CensusSelection selection)
    {
        if (current != null)
        {
            current.Close();
        }

        // Opening this window while another one is up replaces it.
        CloseOtherScreens();

        OpenRows(PipeCollector.Collect(selection));
    }

    // Opens the window on made-up rows too (the in-game tests).
    internal static void OpenRows(List<PipeRow> rows)
    {
        if (current != null)
        {
            current.Close();
        }

        CloseOtherScreens();
        var skills = ManagementMenu.Instance != null
            ? Traverse.Create(ManagementMenu.Instance).Field("skillsScreen").GetValue<SkillsScreen>()
            : null;
        var skillsCanvas = skills != null ? skills.GetComponent<Canvas>() : null;
        UiStyle.Harvest(skills != null ? skills.transform : null);
        var parent = skillsCanvas != null && skills.transform.parent != null
            ? skills.transform.parent
            : GameScreenManager.Instance.ssOverlayCanvas.transform;

        var go = new GameObject("PipeCensusScreen", typeof(RectTransform));
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        CopyCanvasSetup(go, skills != null ? skills.gameObject : null, skillsCanvas);

        var screen = go.AddComponent<CensusScreen>();
        screen.rect = go.GetComponent<RectTransform>();
        screen.rows = Arranged(rows);
        current = screen;
        screen.Activate();
        screen.Show(true);
        Canvas.ForceUpdateCanvases();
        screen.Build();
    }

    // Per kind, the things with the most mass first, the empty tiles last; the networks of one thing stay together.
    internal static List<PipeRow> Arranged(List<PipeRow> rows)
    {
        var total = rows.GroupBy(r => (int)r.Kind + ":" + r.ContentKey).ToDictionary(g => g.Key, g => g.Sum(r => r.Mass));
        return rows.OrderBy(r => (int)r.Kind)
            .ThenBy(r => r.IsEmpty ? 1 : 0)
            .ThenByDescending(r => total[(int)r.Kind + ":" + r.ContentKey])
            .ThenBy(r => r.ContentKey, System.StringComparer.Ordinal)
            .ThenBy(r => r.NetworkNumber)
            .ToList();
    }

    // Same canvas settings and scaler as the skills screen; without a source, a plain overlay canvas.
    private static void CopyCanvasSetup(GameObject go, GameObject source, Canvas sourceCanvas)
    {
        var canvas = go.AddComponent<Canvas>();
        var scaler = go.AddComponent<CanvasScaler>();
        if (sourceCanvas == null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<GraphicRaycaster>();
            return;
        }

        canvas.renderMode = sourceCanvas.renderMode;
        canvas.worldCamera = sourceCanvas.worldCamera;
        canvas.planeDistance = sourceCanvas.planeDistance;
        canvas.pixelPerfect = sourceCanvas.pixelPerfect;
        canvas.overrideSorting = sourceCanvas.overrideSorting;
        canvas.sortingLayerID = sourceCanvas.sortingLayerID;
        canvas.sortingOrder = sourceCanvas.sortingOrder;

        var sourceScaler = source.GetComponent<CanvasScaler>();
        if (sourceScaler != null)
        {
            scaler.uiScaleMode = sourceScaler.uiScaleMode;
            scaler.scaleFactor = sourceScaler.scaleFactor;
            scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            scaler.referenceResolution = sourceScaler.referenceResolution;
            scaler.screenMatchMode = sourceScaler.screenMatchMode;
            scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
        }

        // Keeps the scale factor following the game's UI scale setting and resolution.
        if (source.GetComponent<KCanvasScaler>() != null)
        {
            go.AddComponent<KCanvasScaler>();
        }

        go.AddComponent<GraphicRaycaster>();
    }

    private static bool ManagementScreenOpen() =>
        ManagementMenu.Instance != null && Traverse.Create(ManagementMenu.Instance).Field("activeScreen").GetValue() != null;

    private static void CloseOtherScreens()
    {
        if (ManagementScreenOpen())
        {
            ManagementMenu.Instance.CloseAll();
        }

        if (PauseScreen.Instance != null && PauseScreen.Instance.gameObject.activeInHierarchy)
        {
            PauseScreen.Instance.Show(false);
        }

        ModalScreens.CloseOthers(null);
    }

    // Hiding this window (e.g. another mod's window opening) closes it for good.
    public override void Show(bool show = true)
    {
        if (!show)
        {
            Close();
            return;
        }

        base.Show(show);
    }

    // Another management screen (skills, research, ...) or the pause menu took over: close this window.
    private void Update()
    {
        if (ManagementScreenOpen() || (PauseScreen.Instance != null && PauseScreen.Instance.gameObject.activeInHierarchy))
        {
            Close();
        }
    }

    public override void OnKeyDown(KButtonEvent e)
    {
        if (!e.Consumed && (e.TryConsume(Action.Escape) || e.TryConsume(Action.MouseRight)))
        {
            Close();
        }

        base.OnKeyDown(e);
    }

    public void Close()
    {
        if (current == this)
        {
            current = null;
        }

        Deactivate();
        Destroy(gameObject);
    }

    private void Build()
    {
        UiStyle.Apply(gameObject.AddComponent<Image>(), "web_box", UiStyle.Background);

        // Right pane: the darker content color the skills screen uses behind its tables.
        var right = UiFactory.CreateRect("Right pane", rect, 0f, 0f, 0f, 0f);
        right.anchorMin = Vector2.zero;
        right.anchorMax = Vector2.one;
        right.pivot = new Vector2(0.5f, 0.5f);
        right.offsetMin = new Vector2(LeftWidth, 0f);
        right.offsetMax = Vector2.zero;
        right.gameObject.AddComponent<Image>().color = UiStyle.Content;
        var rightWidth = (rect.rect.width > 100f ? rect.rect.width : 1200f) - LeftWidth;

        BuildLeftPane();
        BuildHeader(right, rightWidth);
        BuildList(right);
        Refresh();
    }

    // Left pane: black border, purple title, then the scrolling filter list.
    private void BuildLeftPane()
    {
        var border = UiFactory.CreateRect("Pane border", rect, 0f, 0f, LeftWidth, 0f);
        border.anchorMin = new Vector2(0f, 0f);
        border.anchorMax = new Vector2(0f, 1f);
        border.sizeDelta = new Vector2(LeftWidth, 0f);
        border.anchoredPosition = Vector2.zero;
        var borderImage = border.gameObject.AddComponent<Image>();
        UiStyle.Apply(borderImage, "web_border", Color.black);
        borderImage.raycastTarget = false;

        UiFactory.CreateSpritePanel("Pane title", rect, "web_button", UiStyle.TitleBar, 0f, 0f, LeftWidth, TitleHeight);
        var title = UiFactory.CreateLabel(rect, STRINGS.UI.TITLE, 8f, 8f, 190f, 20f, style: "style_titleTextLarge");
        title.fontSize = 18f;
        countLabel = UiFactory.CreateLabel(rect, "", 200f, 8f, 80f, 20f, TMPro.TextAlignmentOptions.Right);
        var close = UiFactory.CreateButton(rect, "X", LeftWidth - 32f, 6f, 24f, 24f, Close);
        close.name = "Close";

        var viewport = UiFactory.CreateRect("Filter viewport", rect, 0f, TitleHeight, 0f, 0f);
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(0f, 1f);
        viewport.pivot = new Vector2(0f, 1f);
        viewport.offsetMin = new Vector2(4f, 4f);
        viewport.offsetMax = new Vector2(LeftWidth - 4f, -TitleHeight);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var filters = UiFactory.CreateRect("Filters", viewport, 0f, 0f, 0f, 0f);
        filters.anchorMin = new Vector2(0f, 1f);
        filters.anchorMax = new Vector2(1f, 1f);
        filters.pivot = new Vector2(0.5f, 1f);
        filters.offsetMin = Vector2.zero;
        filters.offsetMax = Vector2.zero;
        var layout = filters.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2f;
        layout.padding = new RectOffset(2, 2, 6, 6);
        filters.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ConfigureScroll(viewport, filters);

        AddSection(filters, STRINGS.UI.FILTERS.KIND);
        foreach (var kind in AllKinds)
        {
            var captured = kind;
            var count = rows.Where(r => r.Kind == kind).Sum(r => r.Segments);
            kindToggles[kind] = AddToggle(filters, KindName(kind) + " (" + count + ")", () => ToggleKind(captured));
        }

        // One toggle per thing actually present in the area (the same name in several kinds of pipe is one toggle).
        AddSection(filters, STRINGS.UI.FILTERS.CONTENT);
        allToggle = AddToggle(filters, STRINGS.UI.FILTERS.ALL + " (" + rows.Where(r => !r.IsEmpty).Select(r => r.ContentKey).Distinct().Count() + ")", ClearContents);
        foreach (var group in rows.Where(r => !r.IsEmpty).GroupBy(r => r.ContentKey).OrderBy(g => g.First().Name, System.StringComparer.CurrentCulture))
        {
            var key = group.Key;
            contentToggles[key] = AddToggle(filters, group.First().Name + " (" + group.Sum(r => r.Segments) + ")", () => ToggleContent(key));
        }
    }

    internal void ToggleKind(PipeKind kind)
    {
        if (!kinds.Remove(kind))
        {
            kinds.Add(kind);
        }

        Refresh();
    }

    internal void ToggleContent(string key)
    {
        if (!contents.Remove(key))
        {
            contents.Add(key);
        }

        Refresh();
    }

    internal void ClearContents()
    {
        contents.Clear();
        Refresh();
    }

    // Shows or hides the tiles of a row; the list stays where it was.
    internal void ToggleExpanded(PipeRow row)
    {
        if (!expanded.Remove(row))
        {
            expanded.Add(row);
        }

        var offset = content.anchoredPosition.y;
        Refresh();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
    }

    private static ScrollRect ConfigureScroll(RectTransform viewport, RectTransform content, bool nested = false)
    {
        var scroll = nested ? viewport.gameObject.AddComponent<NestedScrollRect>() : viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return scroll;
    }

    private static void AddSection(RectTransform parent, string text)
    {
        var holder = UiFactory.CreateRect("Section", parent, 0f, 0f, 0f, SectionHeight);
        holder.gameObject.AddComponent<LayoutElement>().minHeight = SectionHeight;
        UiFactory.CreateLabel(holder, text, 4f, 2f, 240f, 20f);
    }

    // A button row with a checkbox in front of the label. Clicking anywhere on the row toggles; the checkbox mirrors
    // the toggle state. The checkbox is the game's own filter checkbox when available, else a drawn box.
    private static ToggleView AddToggle(RectTransform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var button = UiFactory.CreateButton(parent, "", 0f, 0f, 0f, ToggleHeight, onClick);
        button.gameObject.AddComponent<LayoutElement>().minHeight = ToggleHeight;
        var holder = button.GetComponent<RectTransform>();
        UiFactory.CreateLabel(holder, text, 8f + BoxSize + 8f, (ToggleHeight - 20f) / 2f, 260f, 20f);

        var vanilla = GraftVanillaCheckbox(holder);
        if (vanilla != null)
        {
            return new ToggleView { Vanilla = vanilla };
        }

        var box = UiFactory.CreatePanel("Checkbox", holder, BoxColor, 8f, (ToggleHeight - BoxSize) / 2f, BoxSize, BoxSize);
        box.raycastTarget = false;
        var check = UiFactory.CreatePanel("Check", box.transform, CheckColor, 3f, 3f, BoxSize - 6f, BoxSize - 6f);
        check.raycastTarget = false;
        return new ToggleView { Background = button.GetComponent<Image>(), Check = check.gameObject };
    }

    // Takes the checkbox out of the game's filter row and puts it at the left of our own row. Clicks on the checkbox
    // itself are consumed by it, so only row clicks reach the row button (no double toggle).
    private static MultiToggle GraftVanillaCheckbox(RectTransform row)
    {
        var prefab = ToolMenu.Instance != null && ToolMenu.Instance.toolParameterMenu != null
            ? ToolMenu.Instance.toolParameterMenu.widgetPrefab
            : null;
        if (prefab == null)
        {
            return null;
        }

        var source = Util.KInstantiateUI(prefab, row.gameObject, force_active: true);
        var toggle = source.GetComponentInChildren<MultiToggle>();
        if (toggle == null)
        {
            Destroy(source);
            return null;
        }

        var rect = toggle.GetComponent<RectTransform>();
        var size = rect.rect.size;
        rect.SetParent(row, false);
        Destroy(source);
        var layoutElement = toggle.GetComponent<LayoutElement>();
        if (layoutElement != null)
        {
            Destroy(layoutElement);
        }

        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size.x > 4f && size.y > 4f ? size : new Vector2(BoxSize, BoxSize);
        rect.anchoredPosition = new Vector2(8f, 0f);

        // The checkbox is display only: let clicks on it fall through to the row button behind it.
        foreach (var graphic in toggle.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        return toggle;
    }

    // The header is one container so it can follow the list when it is pulled down past its top.
    private void BuildHeader(RectTransform right, float width)
    {
        header = UiFactory.CreateRect("Header", right, Margin, TopReserved, width - Margin * 2f, HeaderHeight);
        UiFactory.CreateSpritePanel("Header background", header, "web_box", UiStyle.SortHeader, 0f, 0f, width - Margin * 2f, HeaderHeight);
        for (var i = 0; i < ColumnNames.Length; i++)
        {
            headerLabels.Add(UiFactory.CreateLabel(header, ColumnNames[i], ColumnX[i], 2f, 240f, 20f));

            // Transparent click area over the header cell: ascending, descending, then unsorted.
            var column = i;
            var areaWidth = column + 1 < ColumnX.Length ? ColumnX[column + 1] - ColumnX[column] - 4f : 200f;
            var area = UiFactory.CreatePanel("Sort " + column, header, new Color(0f, 0f, 0f, 0f), ColumnX[column] - 4f, 0f, areaWidth, HeaderHeight);
            var button = area.gameObject.AddComponent<Button>();
            button.targetGraphic = area;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.3f);
            button.colors = colors;
            button.onClick.AddListener(() => CycleSort(column));
        }
    }

    private void CycleSort(int column)
    {
        if (sortColumn != column)
        {
            sortColumn = column;
            sortDescending = false;
        }
        else if (!sortDescending)
        {
            sortDescending = true;
        }
        else
        {
            sortColumn = -1;
            sortDescending = false;
        }

        for (var i = 0; i < headerLabels.Count; i++)
        {
            headerLabels[i].text = ColumnNames[i] + (i == sortColumn ? (sortDescending ? " ▼" : " ▲") : "");
        }

        Refresh();
    }

    // Numeric sort key of a column; null when the value is unknown (those rows always go last).
    private static double? SortKey(PipeRow row, int column)
    {
        switch (column)
        {
            case KindColumn: return (int)row.Kind;
            case MassColumn: return row.IsEmpty ? (double?)null : row.Mass;
            case SegmentsColumn: return row.Segments;
            case FillColumn: return row.Fill < 0f ? (double?)null : row.Fill;
            case TemperatureColumn: return row.Temperature < 0f ? (double?)null : row.Temperature;
            case GermsColumn: return row.IsEmpty ? (double?)null : row.GermTotal;
            default: return null;
        }
    }

    internal static List<PipeRow> Sorted(List<PipeRow> list, int column, bool descending)
    {
        if (column < 0)
        {
            return list;
        }

        if (column == ContentColumn)
        {
            var named = list.Where(r => !r.IsEmpty);
            var byName = descending
                ? named.OrderByDescending(r => r.Name, System.StringComparer.CurrentCulture).ThenByDescending(r => r.NetworkNumber)
                : named.OrderBy(r => r.Name, System.StringComparer.CurrentCulture).ThenBy(r => r.NetworkNumber);
            return byName.Concat(list.Where(r => r.IsEmpty)).ToList();
        }

        var known = list.Where(r => SortKey(r, column) != null);
        var ordered = descending
            ? known.OrderByDescending(r => SortKey(r, column).Value)
            : known.OrderBy(r => SortKey(r, column).Value);
        return ordered.Concat(list.Where(r => SortKey(r, column) == null)).ToList();
    }

    private void BuildList(RectTransform right)
    {
        var top = TopReserved + HeaderHeight + 2f;
        var viewport = UiFactory.CreateRect("Viewport", right, Margin, top, 0f, 0f);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.offsetMin = new Vector2(Margin, Margin);
        viewport.offsetMax = new Vector2(-Margin, -top);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        content = UiFactory.CreateRect("Content", viewport, 0f, 0f, 0f, 0f);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2f;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = ConfigureScroll(viewport, content);
        listScroll = scroll;

        // Rubber band like the skills and research screens: overscroll springs back, flicks coast.
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.1f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;

        // Pulling down past the top drags the header along; scrolling up leaves it fixed above the list.
        scroll.onValueChanged.AddListener(_ =>
        {
            var overscroll = Mathf.Min(0f, content.anchoredPosition.y);
            UiFactory.Place(header, Margin, TopReserved - overscroll, header.sizeDelta.x, HeaderHeight);
        });
    }

    // Rebuilds the visible rows from the filters (the content selection is OR within itself, AND with the kind toggles).
    private void Refresh()
    {
        allToggle.Set(contents.Count == 0);
        foreach (var pair in kindToggles)
        {
            pair.Value.Set(kinds.Contains(pair.Key));
        }

        foreach (var pair in contentToggles)
        {
            pair.Value.Set(contents.Contains(pair.Key));
        }

        for (var i = content.childCount - 1; i >= 0; i--)
        {
            var old = content.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }

        var shown = Sorted(Filter(rows, kinds, contents), sortColumn, sortDescending);
        ShownRows = shown.Count;
        countLabel.text = shown.Count + " / " + rows.Count;
        if (rows.Count == 0)
        {
            AddMessageRow(STRINGS.UI.EMPTY);
        }

        foreach (var row in shown)
        {
            AddRow(row);
        }
    }

    // The empty-tiles lines only pass when no content is selected.
    internal static List<PipeRow> Filter(List<PipeRow> rows, HashSet<PipeKind> kinds, HashSet<string> contents) =>
        rows.Where(r => kinds.Contains(r.Kind) && (contents.Count == 0 || (!r.IsEmpty && contents.Contains(r.ContentKey)))).ToList();

    private void AddMessageRow(string text)
    {
        var holder = UiFactory.CreateRect("Message", content, 0f, 0f, 0f, RowHeight);
        holder.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
        UiFactory.CreateLabel(holder, text, 8f, 2f, 600f, 20f);
    }

    private void AddRow(PipeRow row)
    {
        var button = UiFactory.CreateButton(content, "", 0f, 0f, 0f, RowHeight, () => Pick(row));
        button.name = "Row";
        button.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
        if (row.IsEmpty)
        {
            button.GetComponent<Image>().color = EmptyRowColor;
        }

        var holder = button.GetComponent<RectTransform>();
        var nameX = ColumnX[ContentColumn];
        if (row.Tiles.Count > 1)
        {
            var open = expanded.Contains(row);
            var expander = UiFactory.CreateButton(holder, open ? "-" : "+", nameX, (RowHeight - ExpanderSize) / 2f, ExpanderSize, ExpanderSize, () => ToggleExpanded(row));
            expander.name = "Expander";
            if (!open)
            {
                expander.gameObject.AddComponent<ToolTip>().SetSimpleTooltip(STRINGS.UI.TOOLTIPS.EXPAND);
            }
        }

        nameX += ExpanderSize + 4f;
        if (row.Icon != null)
        {
            var icon = UiFactory.CreatePanel("Icon", holder, row.IconColor, nameX, (RowHeight - IconSize) / 2f, IconSize, IconSize);
            icon.sprite = row.Icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        nameX += IconSize + 6f;
        Color? dim = row.IsEmpty ? DimColor : (Color?)null;
        Cell(holder, KindColumn, KindName(row.Kind));
        Cell(holder, ContentColumn, DisplayName(row), x: nameX, color: dim);
        if (row.IsEmpty)
        {
            for (var column = MassColumn; column <= GermsColumn; column++)
            {
                if (column == SegmentsColumn)
                {
                    Cell(holder, column, row.Segments.ToString(), STRINGS.UI.TOOLTIPS.SEGMENTS);
                }
                else
                {
                    Cell(holder, column, "-", color: DimColor);
                }
            }

            AddTiles(row);
            return;
        }

        Cell(holder, MassColumn, GameUtil.GetFormattedMass(row.Mass));
        Cell(holder, SegmentsColumn, row.Segments.ToString(), STRINGS.UI.TOOLTIPS.SEGMENTS);
        var capacity = row.Segments * PipeCollector.Capacity(row.Kind);
        Cell(holder, FillColumn, Mathf.RoundToInt(row.Fill * 100f) + "%", string.Format(STRINGS.UI.TOOLTIPS.FILL, GameUtil.GetFormattedMass(capacity)), ratio: row.Fill);
        Cell(holder, TemperatureColumn, TemperatureText(row));
        Cell(holder, GermsColumn, GermsText(row), GermsTooltip(row), color: row.GermTotal > 0 ? GermColor : DimColor);
        AddTiles(row);
    }

    private static string DisplayName(PipeRow row) =>
        row.NetworkNumber > 0 ? string.Format(STRINGS.UI.FORMATS.NETWORK_ROW, row.Name, row.NetworkNumber) : row.Name;

    // The tiles of an open row, indented under it. Up to MaxTileRows they are plain rows of the list; more than that sit
    // in a window of MaxTileRows rows that scrolls by itself.
    private void AddTiles(PipeRow row)
    {
        if (row.Tiles.Count <= 1 || !expanded.Contains(row))
        {
            return;
        }

        if (row.Tiles.Count <= MaxTileRows)
        {
            foreach (var tile in row.Tiles)
            {
                AddTileRow(content, row, tile);
            }

            return;
        }

        var height = MaxTileRows * TileRowHeight + (MaxTileRows - 1) * Spacing;
        var viewport = UiFactory.CreateRect("Tiles viewport", content, 0f, 0f, 0f, height);
        var element = viewport.gameObject.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

        var inner = UiFactory.CreateRect("Tiles", viewport, 0f, 0f, 0f, 0f);
        inner.anchorMin = new Vector2(0f, 1f);
        inner.anchorMax = new Vector2(1f, 1f);
        inner.pivot = new Vector2(0.5f, 1f);
        inner.offsetMin = Vector2.zero;
        inner.offsetMax = Vector2.zero;
        var layout = inner.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = Spacing;
        inner.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = ConfigureScroll(viewport, inner, nested: true);
        scroll.inertia = false;

        foreach (var tile in row.Tiles)
        {
            AddTileRow(inner, row, tile);
        }

        // A thin bar at the right edge shows there is more and where the window is.
        var track = UiFactory.CreatePanel("Scroll track", viewport, ProgressTrackColor, 0f, 0f, 4f, height);
        track.raycastTarget = false;
        track.rectTransform.anchorMin = new Vector2(1f, 1f);
        track.rectTransform.anchorMax = new Vector2(1f, 1f);
        track.rectTransform.pivot = new Vector2(1f, 1f);
        track.rectTransform.anchoredPosition = new Vector2(-2f, 0f);
        var thumbHeight = Mathf.Max(16f, height * MaxTileRows / (float)row.Tiles.Count);
        var thumb = UiFactory.CreatePanel("Scroll thumb", viewport, DimColor, 0f, 0f, 4f, thumbHeight);
        thumb.raycastTarget = false;
        thumb.rectTransform.anchorMin = new Vector2(1f, 1f);
        thumb.rectTransform.anchorMax = new Vector2(1f, 1f);
        thumb.rectTransform.pivot = new Vector2(1f, 1f);
        thumb.rectTransform.sizeDelta = new Vector2(4f, thumbHeight);
        thumb.rectTransform.anchoredPosition = new Vector2(-2f, 0f);
        scroll.onValueChanged.AddListener(_ =>
            thumb.rectTransform.anchoredPosition = new Vector2(-2f, -(1f - scroll.verticalNormalizedPosition) * (height - thumbHeight)));
        viewport.gameObject.AddComponent<ToolTip>().SetSimpleTooltip(STRINGS.UI.TOOLTIPS.TILE_SCROLL);
    }

    private void AddTileRow(RectTransform parent, PipeRow row, PipeTile tile)
    {
        var button = UiFactory.CreateButton(parent, "", 0f, 0f, 0f, TileRowHeight, () => Pick(tile.Target));
        button.name = "TileRow";
        button.gameObject.AddComponent<LayoutElement>().minHeight = TileRowHeight;
        button.GetComponent<Image>().color = EmptyRowColor;
        var holder = button.GetComponent<RectTransform>();
        Grid.CellToXY(tile.Cell, out var x, out var y);
        var nameX = ColumnX[ContentColumn] + TileIndent;
        Color? dim = tile.IsEmpty ? DimColor : (Color?)null;
        Cell(holder, ContentColumn, string.Format(STRINGS.UI.FORMATS.TILE, x, y), x: nameX, color: dim, rowHeight: TileRowHeight);
        if (tile.IsEmpty)
        {
            return;
        }

        Cell(holder, MassColumn, GameUtil.GetFormattedMass(tile.Mass), rowHeight: TileRowHeight);
        var capacity = PipeCollector.Capacity(row.Kind);
        Cell(holder, FillColumn, Mathf.RoundToInt(tile.Fill * 100f) + "%", string.Format(STRINGS.UI.TOOLTIPS.FILL, GameUtil.GetFormattedMass(capacity)), ratio: tile.Fill, rowHeight: TileRowHeight);
        Cell(holder, TemperatureColumn, GameUtil.GetFormattedTemperature(tile.Temperature), rowHeight: TileRowHeight);
        var germs = tile.DiseaseCount > 0 && tile.DiseaseIdx != byte.MaxValue;
        var germText = germs
            ? GameUtil.GetFormattedDiseaseName(tile.DiseaseIdx) + " " + GameUtil.GetFormattedDiseaseAmount(tile.DiseaseCount)
            : "-";
        Cell(holder, GermsColumn, germText, color: germs ? GermColor : DimColor, rowHeight: TileRowHeight);
    }

    private static string TemperatureText(PipeRow row)
    {
        var average = GameUtil.GetFormattedTemperature(row.Temperature);
        if (row.MaxTemperature - row.MinTemperature < 0.5f)
        {
            return average;
        }

        return average + "\n" + string.Format(STRINGS.UI.FORMATS.RANGE, GameUtil.GetFormattedTemperature(row.MinTemperature), GameUtil.GetFormattedTemperature(row.MaxTemperature));
    }

    // Total germ count and the most common germ.
    private static string GermsText(PipeRow row)
    {
        if (row.GermTotal <= 0)
        {
            return "-";
        }

        var top = row.Germs.OrderByDescending(p => p.Value).First();
        return string.Format(STRINGS.UI.FORMATS.GERMS, GameUtil.GetFormattedDiseaseName(top.Key), GameUtil.GetFormattedDiseaseAmount(row.GermTotal));
    }

    private static string GermsTooltip(PipeRow row) =>
        row.Germs.Count == 0 ? null : string.Join("\n", row.Germs.OrderByDescending(p => p.Value).Select(p => GameUtil.GetFormattedDisease(p.Key, (int)System.Math.Min(p.Value, int.MaxValue))));

    private static void Cell(RectTransform holder, int column, string text, string tooltip = null, float ratio = -1f, Color? color = null, float x = -1f, float rowHeight = RowHeight)
    {
        // Progress bar behind the text (growth toward the next harvest).
        if (ratio >= 0f && column + 1 < ColumnX.Length)
        {
            var barWidth = ColumnX[column + 1] - ColumnX[column] - 8f;
            var track = UiFactory.CreatePanel("Progress track", holder, ProgressTrackColor, ColumnX[column] - 2f, 4f, barWidth, rowHeight - 8f);
            track.raycastTarget = false;
            var bar = UiFactory.CreatePanel("Progress", holder, ProgressColor, ColumnX[column] - 2f, 4f, barWidth * Mathf.Clamp01(ratio), rowHeight - 8f);
            bar.raycastTarget = false;
        }

        var last = column + 1 >= ColumnX.Length;
        if (x < 0f)
        {
            x = ColumnX[column];
        }

        var width = last ? 700f : ColumnX[column + 1] - x - 4f;
        // Two-line texts (growth, yield) get a taller label; the label centers its lines vertically.
        var height = text != null && text.Contains('\n') ? 40f : 20f;
        var label = UiFactory.CreateLabel(holder, text, x, (rowHeight - height) / 2f, width, height);
        if (color.HasValue)
        {
            label.color = color.Value;
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            // Invisible hover area over the cell; clicks fall through to the row button behind it.
            var hover = UiFactory.CreatePanel("Hover", holder, new Color(0f, 0f, 0f, 0f), ColumnX[column], 0f, width, rowHeight);
            hover.gameObject.AddComponent<ToolTip>().SetSimpleTooltip(tooltip);
        }
    }

    private static string KindName(PipeKind kind)
    {
        switch (kind)
        {
            case PipeKind.Liquid: return STRINGS.UI.KINDS.LIQUID;
            case PipeKind.Gas: return STRINGS.UI.KINDS.GAS;
            default: return STRINGS.UI.KINDS.SOLID;
        }
    }

    // Closes the modal and shows the clicked pipe.
    private void Pick(PipeRow row) => Pick(row.Target);

    private void Pick(GameObject target)
    {
        Close();
        if (target == null || SelectTool.Instance == null)
        {
            return;
        }

        var selectable = target.GetComponent<KSelectable>();
        if (selectable == null)
        {
            return;
        }

        SelectTool.Instance.Activate();
        SelectTool.Instance.SelectAndFocus(target.transform.GetPosition(), selectable);
    }
}
