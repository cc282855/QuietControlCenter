namespace ServiceLib.Common;

/// <summary>
/// Deterministic, offline country classification for profile labels and hosts.
/// Remark evidence always wins over the address ccTLD fallback.
/// </summary>
public static class CountryClassifier
{
    public const string AllCode = "";
    public const string UnknownCode = "ZZ";

    private sealed record Country(string Code, string EnglishName, string ChineseName, string[] Aliases);

    private static readonly Country[] Countries =
    [
        C("AE", "United Arab Emirates", "阿联酋", "UAE", "Dubai", "迪拜"),
        C("AR", "Argentina", "阿根廷"), C("AT", "Austria", "奥地利"), C("AU", "Australia", "澳大利亚", "澳洲"),
        C("BD", "Bangladesh", "孟加拉国", "孟加拉"), C("BE", "Belgium", "比利时"), C("BG", "Bulgaria", "保加利亚"),
        C("BR", "Brazil", "巴西"), C("CA", "Canada", "加拿大"), C("CH", "Switzerland", "瑞士"),
        C("CL", "Chile", "智利"), C("CN", "China", "中国", "Mainland China", "中国大陆", "大陆"),
        C("CO", "Colombia", "哥伦比亚"), C("CZ", "Czech Republic", "捷克", "Czechia"),
        C("DE", "Germany", "德国"), C("DK", "Denmark", "丹麦"), C("EG", "Egypt", "埃及"),
        C("ES", "Spain", "西班牙"), C("FI", "Finland", "芬兰"), C("FR", "France", "法国"),
        C("GB", "United Kingdom", "英国", "Great Britain", "Britain", "UK", "London", "伦敦"),
        C("GR", "Greece", "希腊"), C("HK", "Hong Kong", "香港", "Hongkong"), C("HR", "Croatia", "克罗地亚"),
        C("HU", "Hungary", "匈牙利"), C("ID", "Indonesia", "印度尼西亚", "印尼"),
        C("IE", "Ireland", "爱尔兰"), C("IL", "Israel", "以色列"), C("IN", "India", "印度"),
        C("IS", "Iceland", "冰岛"), C("IT", "Italy", "意大利"), C("JP", "Japan", "日本", "Tokyo", "东京", "Osaka", "大阪"),
        C("KE", "Kenya", "肯尼亚"), C("KH", "Cambodia", "柬埔寨"), C("KR", "South Korea", "韩国", "Korea", "首尔", "Seoul"),
        C("KZ", "Kazakhstan", "哈萨克斯坦", "哈萨克"), C("LA", "Laos", "老挝"), C("LK", "Sri Lanka", "斯里兰卡"),
        C("LU", "Luxembourg", "卢森堡"), C("MM", "Myanmar", "缅甸", "Burma"), C("MO", "Macao", "澳门", "Macau"),
        C("MX", "Mexico", "墨西哥"), C("MY", "Malaysia", "马来西亚", "马来"),
        C("NL", "Netherlands", "荷兰", "Holland"), C("NO", "Norway", "挪威"), C("NZ", "New Zealand", "新西兰"),
        C("PH", "Philippines", "菲律宾"), C("PK", "Pakistan", "巴基斯坦"), C("PL", "Poland", "波兰"),
        C("PT", "Portugal", "葡萄牙"), C("RO", "Romania", "罗马尼亚"), C("RS", "Serbia", "塞尔维亚"),
        C("RU", "Russia", "俄罗斯", "俄国", "Moscow", "莫斯科"), C("SA", "Saudi Arabia", "沙特阿拉伯", "沙特"),
        C("SE", "Sweden", "瑞典"), C("SG", "Singapore", "新加坡", "狮城"), C("SI", "Slovenia", "斯洛文尼亚"),
        C("SK", "Slovakia", "斯洛伐克"), C("TH", "Thailand", "泰国"), C("TR", "Turkey", "土耳其", "Türkiye"),
        C("TW", "Taiwan", "台湾", "台北", "Taipei"), C("UA", "Ukraine", "乌克兰"),
        C("US", "United States", "美国", "United States of America", "USA", "America", "洛杉矶", "Los Angeles", "纽约", "New York", "圣何塞", "San Jose", "西雅图", "Seattle"),
        C("VN", "Vietnam", "越南"), C("ZA", "South Africa", "南非")
    ];

