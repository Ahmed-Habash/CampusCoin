using System.Globalization;
using System.Security.Claims;
using CampusCoin.Data;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public sealed class CurrencyService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    private static readonly IReadOnlyDictionary<string, string> Cultures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = "en-US", ["QAR"] = "ar-QA", ["EUR"] = "fr-FR", ["GBP"] = "en-GB",
        ["AED"] = "ar-AE", ["SAR"] = "ar-SA", ["PKR"] = "en-PK", ["INR"] = "en-IN",
        ["CAD"] = "en-CA", ["AUD"] = "en-AU"
    };

    private string _adminMode = "Fixed";
    private Dictionary<string, string> _userCodes = new(StringComparer.Ordinal);

    public string Code { get; private set; } = "USD";
    public string Symbol => CreateCulture(Code).NumberFormat.CurrencySymbol;
    /// <summary>Fixed = always use the signed-in user's display currency. PerUser = use each subject's currency when formatting for them.</summary>
    public string AdminCurrencyMode => _adminMode;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var http = accessor.HttpContext;
        var userId = http?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = http?.User.IsInRole("Administrator") == true;

        string? selected = null;
        string adminMode = "Fixed";
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var row = await db.UserSettings.AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new { x.CurrencyCode, x.AdminCurrencyMode })
                .SingleOrDefaultAsync(cancellationToken);
            selected = row?.CurrencyCode;
            adminMode = string.Equals(row?.AdminCurrencyMode, "PerUser", StringComparison.OrdinalIgnoreCase) ? "PerUser" : "Fixed";
        }

        Code = selected is null or "Auto" ? InferFromRequest(http) : Normalize(selected);
        _adminMode = isAdmin ? adminMode : "Fixed";
        _userCodes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_adminMode == "PerUser")
        {
            var rows = await db.UserSettings.AsNoTracking()
                .Select(x => new { x.UserId, x.CurrencyCode })
                .ToListAsync(cancellationToken);
            foreach (var r in rows)
                _userCodes[r.UserId] = r.CurrencyCode;
        }
    }

    public string Format(decimal value) => FormatWithCode(Code, value);
    public string Format(long cents) => Format(cents / 100m);

    /// <summary>Formats money for a specific user when admin mode is PerUser; otherwise uses the viewer’s currency.</summary>
    public string FormatForUser(string? userId, decimal value)
    {
        if (_adminMode != "PerUser" || string.IsNullOrWhiteSpace(userId))
            return Format(value);
        return FormatWithCode(ResolveUserCode(userId), value);
    }

    public string FormatForUser(string? userId, long cents) => FormatForUser(userId, cents / 100m);

    public string CodeForUser(string? userId)
    {
        if (_adminMode != "PerUser" || string.IsNullOrWhiteSpace(userId))
            return Code;
        return ResolveUserCode(userId);
    }

    private string ResolveUserCode(string userId)
    {
        if (!_userCodes.TryGetValue(userId, out var raw) || string.IsNullOrWhiteSpace(raw) || raw == "Auto")
            return Code; // fall back to admin/viewer display currency
        return Normalize(raw);
    }

    private static string FormatWithCode(string code, decimal value) =>
        value.ToString("C2", CreateCulture(code));

    private static string InferFromRequest(HttpContext? http)
    {
        var language = http?.Request.GetTypedHeaders().AcceptLanguage?.FirstOrDefault()?.Value.Value;
        try
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                var culture = CultureInfo.GetCultureInfo(language);
                if (!culture.IsNeutralCulture) return Normalize(new RegionInfo(culture.Name).ISOCurrencySymbol);
            }
        }
        catch (CultureNotFoundException) { }
        return "USD";
    }

    private static string Normalize(string code) => Cultures.ContainsKey(code) ? code.ToUpperInvariant() : "USD";
    private static CultureInfo CreateCulture(string code)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(Cultures.TryGetValue(code, out var name) ? name : "en-US").Clone();
        culture.NumberFormat.CurrencyDecimalDigits = 2;
        return culture;
    }
}
