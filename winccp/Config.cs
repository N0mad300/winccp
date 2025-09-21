namespace winccp
{
    public class IniParser
    {
        private readonly List<string> _sectionOrder = new();
        private readonly Dictionary<string, Dictionary<string, List<string>>> _data =
            new(StringComparer.OrdinalIgnoreCase);

        public IniParser(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Config file not found: {filePath}");

            string? currentSection = "DEFAULT";
            _data[currentSection] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _sectionOrder.Add(currentSection);

            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.Trim();

                // Ignore empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                // Section header
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line[1..^1].Trim();
                    if (!_data.ContainsKey(currentSection))
                    {
                        _data[currentSection] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        _sectionOrder.Add(currentSection);
                    }
                }
                else if (line.Contains("="))
                {
                    var parts = line.Split('=', 2);
                    var key = parts[0].Trim();
                    var value = parts.Length > 1 ? parts[1].Trim() : "";

                    if (!_data[currentSection].ContainsKey(key))
                        _data[currentSection][key] = new List<string>();

                    // Split comma lists OR allow repeated keys
                    if (value.Contains(","))
                    {
                        foreach (var v in value.Split(','))
                            _data[currentSection][key].Add(v.Trim());
                    }
                    else
                    {
                        _data[currentSection][key].Add(value);
                    }
                }
            }
        }

        public string? Get(string section, string key, string? defaultValue = null)
        {
            if (_data.TryGetValue(section, out var sectionData) &&
                sectionData.TryGetValue(key, out var values) &&
                values.Count > 0)
            {
                return values[0];
            }
            return defaultValue;
        }

        public List<string> GetList(string section, string key)
        {
            if (_data.TryGetValue(section, out var sectionData) &&
                sectionData.TryGetValue(key, out var values))
            {
                return new List<string>(values);
            }
            return new List<string>();
        }

        public int GetInt(string section, string key, int defaultValue = 0)
        {
            if (int.TryParse(Get(section, key), out int result))
                return result;
            return defaultValue;
        }

        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            var value = Get(section, key);
            if (value == null) return defaultValue;

            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1");
        }

        public IEnumerable<string> GetSections() => _sectionOrder;
    }

    public static class ConfigLoader
    {
        public static Config Load(string filePath)
        {
            var ini = new IniParser(filePath);

            var config = new Config
            {
                WidgetsOrder = ini.GetList("Widgets", "Order"),

                AlbumCover = new AlbumCoverConfig
                {
                    Show = ini.GetBool("AlbumCover", "Show", true)
                },

                Infos = new InfosConfig
                {
                    Show = ini.GetBool("Infos", "Show", true),
                    Color = ini.Get("Infos", "Color", "White")
                },

                ProgressBar = new ProgressBarConfig
                {
                    Show = ini.GetBool("ProgressBar", "Show", true),
                    FilledSymbol = ini.Get("ProgressBar", "FilledSymbol", "━"),
                    EmptySymbol = ini.Get("ProgressBar", "EmptySymbol", "━"),
                    FilledColor = ini.Get("ProgressBar", "FilledColor", "Green"),
                    EmptyColor = ini.Get("ProgressBar", "EmptyColor", "DarkGray"),
                    Time = ini.GetBool("ProgressBar", "Time", true),
                    TimeColor = ini.Get("ProgressBar", "TimeColor", "Yellow")
                },

                Source = new SourceConfig
                {
                    Show = ini.GetBool("Source", "Show", false),
                    Color = ini.Get("Source", "Color", "DarkGrey"),
                }
            };

            return config;
        }
    }

    public class Config
    {
        public List<string> WidgetsOrder { get; set; } = new();
        public AlbumCoverConfig AlbumCover { get; set; } = new();
        public InfosConfig Infos { get; set; } = new();
        public ProgressBarConfig ProgressBar { get; set; } = new();
        public SourceConfig Source { get; set; } = new();
    }

    public class AlbumCoverConfig
    {
        public bool Show { get; set; } = true;
    }

    public class InfosConfig
    {
        public bool Show { get; set; } = true;
        public string Color { get; set; } = "White";
    }

    public class ProgressBarConfig
    {
        public bool Show { get; set; } = true;
        public string FilledSymbol { get; set; } = "━";
        public string EmptySymbol { get; set; } = "━";
        public string FilledColor { get; set; } = "Green";
        public string EmptyColor { get; set; } = "DarkGrey";
        public bool Time { get; set; } = true;
        public string TimeColor { get; set; } = "Yellow";
    }

    public class SourceConfig
    {
        public bool Show { get; set; } = false;
        public string Color { get; set; } = "DarkGrey";
    }
}