    private static readonly IReadOnlyDictionary<string, Country> CountriesByCode =
        Countries.ToDictionary(country => country.Code, StringComparer.Ordinal);

    // ISO 3166-1 alpha-2. Keeping the canonical set separate from the alias
    // table lets flags and ccTLDs cover every country without a large name map.
    private static readonly HashSet<string> ValidCodes = new(
        ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ " +
        "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR " +
        "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP " +
        "KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT " +
        "MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW " +
        "SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG " +
        "UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    // These tokens are ordinary English words/identifiers often found in node
    // remarks. They require a flag, alias, or ccTLD instead of a bare ISO token.
    private static readonly HashSet<string> AmbiguousIsoCodes = new(
        ["AM", "AS", "AT", "BE", "BY", "DO", "ID", "IN", "IS", "IT", "LA", "ME", "MY", "NO", "SO", "TO"],
        StringComparer.Ordinal);

    private static readonly (Country Country, string Alias)[] Aliases = Countries
        .SelectMany(country => country.Aliases
            .Append(country.EnglishName)
            .Append(country.ChineseName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(alias => (country, alias)))
        .OrderByDescending(entry => entry.alias.Length)
        .ThenBy(entry => entry.country.Code, StringComparer.Ordinal)
        .ToArray();

    private static readonly Regex IsoCodePattern = new(
        @"(?<![A-Za-z0-9])(?<code>[A-Z]{2})(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static Country C(string code, string englishName, string chineseName, params string[] aliases)
    {
        return new(code, englishName, chineseName, aliases);
    }

    public static string Classify(string? remarks, string? address)
    {
        return FromFlag(remarks) ?? FromIsoCode(remarks) ?? FromAlias(remarks) ?? FromCcTld(address) ?? UnknownCode;
    }

    public static string NormalizeFilterCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return AllCode;
        }

        var normalized = code.Trim().ToUpperInvariant();
        return normalized == UnknownCode || ValidCodes.Contains(normalized) ? normalized : AllCode;
    }

    public static string GetDisplayName(string code)
    {
        if (code == AllCode)
        {
            return "所有国家/地区";
        }
        if (code == UnknownCode || !ValidCodes.Contains(code))
        {
            return "未知地区";
        }

        if (!CountriesByCode.TryGetValue(code, out var country))
        {
            return $"{ToFlag(code)} {code}";
        }

        return $"{code.CountryToEmoji() ?? ToFlag(code)} {country.ChineseName} ({code})";
    }

    public static IReadOnlyList<string> GetAvailableCodes<T>(
        IEnumerable<T> items,
        Func<T, string?> remarks,
        Func<T, string?> address)
    {
        var codes = items.Select(item => Classify(remarks(item), address(item))).Distinct(StringComparer.Ordinal).ToList();
        var hasUnknown = codes.Remove(UnknownCode);
        codes.Sort((left, right) => string.Compare(GetDisplayName(left), GetDisplayName(right), StringComparison.CurrentCulture));
        if (hasUnknown)
        {
            codes.Add(UnknownCode);
        }
        return codes;
    }

    public static IReadOnlyList<T> ApplyFilter<T>(
        IEnumerable<T> items,
        Func<T, string?> remarks,
        Func<T, string?> address,
        string? selectedCode)
    {
        var code = NormalizeFilterCode(selectedCode);
        return code == AllCode
            ? items.ToList()
            : items.Where(item => Classify(remarks(item), address(item)) == code).ToList();
    }

    private static string? FromFlag(string? remarks)
    {
        if (string.IsNullOrEmpty(remarks))
        {
            return null;
        }

        for (var index = 0; index < remarks.Length;)
        {
            if (!Rune.TryGetRuneAt(remarks, index, out var first))
            {
                index++;
                continue;
            }

            var nextIndex = index + first.Utf16SequenceLength;
            if (first.Value is >= 0x1F1E6 and <= 0x1F1FF
                && nextIndex < remarks.Length
                && Rune.TryGetRuneAt(remarks, nextIndex, out var second)
                && second.Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                var code = string.Create(2, (First: first.Value, Second: second.Value), static (span, pair) =>
                {
                    span[0] = (char)('A' + pair.First - 0x1F1E6);
                    span[1] = (char)('A' + pair.Second - 0x1F1E6);
                });
                if (ValidCodes.Contains(code))
                {
                    return code;
                }
            }

            index = nextIndex;
        }
        return null;
    }

    private static string? FromIsoCode(string? remarks)
    {
        if (string.IsNullOrEmpty(remarks))
        {
            return null;
        }
        foreach (Match match in IsoCodePattern.Matches(remarks))
        {
            var code = match.Groups["code"].Value;
            if (ValidCodes.Contains(code)
                && (!AmbiguousIsoCodes.Contains(code) || HasExplicitIsoContext(remarks, match)))
            {
                return code;
            }
        }
        return null;
    }

    private static bool HasExplicitIsoContext(string remarks, Match match)
    {
        var code = match.Groups["code"].Value;
        if (remarks.Trim().Equals(code, StringComparison.Ordinal))
        {
            return true;
        }

        var start = match.Index;
        var end = start + match.Length;
        if (start > 0 && end < remarks.Length && remarks[start - 1] == '[' && remarks[end] == ']')
        {
            return true;
        }

        var digitsAfterDelimiter = end + 1 < remarks.Length
            && IsLabelDelimiter(remarks[end])
            && char.IsDigit(remarks[end + 1]);
        var digitsBeforeDelimiter = start >= 2
            && IsLabelDelimiter(remarks[start - 1])
            && char.IsDigit(remarks[start - 2]);
        return digitsAfterDelimiter || digitsBeforeDelimiter;
    }

    private static bool IsLabelDelimiter(char value) => value is '-' or '_' or '#';

    private static string? FromAlias(string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return null;
        }
        foreach (var (country, alias) in Aliases)
        {
            var index = remarks.IndexOf(alias, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                if (ContainsCjk(alias) || HasTextBoundaries(remarks, index, alias.Length))
                {
                    return country.Code;
                }
                index = remarks.IndexOf(alias, index + alias.Length, StringComparison.OrdinalIgnoreCase);
            }
        }
        return null;
    }

