using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace CampusCoin.Services;

public class AssistantService(ApplicationDbContext db, IConfiguration configuration, IHttpClientFactory httpClientFactory, IDataProtectionProvider protection, CurrencyService currency, IHostEnvironment env, ILogger<AssistantService> logger)
{
    public async Task<AssistantResult> AskAsync(string userId, string question, CancellationToken cancellationToken = default)
    {
        var context = await BuildContextAsync(userId, cancellationToken);
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var provider = settings?.AiProvider ?? "Auto";
        string? personalKey = null;
        if (!string.IsNullOrWhiteSpace(settings?.EncryptedApiKey))
        {
            try { personalKey = protection.CreateProtector("CampusCoin.UserAiKeys.v1").Unprotect(settings.EncryptedApiKey); }
            catch (CryptographicException) { }
        }
        var apiLimitReached = false;

        if (provider != "Local" && provider != "OpenAI")
        {
            var geminiKeys = ResolveGeminiKeys(personalKey, provider);
            for (var i = 0; i < geminiKeys.Count; i++)
            {
                try
                {
                    var answer = await AskGeminiAsync(geminiKeys[i], context, question, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(answer))
                        return new AssistantResult(answer.Trim(), geminiKeys.Count > 1 ? $"AI · Gemini (key {i + 1})" : "AI · Gemini");
                    logger.LogWarning("Gemini key {Index} returned an empty answer; trying next key if available.", i + 1);
                }
                catch (HttpRequestException ex)
                {
                    var limited = ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                        || ex.Message.Contains("429")
                        || ex.Message.Contains("RESOURCE_EXHAUSTED");
                    if (limited) apiLimitReached = true;
                    logger.LogWarning(ex, "Gemini key {Index} failed for provider {Provider}; trying next key if available.", i + 1, provider);
                }
                catch (JsonException ex) { logger.LogWarning(ex, "Gemini key {Index} returned an unreadable response", i + 1); }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Gemini key {Index} timed out; trying next key if available.", i + 1);
                }
            }
        }

