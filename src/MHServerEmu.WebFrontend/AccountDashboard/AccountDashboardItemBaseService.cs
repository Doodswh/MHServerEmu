using System.Collections.ObjectModel;
using System.Data.SQLite;

namespace MHServerEmu.WebFrontend.AccountDashboard
{
    internal static class AccountDashboardItemBaseService
    {
        private static readonly Lazy<ItemBaseIndex> ItemBaseLookup = new(LoadIndex, LazyThreadSafetyMode.ExecutionAndPublication);

        public static bool TryGetItem(string prototypeName, out ItemBaseItem item)
        {
            item = null;

            ItemBaseIndex index = ItemBaseLookup.Value;
            if (index.IsAvailable == false)
                return false;

            if (string.IsNullOrWhiteSpace(prototypeName) == false)
            {
                if (index.ByPrototype.TryGetValue(NormalizePrototypePath(prototypeName), out item))
                    return true;

                string prototypeKey = NormalizePrototypeKey(prototypeName);
                if (string.IsNullOrWhiteSpace(prototypeKey) == false && index.ByPrototypeKey.TryGetValue(prototypeKey, out item))
                    return true;
            }

            return false;
        }

        private static ItemBaseIndex LoadIndex()
        {
            string databasePath = Path.Combine(AppContext.BaseDirectory, "Data", "ItemBase", "itembase.sqlite");
            if (File.Exists(databasePath) == false)
                return ItemBaseIndex.Empty;

            Dictionary<string, ItemBaseItem> byPrototype = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ItemBaseItem> byPrototypeKey = new(StringComparer.OrdinalIgnoreCase);

            using SQLiteConnection connection = new($"Data Source={databasePath};Version=3;Read Only=True;");
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT item_id, name, prototype, prototype_key, icon_url, tooltip_html, tooltip_text, description_text, type_name, required_level, item_grade FROM items";

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ItemBaseItem item = new()
                {
                    ItemId = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Prototype = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PrototypeKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IconUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    TooltipHtml = reader.IsDBNull(5) ? null : reader.GetString(5),
                    TooltipText = reader.IsDBNull(6) ? null : reader.GetString(6),
                    DescriptionText = reader.IsDBNull(7) ? null : reader.GetString(7),
                    TypeName = reader.IsDBNull(8) ? null : reader.GetString(8),
                    RequiredLevelText = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ItemGradeText = reader.IsDBNull(10) ? null : reader.GetString(10),
                };

                string normalizedPrototype = NormalizePrototypePath(item.Prototype);
                if (string.IsNullOrWhiteSpace(normalizedPrototype) == false && byPrototype.ContainsKey(normalizedPrototype) == false)
                    byPrototype[normalizedPrototype] = item;

                string normalizedPrototypeKey = NormalizePrototypeKey(item.PrototypeKey);
                if (string.IsNullOrWhiteSpace(normalizedPrototypeKey) == false && byPrototypeKey.ContainsKey(normalizedPrototypeKey) == false)
                    byPrototypeKey[normalizedPrototypeKey] = item;
            }

            return new ItemBaseIndex(byPrototype, byPrototypeKey);
        }

        private static string NormalizePrototypePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Replace('\\', '/').Trim();
        }

        private static string NormalizePrototypeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Replace('\\', '/').Trim();
            int slashIndex = value.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < value.Length - 1)
                value = value[(slashIndex + 1)..];

            if (value.EndsWith(".prototype", StringComparison.OrdinalIgnoreCase))
                value = value[..^".prototype".Length];

            return value.Trim().ToLowerInvariant();
        }

        private sealed class ItemBaseIndex
        {
            public static ItemBaseIndex Empty { get; } = new(new Dictionary<string, ItemBaseItem>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, ItemBaseItem>(StringComparer.OrdinalIgnoreCase));

            public IReadOnlyDictionary<string, ItemBaseItem> ByPrototype { get; }
            public IReadOnlyDictionary<string, ItemBaseItem> ByPrototypeKey { get; }
            public bool IsAvailable => ByPrototype.Count > 0 || ByPrototypeKey.Count > 0;

            public ItemBaseIndex(Dictionary<string, ItemBaseItem> byPrototype, Dictionary<string, ItemBaseItem> byPrototypeKey)
            {
                ByPrototype = new ReadOnlyDictionary<string, ItemBaseItem>(byPrototype);
                ByPrototypeKey = new ReadOnlyDictionary<string, ItemBaseItem>(byPrototypeKey);
            }
        }

        internal sealed class ItemBaseItem
        {
            public int ItemId { get; init; }
            public string Name { get; init; }
            public string Prototype { get; init; }
            public string PrototypeKey { get; init; }
            public string IconUrl { get; init; }
            public string TooltipHtml { get; init; }
            public string TooltipText { get; init; }
            public string DescriptionText { get; init; }
            public string TypeName { get; init; }
            public string RequiredLevelText { get; init; }
            public string ItemGradeText { get; init; }
        }
    }
}
