using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fy.EventSystem.Editor
{
    /// <summary>
    /// Lists every event in the project, one tab per assembly, and for the selected one shows the code that
    /// publishes and subscribes to it.
    /// </summary>
    /// <remarks>
    /// The data comes from scanning compiled assemblies, so it reflects the code as written rather than the current
    /// play session: an event with no listeners at runtime still shows the places that would subscribe to it.
    /// Scanning is deliberate rather than continuous, so refresh after recompiling to pick up new call sites.
    /// </remarks>
    public sealed class EventSystemWindow : EditorWindow
    {
        private readonly Dictionary<string, Tab> _tabsByAssembly = new();
        private readonly List<string> _assemblies = new();
        private readonly List<Type> _visibleTypes = new();

        private Dictionary<string, List<Type>> _allTypesByAssembly = new();
        private Dictionary<string, List<EventCallSite>> _callSites = new();
        private VisualElement _tabStrip;
        private TabView _tabView;
        private VisualElement _listHost;
        private ListView _eventList;
        private ScrollView _detail;
        private Label _statusLabel;
        private Texture _eventIcon;
        private Type _selectedType;
        private string _searchText = string.Empty;

        private void CreateGUI()
        {
            rootVisualElement.style.fontSize = 13;
            _eventIcon = EditorGUIUtility.IconContent("cs Script Icon").image;

            rootVisualElement.Add(BuildToolbar());

            _tabStrip = new VisualElement();
            _tabStrip.style.flexShrink = 0;
            rootVisualElement.Add(_tabStrip);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 260f, TwoPaneSplitViewOrientation.Horizontal);

            _listHost = new VisualElement();
            _listHost.style.flexGrow = 1;
            split.Add(_listHost);

            _detail = new ScrollView();
            _detail.style.flexGrow = 1;
            split.Add(_detail);

            rootVisualElement.Add(split);
            rootVisualElement.Add(BuildFooter());

            BuildEventList();
            Refresh();
        }

        [MenuItem("Window/Fy/Event System")]
        private static void Open()
        {
            EventSystemWindow window = GetWindow<EventSystemWindow>();
            window.titleContent = new GUIContent("Event System");
            window.minSize = new Vector2(520f, 300f);
        }

        private VisualElement BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();

            ToolbarButton refreshButton = new ToolbarButton(Refresh) { text = "Refresh" };
            refreshButton.tooltip = "Rescan the compiled assemblies for publishers and subscribers.";
            toolbar.Add(refreshButton);

            ToolbarSearchField searchField = new ToolbarSearchField();
            searchField.style.flexGrow = 1f;
            searchField.RegisterValueChangedCallback(changeEvent =>
            {
                _searchText = changeEvent.newValue ?? string.Empty;
                ApplyFilter();
            });
            toolbar.Add(searchField);

            return toolbar;
        }

        private void BuildEventList()
        {
            _eventList = new ListView
            {
                itemsSource = _visibleTypes,
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                makeItem = MakeRow
            };
            _eventList.style.flexGrow = 1;
            _eventList.bindItem = BindRow;
            _eventList.selectionChanged += _ => HandleSelectionChanged();

            _listHost.Add(_eventList);
        }

        private VisualElement BuildFooter()
        {
            VisualElement footer = new VisualElement();
            footer.style.flexShrink = 0;
            footer.style.paddingLeft = EventWindowStyles.Space1;
            footer.style.paddingRight = EventWindowStyles.Space1;
            footer.style.paddingTop = 3;
            footer.style.paddingBottom = 3;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = EventWindowStyles.SeparatorColor;

            _statusLabel = new Label();
            _statusLabel.style.color = EventWindowStyles.MutedTextColor;
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            footer.Add(_statusLabel);

            return footer;
        }

        private void Refresh()
        {
            List<Type> eventTypes = EventUsageAnalyzer.FindEventTypes();
            _callSites = EventUsageAnalyzer.FindCallSites(eventTypes);
            _allTypesByAssembly = GroupByAssembly(eventTypes);

            int callSiteCount = _callSites.Values.Sum(sites => sites.Count);
            _statusLabel.text = $"{eventTypes.Count} events in {_allTypesByAssembly.Count} assemblies, " +
                                $"{callSiteCount} call sites. Scanned from compiled assemblies — " +
                                "refresh after recompiling.";

            List<string> assemblies = _allTypesByAssembly.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

            if (!assemblies.SequenceEqual(_assemblies))
            {
                RebuildTabs(assemblies);
            }

            ApplyFilter();

            if (_selectedType == null)
            {
                SelectFirst();
            }
        }

        private static Dictionary<string, List<Type>> GroupByAssembly(List<Type> eventTypes)
        {
            Dictionary<string, List<Type>> grouped = new();

            foreach (Type eventType in eventTypes)
            {
                string assembly = eventType.Assembly.GetName().Name;

                if (!grouped.TryGetValue(assembly, out List<Type> bucket))
                {
                    bucket = new List<Type>();
                    grouped.Add(assembly, bucket);
                }

                bucket.Add(eventType);
            }

            return grouped;
        }

        /// <summary>
        /// Rebuilds the assembly strip. Only called when the set of assemblies itself changes, so searching and
        /// refreshing keep the tab you were on.
        /// </summary>
        private void RebuildTabs(List<string> assemblies)
        {
            string activeAssembly = ActiveAssembly();

            _assemblies.Clear();
            _assemblies.AddRange(assemblies);
            _tabsByAssembly.Clear();
            _tabStrip.Clear();
            _tabView = null;

            if (assemblies.Count == 0)
            {
                return;
            }

            _tabView = new TabView();
            _tabView.style.flexShrink = 0;

            foreach (string assembly in assemblies)
            {
                Tab tab = new Tab(assembly);
                _tabsByAssembly.Add(assembly, tab);
                _tabView.Add(tab);
            }

            _tabView.contentContainer.style.display = DisplayStyle.None;

            int activeIndex = activeAssembly != null ? assemblies.IndexOf(activeAssembly) : -1;
            _tabView.selectedTabIndex = activeIndex >= 0 ? activeIndex : 0;
            _tabView.activeTabChanged += (_, _) => HandleTabChanged();

            _tabStrip.Add(_tabView);
        }

        /// <summary>
        /// Refills the list from the active assembly and the search text, and restates every tab's match count. Tabs
        /// stay put while typing — an assembly whose events are all filtered out shows a zero rather than vanishing.
        /// </summary>
        private void ApplyFilter()
        {
            foreach (string assembly in _assemblies)
            {
                int matches = _allTypesByAssembly[assembly].Count(Matches);
                _tabsByAssembly[assembly].label = $"{assembly} ({matches})";
            }

            _visibleTypes.Clear();
            string activeAssembly = ActiveAssembly();

            if (activeAssembly != null)
            {
                _visibleTypes.AddRange(_allTypesByAssembly[activeAssembly].Where(Matches));
            }

            int index = _selectedType != null ? _visibleTypes.IndexOf(_selectedType) : -1;

            _eventList.itemsSource = _visibleTypes;
            _eventList.Rebuild();
            _eventList.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
            _eventList.RefreshItems();

            BuildDetail(index >= 0 ? _selectedType : null);
        }

        private bool Matches(Type eventType)
        {
            return string.IsNullOrEmpty(_searchText)
                || eventType.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ActiveAssembly()
        {
            if (_tabView == null)
            {
                return null;
            }

            int index = _tabView.selectedTabIndex;

            return index >= 0 && index < _assemblies.Count ? _assemblies[index] : null;
        }

        /// <summary>
        /// Switching assembly replaces the list, so the selection cannot carry over: the first event of the new
        /// assembly is selected to keep the detail pane showing something.
        /// </summary>
        private void HandleTabChanged()
        {
            _selectedType = null;
            ApplyFilter();
            SelectFirst();
        }

        private void SelectFirst()
        {
            if (_visibleTypes.Count == 0)
            {
                _selectedType = null;
                BuildDetail(null);

                return;
            }

            _eventList.SetSelectionWithoutNotify(new[] { 0 });
            _selectedType = _visibleTypes[0];
            _eventList.RefreshItems();
            BuildDetail(_selectedType);
        }

        private void HandleSelectionChanged()
        {
            int index = _eventList.selectedIndex;

            if (index < 0 || index >= _visibleTypes.Count)
            {
                return;
            }

            _selectedType = _visibleTypes[index];
            _eventList.RefreshItems();
            BuildDetail(_selectedType);
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexGrow = 1;
            row.style.paddingLeft = EventWindowStyles.Space1;
            row.style.paddingRight = EventWindowStyles.Space1;

            VisualElement marker = EventWindowStyles.CreateRightArrow(EventWindowStyles.UnusedColor);
            marker.name = "marker";
            marker.style.marginRight = EventWindowStyles.Space1;
            marker.style.visibility = Visibility.Hidden;
            row.Add(marker);

            Image icon = new Image { name = "icon", image = _eventIcon };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = EventWindowStyles.Space1;
            row.Add(icon);

            Label label = new Label { name = "label" };
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);

            Label unusedBadge = new Label("unused") { name = "unused" };
            unusedBadge.style.color = EventWindowStyles.UnusedColor;
            unusedBadge.style.fontSize = 11;
            row.Add(unusedBadge);

            row.Add(NamedChip("publishers", EventWindowStyles.PublisherColor, "Publishers"));
            row.Add(NamedChip("subscribers", EventWindowStyles.SubscriberColor, "Subscribers"));

            return row;
        }

        private static VisualElement NamedChip(string name, Color color, string tooltip)
        {
            VisualElement chip = EventWindowStyles.CreateCountChip(color, tooltip);
            chip.name = name;

            return chip;
        }

        private void BindRow(VisualElement element, int index)
        {
            Type eventType = _visibleTypes[index];
            bool isSelected = index == _eventList.selectedIndex;
            (int publishers, int subscribers) = CountCallSites(eventType);

            element.Q<Label>("label").text = eventType.Name;
            element.style.backgroundColor = isSelected
                ? EventWindowStyles.SelectedRowColor
                : EventWindowStyles.RowColor(index);
            element.Q("marker").style.visibility = isSelected ? Visibility.Visible : Visibility.Hidden;

            BindChip(element.Q("publishers"), publishers);
            BindChip(element.Q("subscribers"), subscribers);
            element.Q("unused").style.display = publishers + subscribers == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void BindChip(VisualElement chip, int count)
        {
            chip.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            chip.Q<Label>("count").text = count.ToString();
        }

        private (int Publishers, int Subscribers) CountCallSites(Type eventType)
        {
            List<EventCallSite> sites = CallSitesOf(eventType);
            int publishers = 0;

            foreach (EventCallSite site in sites)
            {
                if (site.Kind == EventCallSiteKind.Publisher)
                {
                    publishers++;
                }
            }

            return (publishers, sites.Count - publishers);
        }

        private List<EventCallSite> CallSitesOf(Type eventType)
        {
            return _callSites.TryGetValue(eventType.FullName, out List<EventCallSite> sites)
                ? sites
                : new List<EventCallSite>();
        }

        private void BuildDetail(Type eventType)
        {
            _detail.Clear();

            if (eventType == null)
            {
                ShowPlaceholder(_assemblies.Count > 0
                    ? "Select an event to see its publishers and subscribers."
                    : "No events found.");

                return;
            }

            List<EventCallSite> sites = CallSitesOf(eventType);
            List<EventCallSite> publishers = SortedSites(sites, EventCallSiteKind.Publisher);
            List<EventCallSite> subscribers = SortedSites(sites, EventCallSiteKind.Subscriber);

            VisualElement card = EventWindowStyles.CreateCard();
            card.Add(EventWindowStyles.CreateTypeHeader("Event", eventType.Name, eventType.Namespace));
            card.Add(BuildUsageRow(publishers.Count, subscribers.Count));
            card.Add(BuildCallSiteSection("Publishers", publishers, EventWindowStyles.PublisherColor));
            card.Add(BuildCallSiteSection("Subscribers", subscribers, EventWindowStyles.SubscriberColor));

            _detail.Add(card);
        }

        private static List<EventCallSite> SortedSites(List<EventCallSite> sites, EventCallSiteKind kind)
        {
            return sites
                .Where(site => site.Kind == kind)
                .OrderBy(site => site.DeclaringTypeName, StringComparer.Ordinal)
                .ThenBy(site => site.Line)
                .ToList();
        }

        private static VisualElement BuildUsageRow(int publishers, int subscribers)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = EventWindowStyles.Space1;

            Label prefix = new Label("Usage:");
            prefix.style.color = EventWindowStyles.MutedTextColor;
            prefix.style.fontSize = 11;
            prefix.style.marginRight = EventWindowStyles.Space1;
            row.Add(prefix);

            if (publishers + subscribers == 0)
            {
                row.Add(EventWindowStyles.CreateStatusPill(EventWindowStyles.UnusedColor, "Unused"));

                return row;
            }

            VisualElement publisherPill = EventWindowStyles.CreateStatusPill(EventWindowStyles.PublisherColor,
                $"{publishers} publisher{(publishers == 1 ? string.Empty : "s")}");
            publisherPill.style.marginRight = EventWindowStyles.Space2;
            row.Add(publisherPill);

            row.Add(EventWindowStyles.CreateStatusPill(EventWindowStyles.SubscriberColor,
                $"{subscribers} subscriber{(subscribers == 1 ? string.Empty : "s")}"));

            return row;
        }

        private static VisualElement BuildCallSiteSection(string title, List<EventCallSite> sites, Color color)
        {
            VisualElement section = EventWindowStyles.CreateSubSection($"{title} ({sites.Count})");

            if (sites.Count == 0)
            {
                section.Add(EventWindowStyles.CreateInfoLabel("None found."));

                return section;
            }

            for (int index = 0; index < sites.Count; index++)
            {
                section.Add(CreateCallSiteRow(sites[index], color, index));
            }

            return section;
        }

        /// <summary>
        /// One zebra-striped call site row. Rows with symbols open the file at the call when clicked; rows without
        /// them stay inert and say so, since there is nowhere to jump to.
        /// </summary>
        private static VisualElement CreateCallSiteRow(EventCallSite site, Color color, int index)
        {
            Color background = EventWindowStyles.RowColor(index);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = EventWindowStyles.Space1;
            row.style.paddingRight = EventWindowStyles.Space1;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.backgroundColor = background;

            VisualElement dot = EventWindowStyles.CreateDot(color);
            dot.style.width = 6;
            dot.style.height = 6;
            EventWindowStyles.SetBorderRadius(dot, 3);
            dot.style.marginRight = EventWindowStyles.Space1;
            row.Add(dot);

            Label name = new Label(site.DisplayName);
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(name);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);

            Label location = new Label(DescribeLocation(site));
            location.style.color = EventWindowStyles.MutedTextColor;
            location.style.fontSize = 12;
            location.style.flexShrink = 0;
            row.Add(location);

            if (!site.HasSourceLocation)
            {
                row.tooltip = "This assembly has no readable symbols, so the call has no source location.";

                return row;
            }

            row.tooltip = $"{site.FilePath}:{site.Line}\nClick to open it in your editor.";
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = EventWindowStyles.HoveredRowColor);
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = background);
            row.RegisterCallback<PointerDownEvent>(_ =>
                InternalEditorUtility.OpenFileAtLineExternal(site.FilePath, site.Line));

            return row;
        }

        private static string DescribeLocation(EventCallSite site)
        {
            return site.HasSourceLocation
                ? $"{Path.GetFileName(site.FilePath)}:{site.Line}"
                : "no symbols";
        }

        private void ShowPlaceholder(string message)
        {
            Label label = new Label(message);
            label.style.flexGrow = 1;
            label.style.color = EventWindowStyles.MutedTextColor;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            _detail.Add(label);
        }
    }
}
