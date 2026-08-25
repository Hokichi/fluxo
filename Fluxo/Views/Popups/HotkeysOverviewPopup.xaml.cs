namespace Fluxo.Views.Popups;

public partial class HotkeysOverviewPopup : BasePopup
{
    public HotkeysOverviewPopup()
    {
        InitializeComponent();
        DataContext = this;
    }

    public IReadOnlyList<HotkeyGroup> HotkeyGroups { get; } =
    [
        new HotkeyGroup("Global",
        [
            new HotkeyItem("Open dashboard", Parts("Ctrl", "1")),
            new HotkeyItem("Open analytics", Parts("Ctrl", "2")),
            new HotkeyItem("Open calendar", Parts("Ctrl", "3")),
            new HotkeyItem("Open ledger", Parts("Ctrl", "4")),
            new HotkeyItem("Search everything", Parts("Ctrl", "F")),
            new HotkeyItem("Toggle history", Parts("Ctrl", "H")),
            new HotkeyItem("Undo last log action", Parts("Ctrl", "Z")),
            new HotkeyItem("Redo last log action", Parts("Ctrl", "Y")),
            new HotkeyItem("Open quick access", Parts("Ctrl", "Q")),
            new HotkeyItem("Show hotkeys overview", Parts("Ctrl", "/")),
            new HotkeyItem("Create transaction", Parts("Ctrl", "N")),
            new HotkeyItem("Create recurring transaction", Parts("Ctrl", "Shift", "N")),
            new HotkeyItem("View accounts", Parts("Ctrl", "L")),
            new HotkeyItem("Lock fluxo", Parts("Ctrl", "Shift", "L")),
            new HotkeyItem("Add saving goal", Parts("Ctrl", "Shift", "G")),
            new HotkeyItem("Open planning mode", Parts("Ctrl", "P")),
            new HotkeyItem("Open budget forecast", Parts("Ctrl", "Shift", "P")),
            new HotkeyItem("Toggle notifications", Parts("Ctrl", "Alt", "N"))
        ]),
        new HotkeyGroup("Dashboard",
        [
            new HotkeyItem("Move to previous period", Parts("Ctrl", "Left")),
            new HotkeyItem("Move to next period", Parts("Ctrl", "Right")),
            new HotkeyItem("Move to current period", Parts("Ctrl", "Home"))
        ]),
        new HotkeyGroup("Calendar",
        [
            new HotkeyItem("Select today", Parts("Ctrl", "Home"))
        ]),
        new HotkeyGroup("Ledger",
        [
            new HotkeyItem("Sort amount ascending", Parts("Ctrl", "Up")),
            new HotkeyItem("Sort amount descending", Parts("Ctrl", "Down")),
            new HotkeyItem("Clear ledger filters", Parts("Ctrl", "Shift", "R")),
            new HotkeyItem("Export ledger", Parts("Ctrl", "E"))
        ]),
        new HotkeyGroup("Settings",
        [
            new HotkeyItem("Open settings", Parts("Ctrl", ","))
        ]),
        new HotkeyGroup("Other",
        [
            new HotkeyItem("Switch to daily view", Parts("Ctrl", "Alt", "1")),
            new HotkeyItem("Switch to weekly view", Parts("Ctrl", "Alt", "2")),
            new HotkeyItem("Switch to monthly view", Parts("Ctrl", "Alt", "3")),
            new HotkeyItem("Switch to all-time view", Parts("Ctrl", "Alt", "4")),
            new HotkeyItem("Duplicate transaction detail", Parts("Ctrl", "D")),
            new HotkeyItem("Open data management", Parts("Ctrl", "Shift", "B")),
            new HotkeyItem("Save or apply popup changes", Parts("Ctrl", "S")),
            new HotkeyItem("Save and create another item", Parts("Ctrl", "Shift", "S"))
        ])
    ];

    private static IReadOnlyList<HotkeyPart> Parts(params string[] texts)
    {
        return texts.Select(text => new HotkeyPart(text)).ToArray();
    }

    public sealed record HotkeyGroup
    {
        public HotkeyGroup(string name, IReadOnlyList<HotkeyItem> hotkeys)
        {
            Name = name;
            Hotkeys = hotkeys;

            var midpoint = (Hotkeys.Count + 1) / 2;
            LeftHotkeys = Hotkeys.Take(midpoint).ToArray();
            RightHotkeys = Hotkeys.Skip(midpoint).ToArray();
        }

        public string Name { get; }

        public IReadOnlyList<HotkeyItem> Hotkeys { get; }

        public IReadOnlyList<HotkeyItem> LeftHotkeys { get; }

        public IReadOnlyList<HotkeyItem> RightHotkeys { get; }
    }

    public sealed record HotkeyItem(string Functionality, IReadOnlyList<HotkeyPart> Parts);

    public sealed record HotkeyPart(string Text);
}
