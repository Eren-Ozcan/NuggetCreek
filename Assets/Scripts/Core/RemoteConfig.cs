using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Names a Remote Config key that differs from the field's automatic snake_case name,
    /// kept for keys the design doc (13.2) fixed before the field existed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RemoteKeyAttribute : Attribute
    {
        public string Key { get; }

        public RemoteKeyAttribute(string key) => Key = key;
    }

    /// <summary>What <see cref="RemoteConfig.Apply"/> did with each incoming key.</summary>
    public sealed class RemoteConfigResult
    {
        public List<string> Applied { get; } = new List<string>();

        /// <summary>Known keys whose value did not parse or changed an array's length.</summary>
        public List<string> Rejected { get; } = new List<string>();

        /// <summary>Keys no <see cref="EconomyConfig"/> field answers to (other systems read them).</summary>
        public List<string> Unknown { get; } = new List<string>();
    }

    /// <summary>
    /// Maps every public <see cref="EconomyConfig"/> field to one Remote Config key
    /// (design doc 13.2): numbers as invariant text, arrays as JSON-style lists
    /// ("[1,2,3]", enums by name). A bad value keeps the default, so a typo in the console
    /// never breaks a session. Arrays must keep their length because catalogs index them.
    /// </summary>
    public static class RemoteConfig
    {
        static readonly FieldInfo[] Fields = typeof(EconomyConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static string KeyFor(FieldInfo field)
        {
            var named = (RemoteKeyAttribute)Attribute.GetCustomAttribute(field, typeof(RemoteKeyAttribute));
            return named != null ? named.Key : SnakeCase(field.Name);
        }

        public static string SnakeCase(string name)
        {
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Every key with the config's current value, in field order.</summary>
        public static List<KeyValuePair<string, string>> Export(EconomyConfig config)
        {
            var list = new List<KeyValuePair<string, string>>(Fields.Length);
            foreach (FieldInfo field in Fields)
                list.Add(new KeyValuePair<string, string>(KeyFor(field), Format(field.GetValue(config))));
            return list;
        }

        public static RemoteConfigResult Apply(EconomyConfig config, IEnumerable<KeyValuePair<string, string>> values)
        {
            var byKey = new Dictionary<string, FieldInfo>(Fields.Length);
            foreach (FieldInfo field in Fields)
                byKey[KeyFor(field)] = field;

            var result = new RemoteConfigResult();
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!byKey.TryGetValue(pair.Key, out FieldInfo field))
                {
                    result.Unknown.Add(pair.Key);
                    continue;
                }
                if (TryParse(field.FieldType, pair.Value, field.GetValue(config), out object value))
                {
                    field.SetValue(config, value);
                    result.Applied.Add(pair.Key);
                }
                else
                {
                    result.Rejected.Add(pair.Key);
                }
            }
            return result;
        }

        /// <summary>One "key=value" line per entry; values never contain line breaks.</summary>
        public static string Serialize(IEnumerable<KeyValuePair<string, string>> values)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in values)
                sb.Append(pair.Key).Append('=').Append(pair.Value.Replace("\r", "").Replace("\n", "")).Append('\n');
            return sb.ToString();
        }

        public static List<KeyValuePair<string, string>> Deserialize(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text))
                return list;
            foreach (string line in text.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0)
                    list.Add(new KeyValuePair<string, string>(line.Substring(0, eq), line.Substring(eq + 1)));
            }
            return list;
        }

        static string Format(object value)
        {
            switch (value)
            {
                case double d:
                    return d.ToString("R", CultureInfo.InvariantCulture);
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case Enum e:
                    return e.ToString();
                case Array array:
                    var parts = new string[array.Length];
                    for (int k = 0; k < array.Length; k++)
                    {
                        object item = array.GetValue(k);
                        parts[k] = item is Enum ? "\"" + item + "\"" : Format(item);
                    }
                    return "[" + string.Join(",", parts) + "]";
                default:
                    throw new NotSupportedException("EconomyConfig field type " + value?.GetType());
            }
        }

        static bool TryParse(Type type, string text, object current, out object value)
        {
            value = null;
            if (text == null)
                return false;
            text = text.Trim();
            if (type.IsArray)
            {
                if (text.Length < 2 || text[0] != '[' || text[text.Length - 1] != ']')
                    return false;
                string body = text.Substring(1, text.Length - 2).Trim();
                string[] items = body.Length == 0 ? new string[0] : body.Split(',');
                var currentArray = (Array)current;
                if (currentArray != null && items.Length != currentArray.Length)
                    return false;
                Type element = type.GetElementType();
                Array array = Array.CreateInstance(element, items.Length);
                for (int k = 0; k < items.Length; k++)
                {
                    if (!TryParseScalar(element, items[k].Trim().Trim('"'), out object item))
                        return false;
                    array.SetValue(item, k);
                }
                value = array;
                return true;
            }
            return TryParseScalar(type, text, out value);
        }

        static bool TryParseScalar(Type type, string text, out object value)
        {
            value = null;
            if (type == typeof(double))
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                    return false;
                value = d;
                return true;
            }
            if (type == typeof(int))
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    return false;
                value = i;
                return true;
            }
            if (type == typeof(long))
            {
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return false;
                value = l;
                return true;
            }
            if (type.IsEnum)
            {
                foreach (string name in Enum.GetNames(type))
                {
                    if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                    {
                        value = Enum.Parse(type, name);
                        return true;
                    }
                }
                return false;
            }
            return false;
        }
    }
}