        if (provider != "Local" && provider != "Gemini")
        {
            var openAiKeys = ResolveOpenAiKeys(personalKey, provider);
            for (var i = 0; i < openAiKeys.Count; i++)
            {
                try
                {
                    var answer = await AskOpenAiAsync(openAiKeys[i], context, question, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(answer))
                        return new AssistantResult(answer.Trim(), openAiKeys.Count > 1 ? $"AI · OpenAI (key {i + 1})" : "AI · OpenAI");
                }
                catch (HttpRequestException ex)
                {
                    var limited = ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                        || ex.Message.Contains("429")
                        || ex.Message.Contains("insufficient_quota");
                    if (limited) apiLimitReached = true;
                    logger.LogWarning(ex, "OpenAI key {Index} failed for provider {Provider}; trying next key if available.", i + 1, provider);
                }
                catch (JsonException ex) { logger.LogWarning(ex, "OpenAI key {Index} returned an unreadable response", i + 1); }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "OpenAI key {Index} timed out; trying next key if available.", i + 1);
                }
            }
        }

        if (apiLimitReached)
        {
            return new AssistantResult("API usage limit reached for the configured AI provider keys. Campus Coin will keep trying your backup keys on the next question, or you can update keys in Settings / .keys.", "API Limit Exceeded");
        }

        return new AssistantResult(LocalAnswer(context, question, currency), "Campus Coin Assistant");
    }

    private List<string> ResolveGeminiKeys(string? personalKey, string provider)
    {
        var keys = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (!keys.Contains(trimmed, StringComparer.Ordinal)) keys.Add(trimmed);
        }

        // Personal key first when user chose Gemini or Auto.
        if (provider is "Gemini" or "Auto") Add(personalKey);

        foreach (var value in configuration.GetSection("Gemini:ApiKeys").GetChildren().Select(x => x.Value))
            Add(value);
        Add(configuration["Gemini:ApiKey"]);

        var envMany = Environment.GetEnvironmentVariable("GEMINI_API_KEYS");
        if (!string.IsNullOrWhiteSpace(envMany))
        {
            foreach (var part in envMany.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Add(part);
        }
        Add(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

        // Local fallback file (gitignored): .keys/gemini-keys.txt
        try
        {
            var path = Path.Combine(env.ContentRootPath, ".keys", "gemini-keys.txt");
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#')) continue;
                    Add(t);
                }
            }
        }
        catch (IOException ex) { logger.LogWarning(ex, "Could not read .keys/gemini-keys.txt"); }

        return keys;
    }

    private List<string> ResolveOpenAiKeys(string? personalKey, string provider)
    {
        var keys = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (!keys.Contains(trimmed, StringComparer.Ordinal)) keys.Add(trimmed);
        }

        if (provider is "OpenAI" or "Auto") Add(personalKey);
        foreach (var value in configuration.GetSection("OpenAI:ApiKeys").GetChildren().Select(x => x.Value))
            Add(value);
        Add(configuration["OpenAI:ApiKey"]);
        var envMany = Environment.GetEnvironmentVariable("OPENAI_API_KEYS");
        if (!string.IsNullOrWhiteSpace(envMany))
        {
            foreach (var part in envMany.Split([',', ';', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                Add(part);
        }
        Add(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        return keys;
    }

    private async Task<AssistantContext> BuildContextAsync(string userId, CancellationToken cancellationToken)
    {
        var month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var nextMonth = month.AddMonths(1);
        var previousMonth = month.AddMonths(-1);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, cancellationToken);
        var transactions = await db.Transactions.AsNoTracking().Include(x => x.Category)
            .Where(x => x.UserId == userId && !x.IsDeleted && x.Date >= previousMonth.AddYears(-1) && x.Date < nextMonth)
            .ToListAsync(cancellationToken);
        var budgets = await db.Budgets.AsNoTracking().Include(x => x.Category).Where(x => x.UserId == userId && x.Month == month).ToListAsync(cancellationToken);
        var recurring = await db.RecurringTransactions.AsNoTracking().Include(x => x.Category).Where(x => x.UserId == userId && x.IsActive).ToListAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking()
            .Where(x => !x.IsArchived && (x.UserId == null || x.UserId == userId))
            .OrderBy(x => x.Type).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var goals = await db.SavingsGoals.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        var xp = await db.UserXpProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var memberships = await db.GroupMembers.AsNoTracking().Include(m => m.Group)
            .Where(m => m.UserId == userId && !m.Group.IsDisabled)
            .ToListAsync(cancellationToken);
        var groupIds = memberships.Select(m => m.GroupId).ToList();
        var shared = groupIds.Count == 0
            ? new List<SharedExpense>()
            : await db.SharedExpenses.AsNoTracking()
                .Include(e => e.Participants)
                .Where(e => groupIds.Contains(e.GroupId))
                .OrderByDescending(e => e.Date)
                .Take(40)
                .ToListAsync(cancellationToken);
        var announcements = await db.Announcements.AsNoTracking()
            .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);
        return new AssistantContext(
            user.FullName,
            user.Email ?? "",
            user.AcademicYear ?? "",
            month,
            user.AllowanceCents / 100m,
            user.SavingsGoalCents / 100m,
            transactions,
            budgets,
            recurring,
            categories,
            goals,
            memberships,
            shared,
            announcements,
            xp?.Level ?? 1,
            xp?.Title ?? "Starter",
            xp?.TotalXp ?? 0);
    }

    private async Task<string?> AskGeminiAsync(string key, AssistantContext context, string question, CancellationToken cancellationToken)
    {
        // Prefer Flash-Lite — lowest credit cost for account Q&A.
        var model = configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite";
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt(currency.Code) } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = UserMessage(context, question, currency.Code) } } } },
            generationConfig = new
            {
                maxOutputTokens = 512,
                temperature = 0.2,
                // Disable thinking tokens to keep credit use low.
                thinkingConfig = new { thinkingBudget = 0 }
            }
            // Intentionally no googleSearch / grounding tools — answers must stay on this account + Campus Coin.
        };
        var serialized = JsonSerializer.Serialize(payload);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
            request.Headers.Add("x-goog-api-key", key);
            request.Content = new StringContent(serialized, Encoding.UTF8, "application/json");
            using var response = await httpClientFactory.CreateClient("Gemini").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                if (attempt < 2 && ((int)response.StatusCode == 429 || (int)response.StatusCode == 503))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), cancellationToken);
                    continue;
                }
                logger.LogWarning("Gemini returned HTTP {Status}. Response: {Detail}", (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
                response.EnsureSuccessStatusCode();
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonNode.Parse(body);
            var answer = json?["candidates"]?[0]?["content"]?["parts"]?.AsArray()
                ?.Where(part => part?["thought"]?.GetValue<bool>() != true)
                .Select(part => part?["text"]?.GetValue<string>())
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            if (string.IsNullOrWhiteSpace(answer))
                logger.LogWarning("Gemini returned an empty answer. FinishReason={FinishReason}. Body={Detail}",
                    json?["candidates"]?[0]?["finishReason"]?.GetValue<string>(),
                    body.Length > 500 ? body[..500] : body);
            return answer;
        }
        return null;
    }

    private async Task<string?> AskOpenAiAsync(string key, AssistantContext context, string question, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = configuration["OpenAI:Model"] ?? "gpt-4.1-nano",
            store = false,
            instructions = SystemPrompt(currency.Code),
            input = UserMessage(context, question, currency.Code),
            max_output_tokens = 450,
            temperature = 0.2
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await httpClientFactory.CreateClient("OpenAI").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("OpenAI returned HTTP {Status}. Response: {Detail}", (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
            response.EnsureSuccessStatusCode();
        }
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var output = json?["output"]?.AsArray();
        if (output == null) return null;
        return string.Join("\n", output.SelectMany(item => item?["content"]?.AsArray() ?? [])
            .Where(item => item?["type"]?.GetValue<string>() == "output_text")
            .Select(item => item?["text"]?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string SystemPrompt(string code) =>
        "You are Ask Coin, the in-app assistant for Campus Coin — a student budgeting website. " +
        "Your ONLY job is to answer from the signed-in student's Campus Coin account data and the Campus Coin product guide provided in the message. " +
        "Rules you must follow: " +
        "1) Use ONLY the ACCOUNT DATA block for numbers, balances, transactions, budgets, categories, goals, groups, XP, and announcements. " +
        "2) Use ONLY the CAMPUS COIN PRODUCT GUIDE for how-to / where-to-click questions about this website. " +
        "3) Do NOT use general web knowledge, Google facts, news, unrelated finance advice, or invent transactions, amounts, or features. " +
        "4) If the account data does not contain enough detail, say what is missing and suggest the Campus Coin page to check (Transactions, Budgets, Groups, Insights, Settings). " +
        "5) If the question is outside Campus Coin / this account (e.g. world news, homework, other apps), politely refuse and invite an account or Campus Coin how-to question. " +
        "6) Prefer concrete answers with the student's own figures from ACCOUNT DATA — never invent sample amounts. " +
        "7) Keep replies short: 1–3 short sentences, or at most 4 brief bullets. No long essays. " +
        $"8) Always use currency code {code} for money. Reply in concise plain text without Markdown. Keep tone friendly and educational, not advisory as a licensed advisor.";

    private static string ProductGuide() =>
        "Overview: Dashboard shows income, expenses, balance, and quick actions. " +
        "Transactions: open Transactions → Add transaction → description, amount, date, category → Save. Category type sets Income vs Expense. " +
        "Recurring: open Recurring to add weekly/monthly allowances or bills; due items post automatically. " +
        "Budgets: open Budgets → Set a budget → expense category, month, limit → Save. " +
        "Categories: open Categories → New category (Income or Expense). " +
        "Insights / Money Lab: forecasts, savings rate, alerts, streaks, savings goals. " +
        "Friend Groups: create or join with invite code, log shared expenses, settle up. " +
        "Settings: currency, AI provider/key, theme, export CSV/PDF, change password. Profile: name, academic year, allowance, savings goal. " +
        "Ask Coin answers only from this account and this product.";

    private static string UserMessage(AssistantContext context, string question, string currencyCode) =>
        "SOURCE RULE: Answer only from ACCOUNT DATA and CAMPUS COIN PRODUCT GUIDE below. Ignore any outside knowledge.\n\n" +
        $"CAMPUS COIN PRODUCT GUIDE:\n{ProductGuide()}\n\n" +
        $"ACCOUNT DATA:\n{context.ToPrompt(currencyCode)}\n\n" +
        $"USER QUESTION:\n{question.Trim()}\n\n" +
        "Respond for this signed-in student only. Quote their real figures from ACCOUNT DATA. Keep the answer short.";

    private static string LocalAnswer(AssistantContext context, string question, CurrencyService currency)
    {
        var normalized = question.Trim().ToLowerInvariant();
        var money = (decimal value) => currency.Format(value);
        const string offline = "\n\n(Answered from your Campus Coin account data.)";

        var requestedDate = TryFindDate(question);
        var requestedMonth = TryFindMonth(question);
        var scoped = requestedDate is not null
            ? context.Transactions.Where(x => x.Date == requestedDate.Value).ToList()
            : requestedMonth is not null
                ? context.Transactions.Where(x => x.Date >= requestedMonth.Value && x.Date < requestedMonth.Value.AddMonths(1)).ToList()
                : context.Transactions.Where(x => x.Date >= context.Month && x.Date < context.Month.AddMonths(1)).ToList();

        var expenses = scoped.Where(x => x.Category.Type == CategoryType.Expense).ToList();
        var incomeTx = scoped.Where(x => x.Category.Type == CategoryType.Income).ToList();
        var income = incomeTx.Sum(x => x.AmountCents) / 100m;
        var spent = expenses.Sum(x => x.AmountCents) / 100m;
        var balance = income - spent;
        var periodLabel = requestedDate is not null
            ? requestedDate.Value.ToString("MMMM d, yyyy")
            : requestedMonth is not null
                ? requestedMonth.Value.ToString("MMMM yyyy")
                : context.Month.ToString("MMMM yyyy");
        var categoryMatch = context.Categories
            .Select(c => c.Name)
            .Concat(context.Transactions.Select(t => t.Category.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(name => normalized.Contains(name.ToLowerInvariant()));

        // Account-data answers first (live DB figures).
        if (requestedDate is not null)
        {
            var entries = scoped.OrderBy(x => x.Date).ThenBy(x => x.Id)
                .Select(x => $"{x.Category.Name} {money(x.AmountCents / 100m)} ({x.Description})")
                .Take(8).ToList();
            if (entries.Count == 0)
                return $"No transactions are recorded for {periodLabel} in your account." + offline;
            return $"On {periodLabel} your account shows {money(income)} income and {money(spent)} expenses ({entries.Count} entries). Examples: {string.Join("; ", entries)}." + offline;
        }

        if (normalized.Contains("balance") || normalized.Contains("left") || normalized.Contains("remaining") || normalized.Contains("net"))
            return $"From your account for {periodLabel}: balance {money(balance)} (income {money(income)} − expenses {money(spent)})." + offline;

        if (normalized.Contains("income") || normalized.Contains("earn") || normalized.Contains("money in"))
        {
            var topIn = incomeTx.GroupBy(x => x.Category.Name).OrderByDescending(g => g.Sum(x => x.AmountCents)).Take(3)
                .Select(g => $"{g.Key} {money(g.Sum(x => x.AmountCents) / 100m)}");
            return income <= 0
                ? $"Your account has no income recorded for {periodLabel} yet." + offline
                : $"Your account income for {periodLabel} is {money(income)}" + (topIn.Any() ? $". Top sources: {string.Join(", ", topIn)}." : ".") + offline;
        }

        if (normalized.Contains("spend") || normalized.Contains("expense") || normalized.Contains("money out") || normalized.Contains("outgo"))
        {
            if (categoryMatch != null)
            {
                var categoryTotal = expenses.Where(x => x.Category.Name.Equals(categoryMatch, StringComparison.OrdinalIgnoreCase)).Sum(x => x.AmountCents) / 100m;
                return $"Your account shows {money(categoryTotal)} spent on {categoryMatch} in {periodLabel}." + offline;
            }
            var top = expenses.GroupBy(x => x.Category.Name).OrderByDescending(g => g.Sum(x => x.AmountCents)).Take(3)
                .Select(g => $"{g.Key} {money(g.Sum(x => x.AmountCents) / 100m)}");
            return spent <= 0
                ? $"Your account has no expenses recorded for {periodLabel} yet." + offline
                : $"Your account spending for {periodLabel} is {money(spent)}" + (top.Any() ? $". Largest categories: {string.Join(", ", top)}." : ".") + offline;
        }

        if (normalized.Contains("transaction") && (normalized.Contains("recent") || normalized.Contains("last") || normalized.Contains("list") || normalized.Contains("show")))
        {
            var recent = context.Transactions.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(5)
                .Select(x => $"{x.Date:MMM d}: {x.Description} · {x.Category.Name} {money(x.AmountCents / 100m)}");
            return recent.Any()
                ? $"Latest transactions in your account: {string.Join(" | ", recent)}." + offline
                : "Your account has no transactions yet. Add one from Transactions." + offline;
        }

        if (normalized.Contains("budget") || normalized.Contains("limit"))
        {
            if (context.Budgets.Count == 0)
                return "Your account has no budgets set for this month. Open Budgets to add one." + offline;
            var lines = context.Budgets.Select(b =>
            {
                var used = expenses.Where(x => x.CategoryId == b.CategoryId).Sum(x => x.AmountCents) / 100m;
                var limit = b.LimitCents / 100m;
                return $"{b.Category.Name}: {money(used)} of {money(limit)}";
            });
            return $"Budgets from your account this month — {string.Join("; ", lines)}." + offline;
        }

        if (normalized.Contains("save") || normalized.Contains("goal"))
        {
            if (context.Goals.Count > 0)
            {
                var lines = context.Goals.Take(4).Select(g =>
                    $"{g.Name}: {money(g.SavedCents / 100m)} of {money(g.TargetCents / 100m)}" + (g.IsComplete ? " (done)" : ""));
                return $"Savings goals in your account: {string.Join("; ", lines)}. This month’s recorded balance is {money(balance)}." + offline;
            }
            return context.SavingsGoal > 0
                ? $"Your profile savings goal is {money(context.SavingsGoal)}. This month’s recorded balance is {money(balance)}." + offline
                : "Your account has no savings goal yet. Add one from Profile or Money Lab." + offline;
        }

        if (normalized.Contains("recurring") || normalized.Contains("subscription") || normalized.Contains("routine"))
        {
            if (context.Recurring.Count == 0)
                return "Your account has no active recurring items. Add one from Recurring." + offline;
            var lines = context.Recurring.Take(5).Select(r =>
                $"{r.Description}: {money(r.AmountCents / 100m)} {r.Frequency}, next {r.NextRunDate:MMM d}");
            return $"Active recurring items in your account: {string.Join("; ", lines)}." + offline;
        }

        if (normalized.Contains("group") || normalized.Contains("roommate") || normalized.Contains("settle") || normalized.Contains("owe") || normalized.Contains("split"))
        {
            if (context.Memberships.Count == 0)
                return "Your account is not in any friend groups yet. Open Roommate & Friend Groups to create or join one." + offline;
            var names = context.Memberships.Select(m => $"{m.Group.Name} ({m.Role})");
            var sharedBits = context.SharedExpenses.Take(3).Select(e =>
                $"{e.Date:MMM d} {e.Description} {money(e.AmountCents / 100m)}");
            return $"Your groups: {string.Join(", ", names)}." +
                   (sharedBits.Any() ? $" Recent shared expenses: {string.Join("; ", sharedBits)}." : " No shared expenses logged yet.") + offline;
        }

        if (normalized.Contains("categor"))
        {
            var expenseCats = context.Categories.Where(c => c.Type == CategoryType.Expense).Select(c => c.Name).Take(8);
            var incomeCats = context.Categories.Where(c => c.Type == CategoryType.Income).Select(c => c.Name).Take(8);
            return $"Categories on your account — Income: {(incomeCats.Any() ? string.Join(", ", incomeCats) : "none")}; Expense: {(expenseCats.Any() ? string.Join(", ", expenseCats) : "none")}." + offline;
        }

        if (normalized.Contains("xp") || normalized.Contains("level") || normalized.Contains("streak"))
            return $"Your Campus Coin XP profile: level {context.XpLevel} ({context.XpTitle}), {context.TotalXp} total XP." + offline;

        // Short product how-tos only when the question is clearly about using the site (not account numbers).
        var asksHow = normalized.Contains("how") || normalized.Contains("where") || normalized.Contains("steps") || normalized.Contains("can i");
        if (asksHow && normalized.Contains("transaction"))
            return "On Campus Coin: open Transactions → Add transaction → enter description, amount, date, and category → Save. Your totals then update from that saved record.";
        if (asksHow && normalized.Contains("budget"))
            return "On Campus Coin: open Budgets → Set a budget → pick an expense category, month, and limit → Save. Ask Coin then reads those limits from your account.";
        if (asksHow && (normalized.Contains("group") || normalized.Contains("join")))
            return "On Campus Coin: open Roommate & Friend Groups → Create New Group, or Join via Code with an 8-character invite.";
        if (normalized.Contains("export") || normalized.Contains("csv") || normalized.Contains("pdf"))
            return "On Campus Coin: export from Settings or Transactions — CSV for a spreadsheet, PDF for a printable report.";
        if (asksHow && normalized.Contains("password"))
            return "On Campus Coin: Settings → Change password → enter current password, then a new password (8+ characters).";

        return $"I answer from your Campus Coin account. For {periodLabel} your data shows income {money(income)}, expenses {money(spent)}, balance {money(balance)}. Ask about spending, budgets, transactions, goals, groups, or how to use a page.";
    }


    private static DateOnly? TryFindDate(string question)
    {
        var iso = Regex.Match(question, @"\b(20\d{2})[-/](\d{1,2})[-/](\d{1,2})\b");
        if (iso.Success && DateOnly.TryParse($"{iso.Groups[1].Value}-{iso.Groups[2].Value}-{iso.Groups[3].Value}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact;
        var natural = Regex.Match(question, @"\b(?:on\s+)?([A-Za-z]{3,9}\s+\d{1,2}(?:st|nd|rd|th)?(?:,?\s+20\d{2})?)\b", RegexOptions.IgnoreCase);
        var value = natural.Success ? Regex.Replace(natural.Groups[1].Value, @"(\d+)(st|nd|rd|th)", "$1", RegexOptions.IgnoreCase) : string.Empty;
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
    }

    private static DateOnly? TryFindMonth(string question)
    {
        var match = Regex.Match(question, @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(20\d{2})\b", RegexOptions.IgnoreCase);
        return match.Success && DateTime.TryParseExact($"{match.Groups[1].Value} {match.Groups[2].Value}", "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ? new DateOnly(month.Year, month.Month, 1) : null;
    }

    private sealed record AssistantContext(
        string Name,
        string Email,
        string AcademicYear,
        DateOnly Month,
        decimal Allowance,
        decimal SavingsGoal,
        List<Transaction> Transactions,
        List<Budget> Budgets,
        List<RecurringTransaction> Recurring,
        List<Category> Categories,
        List<SavingsGoal> Goals,
        List<GroupMember> Memberships,
        List<SharedExpense> SharedExpenses,
        List<Announcement> Announcements,
        int XpLevel,
        string XpTitle,
        int TotalXp)
    {
        public string ToPrompt(string currencyCode)
        {
            var monthTx = Transactions.Where(x => x.Date >= Month && x.Date < Month.AddMonths(1)).ToList();
            var monthIncome = monthTx.Where(x => x.Category.Type == CategoryType.Income).Sum(x => x.AmountCents) / 100m;
            var monthExpense = monthTx.Where(x => x.Category.Type == CategoryType.Expense).Sum(x => x.AmountCents) / 100m;
            var topCats = monthTx.Where(x => x.Category.Type == CategoryType.Expense)
                .GroupBy(x => x.Category.Name)
                .OrderByDescending(g => g.Sum(x => x.AmountCents))
                .Take(5)
                .Select(g => $"- {g.Key}: {currencyCode} {g.Sum(x => x.AmountCents) / 100m:0.00}");
            var lines = Transactions.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(80)
                .Select(x => $"- {x.Date:yyyy-MM-dd}: {x.Category.Type} / {x.Category.Name} {currencyCode} {x.AmountCents / 100m:0.00} ({x.Description})");
            var budgets = Budgets.Select(x =>
            {
                var spent = monthTx.Where(t => t.CategoryId == x.CategoryId && t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
                return $"- {x.Category.Name}: limit {currencyCode} {x.LimitCents / 100m:0.00}, spent {currencyCode} {spent:0.00}";
            });
            var routines = Recurring.Select(x => $"- {x.Description}: {currencyCode} {x.AmountCents / 100m:0.00} {x.Frequency} via {x.Category.Name}, next {x.NextRunDate:yyyy-MM-dd}, active={x.IsActive}");
            var cats = Categories.Select(x => $"- {x.Type}: {x.Name}{(x.UserId == null ? " (system)" : " (personal)")}");
            var goals = Goals.Select(x => $"- {x.Name}: saved {currencyCode} {x.SavedCents / 100m:0.00} of {currencyCode} {x.TargetCents / 100m:0.00}" +
                (x.Deadline is null ? "" : $", deadline {x.Deadline:yyyy-MM-dd}") + (x.IsComplete ? " (complete)" : ""));
            var groups = Memberships.Select(m => $"- {m.Group.Name} as {m.Role}");
            var shared = SharedExpenses.Take(25).Select(e =>
                $"- {e.Date:yyyy-MM-dd} group#{e.GroupId}: {e.Description} {currencyCode} {e.AmountCents / 100m:0.00} ({e.Category}), participants={e.Participants.Count}");
            var news = Announcements.Select(a => $"- {a.Title}: {a.Message}");
            return string.Join("\n", new[]
            {
                $"Student name: {Name}",
                $"Email: {Email}",
                $"Academic year: {(string.IsNullOrWhiteSpace(AcademicYear) ? "not set" : AcademicYear)}",
                $"Currency: {currencyCode}",
                $"Current month: {Month:MMMM yyyy}",
                $"Allowance baseline: {currencyCode} {Allowance:0.00}",
                $"Profile savings goal: {currencyCode} {SavingsGoal:0.00}",
                $"XP: level {XpLevel} ({XpTitle}), total XP {TotalXp}",
                $"This month income total: {currencyCode} {monthIncome:0.00}",
                $"This month expense total: {currencyCode} {monthExpense:0.00}",
                $"This month net balance: {currencyCode} {monthIncome - monthExpense:0.00}",
                "This month top expense categories:",
                topCats.Any() ? string.Join("\n", topCats) : "- none yet",
                "Categories available:",
                cats.Any() ? string.Join("\n", cats) : "- none",
                "Recent transactions (newest first, exact dates):",
                lines.Any() ? string.Join("\n", lines) : "- none",
                "Budgets this month:",
                budgets.Any() ? string.Join("\n", budgets) : "- none set",
                "Active recurring:",
                routines.Any() ? string.Join("\n", routines) : "- none",
                "Savings goals:",
                goals.Any() ? string.Join("\n", goals) : "- none",
                "Friend groups:",
                groups.Any() ? string.Join("\n", groups) : "- none",
                "Recent shared group expenses:",
                shared.Any() ? string.Join("\n", shared) : "- none",
                "Active campus announcements:",
                news.Any() ? string.Join("\n", news) : "- none"
            });
        }
    }
}