    private static string? FromCcTld(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || IPAddress.TryParse(address.Trim('[', ']'), out _))
        {
            return null;
        }
        var host = address.Trim().TrimEnd('.');
        if (Uri.TryCreate(host.Contains("://", StringComparison.Ordinal) ? host : $"https://{host}", UriKind.Absolute, out var uri))
        {
            host = uri.IdnHost;
        }
        var lastDot = host.LastIndexOf('.');
        if (lastDot < 0 || lastDot == host.Length - 1)
        {
            return null;
        }
        var tld = host[(lastDot + 1)..].ToUpperInvariant();
        if (tld == "UK")
        {
            tld = "GB";
        }
        return ValidCodes.Contains(tld) ? tld : null;
    }

    private static bool ContainsCjk(string value)
    {
        return value.Any(character => character is >= '\u3400' and <= '\u9FFF');
    }

    private static bool HasTextBoundaries(string value, int index, int length)
    {
        var beforeIsText = index > 0 && char.IsLetterOrDigit(value[index - 1]);
        var afterIndex = index + length;
        var afterIsText = afterIndex < value.Length && char.IsLetterOrDigit(value[afterIndex]);
        return !beforeIsText && !afterIsText;
    }

    private static string ToFlag(string code)
    {
        return string.Concat(
        char.ConvertFromUtf32(0x1F1E6 + code[0] - 'A'),
        char.ConvertFromUtf32(0x1F1E6 + code[1] - 'A'));
    }
}
