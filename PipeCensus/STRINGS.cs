namespace PipeCensus;

// All user-visible text. Translations are loaded from translations/<language code>.po (see PipeCensusMod).
public static class STRINGS
{
    public static class UI
    {
        public static LocString TOOL_NAME = "Pipe Census";
        public static LocString TOOL_TOOLTIP = "Drag to summarize the contents of the pipes in an area";
        public static LocString TITLE = "Pipe Census";
        public static LocString EMPTY = "No pipes in the selected area.";

        public static class FILTERS
        {
            public static LocString KIND = "Pipe";
            public static LocString CONTENT = "Contents";
            public static LocString ALL = "All";
        }

        public static class KINDS
        {
            public static LocString LIQUID = "Liquid";
            public static LocString GAS = "Gas";
            public static LocString SOLID = "Solid";
        }

        public static class COLUMNS
        {
            public static LocString KIND = "Pipe";
            public static LocString CONTENT = "Contents";
            public static LocString MASS = "Mass";
            public static LocString SEGMENTS = "Segments";
            public static LocString FILL = "Filled";
            public static LocString TEMPERATURE = "Temperature";
            public static LocString GERMS = "Germs";
        }

        public static class TOOLTIPS
        {
            public static LocString SEGMENTS = "Pipe tiles holding this";
            public static LocString FILL = "Mass divided by what these tiles can hold ({0})";
            public static LocString EXPAND = "Click the + to list the pipe tiles";
            public static LocString TILE_SCROLL = "More tiles below: scroll the list";

            // {0}: the germ list, one per line
            public static LocString GERMS = "{0}";
        }

        public static class FORMATS
        {
            public static LocString EMPTY_CONTENT = "Empty";

            // Temperature range. {0}: lowest, {1}: highest
            public static LocString RANGE = "{0} - {1}";

            // A row of one network when the same content is in several. {0}: the content, {1}: the network number
            public static LocString NETWORK_ROW = "{0} (network {1})";

            // A pipe tile under a row. {0}: x, {1}: y
            public static LocString TILE = "Tile {0}, {1}";

            // Germs of a row. {0}: germ name, {1}: amount
            public static LocString GERMS = "{0}\n{1}";
        }
    }
}
