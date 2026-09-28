using System.ComponentModel.DataAnnotations;
using CampusCoin.Models;
namespace CampusCoin.ViewModels;

public class LoginForm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
public class RegisterForm : LoginForm
{
    [Required, StringLength(80), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
}
public class ProfileForm
{
    [Required, StringLength(80), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [StringLength(40), Display(Name = "Academic year")] public string? AcademicYear { get; set; }
    [Range(typeof(decimal), "0", "1000000"), Display(Name = "Monthly allowance")] public decimal? Allowance { get; set; }
    [Range(typeof(decimal), "0", "1000000"), Display(Name = "Monthly savings goal")] public decimal? SavingsGoal { get; set; }
    public string Email { get; set; } = "";
}
public class TransactionForm
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Description { get; set; } = "";
    [Required, Range(typeof(decimal), "0.01", "1000000")] public decimal? Amount { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Category")] public int CategoryId { get; set; }
    [Required, DataType(DataType.Date)] public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}
public class RecurringTransactionForm
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Description { get; set; } = "";
    [Required, Range(typeof(decimal), "0.01", "1000000")] public decimal? Amount { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Category")] public int CategoryId { get; set; }
    [EnumDataType(typeof(RecurrenceFrequency))] public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;
    [Required, DataType(DataType.Date), Display(Name = "Start date")] public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool IsActive { get; set; } = true;
}
public class CategoryForm
{
    public int Id { get; set; }
    [Required, StringLength(60)] public string Name { get; set; } = "";
    [EnumDataType(typeof(CategoryType))] public CategoryType Type { get; set; }
}
public class BudgetForm
{
    public int Id { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Expense category")] public int CategoryId { get; set; }
    [Required, Range(typeof(decimal), "0.01", "1000000"), Display(Name = "Monthly limit")] public decimal? Amount { get; set; }
    [Required, DataType(DataType.Date)] public DateOnly Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
}
public record BudgetProgress(int Id, string Category, decimal Limit, decimal Spent)
{
    public decimal Percent => Limit > 0 ? Spent / Limit * 100 : 0;
}
public class DashboardViewModel
{
    public string Name { get; set; } = "";
    public DateOnly Month { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Balance => Income - Expenses;
    public List<Transaction> Recent { get; set; } = [];
    public List<BudgetProgress> Budgets { get; set; } = [];
    public string[] Labels { get; set; } = [];
    public decimal[] IncomeTrend { get; set; } = [];
    public decimal[] ExpenseTrend { get; set; } = [];
    public string[] CategoryLabels { get; set; } = [];
    public decimal[] CategoryAmounts { get; set; } = [];
    public string Tip { get; set; } = "";
    public List<Announcement> ActiveAnnouncements { get; set; } = [];
    public TreeHealthViewModel Tree { get; set; } = new();
    public List<AffordCategoryOption> AffordCategories { get; set; } = [];
    public XpProgressViewModel Xp { get; set; } = new();
    public SafeToSpendViewModel SafeToSpend { get; set; } = new();
    public MonthComparisonViewModel MonthCompare { get; set; } = new();
    public FutureYouDashboardCardViewModel? FutureYou { get; set; }
    public SundayLetterCardViewModel SundayLetter { get; set; } = new();
    public bool XpProgressEnabled { get; set; } = true;
}

public class SundayLetterCardViewModel
{
    public int LetterId { get; set; }
    public DateOnly WeekStart { get; set; }
    public DateOnly UnlockDate { get; set; }
    public string WeekLabel { get; set; } = "";
    public bool IsUnlocked { get; set; }
    public bool IsOpened { get; set; }
    public bool IsSaved { get; set; }
    public int DaysUntilUnlock { get; set; }
    public string UnlockLabel { get; set; } = "";
    public string Title { get; set; } = "Your Sunday letter";
    public string Body { get; set; } = "";
    public string SealedHint { get; set; } = "The seal breaks on Sunday.";
    public string Tone { get; set; } = "steady";
    public int GoodDays { get; set; }
    public List<SundayLetterArchiveItem> LetterBox { get; set; } = [];
}

public class SundayLetterArchiveItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly UnlockDate { get; set; }
    public string WeekLabel { get; set; } = "";
    public string Preview { get; set; } = "";
    public string Body { get; set; } = "";
    public string Tone { get; set; } = "steady";
    public bool IsOpened { get; set; }
}

public class TreeHealthViewModel
{
    public DateOnly Month { get; set; }
    public int HealthScore { get; set; }
    public int GrowthPercent { get; set; } = 40;
    public string State { get; set; } = "Steady";
    public string PreviousState { get; set; } = "Steady";
    public string Title { get; set; } = "Steady";
    public string Blurb { get; set; } = "Your Money Garden grows with careful spending.";
    public string SoftMessage { get; set; } = "The garden remembers your good days.";
    public string Motion { get; set; } = "idle";
    public bool IsMonthFinalized { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int ConsistencyDays { get; set; }
    public int BloomCount { get; set; } = 2;
    public List<string> UnlockedElements { get; set; } = [];
    public bool HasButterfly { get; set; }
    public bool HasBird { get; set; }
    public bool HasFox { get; set; }
    public bool HasGlow { get; set; }
    public bool HasRare { get; set; }
    public bool HasBush { get; set; }
}

public record AffordCategoryOption(int Id, string Name);

public class AffordabilityResult
{
    public string Verdict { get; set; } = "Yes"; // Easy | Yes | Caution | No
    public string Headline { get; set; } = "";
    public string Detail { get; set; } = "";
    public decimal Amount { get; set; }
    public string AmountFormatted { get; set; } = "";
    public decimal MonthBalance { get; set; }
    public string MonthBalanceFormatted { get; set; } = "";
    public decimal UpcomingRecurring { get; set; }
    public string UpcomingRecurringFormatted { get; set; } = "";
    public decimal AvailableAfterRecurring { get; set; }
    public string AvailableAfterRecurringFormatted { get; set; } = "";
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public bool HasBudget { get; set; }
    public decimal? BudgetLimit { get; set; }
    public decimal? BudgetRemaining { get; set; }
    public string? BudgetRemainingFormatted { get; set; }
    public decimal? PercentOfRemainingBudget { get; set; }
    public bool WouldExceedBudget { get; set; }
    public bool WouldExceedBalance { get; set; }
}

public class XpProgressViewModel
{
    public int Level { get; set; } = 1;
    public string Title { get; set; } = "Budget Rookie";
    public int TotalXp { get; set; }
    public int XpIntoLevel { get; set; }
    public int XpForNextLevel { get; set; } = 100;
    public int ProgressPercent { get; set; }
    public bool JustLeveledUp { get; set; }
}

public class SafeToSpendViewModel
{
    public decimal Amount { get; set; }
    public string AmountFormatted { get; set; } = "";
    public int DaysLeft { get; set; }
    public int DaysInMonth { get; set; }
    public decimal RemainingBudget { get; set; }
    public string RemainingBudgetFormatted { get; set; } = "";
    public decimal MonthBalance { get; set; }
    public string MonthBalanceFormatted { get; set; } = "";
    public int MonthProgressPercent { get; set; }
    public string SourceLabel { get; set; } = "";
    public bool HasBudgets { get; set; }
    public string Blurb { get; set; } = "";
}

public class MonthComparisonViewModel
{
    public string CurrentLabel { get; set; } = "";
    public string PreviousLabel { get; set; } = "";
    public decimal CurrentIncome { get; set; }
    public decimal PreviousIncome { get; set; }
    public string CurrentIncomeFormatted { get; set; } = "";
    public string PreviousIncomeFormatted { get; set; } = "";
    public decimal? IncomeChangePercent { get; set; }
    public decimal CurrentExpenses { get; set; }
    public decimal PreviousExpenses { get; set; }
    public string CurrentExpensesFormatted { get; set; } = "";
    public string PreviousExpensesFormatted { get; set; } = "";
    public decimal? ExpenseChangePercent { get; set; }
    public decimal CurrentSaved { get; set; }
    public decimal PreviousSaved { get; set; }
    public string CurrentSavedFormatted { get; set; } = "";
    public string PreviousSavedFormatted { get; set; } = "";
    public decimal? SavedChangePercent { get; set; }
    public string CurrentTopCategory { get; set; } = "—";
    public string PreviousTopCategory { get; set; } = "—";
    public string CurrentTopCategoryAmountFormatted { get; set; } = "";
    public string PreviousTopCategoryAmountFormatted { get; set; } = "";
    public decimal? TopCategoryChangePercent { get; set; }
    public string TopCategoryNote { get; set; } = "";
}

public class WhatIfResult
{
    public bool Ok { get; set; }
    public string Scenario { get; set; } = "";
    public string Label { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string? CategoryName { get; set; }
    public decimal BeforeBalance { get; set; }
    public decimal AfterBalance { get; set; }
    public string BeforeBalanceFormatted { get; set; } = "";
    public string AfterBalanceFormatted { get; set; } = "";
    public string DeltaFormatted { get; set; } = "";
    public string BeforeIncomeFormatted { get; set; } = "";
    public string AfterIncomeFormatted { get; set; } = "";
    public string BeforeExpensesFormatted { get; set; } = "";
    public string AfterExpensesFormatted { get; set; } = "";
    public string? BudgetImpact { get; set; }
}

public class GoalForm
{
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Range(typeof(decimal), "0.01", "10000000")] public decimal Target { get; set; }
    [DataType(DataType.Date)] public DateOnly? Deadline { get; set; }
}
public record InsightAlert(string Title, string Detail, string Tone);
public record GoalProgress(int Id, string Name, decimal Target, decimal Saved, DateOnly? Deadline)
{
    public decimal Percent => Target > 0 ? Math.Min(100, Saved / Target * 100) : 0;
    public decimal Remaining => Math.Max(0, Target - Saved);
}
public class InsightsViewModel
{
    public DateOnly Month { get; set; }
    public decimal ForecastExpenses { get; set; }
    public decimal AverageExpenses { get; set; }
    public decimal ForecastBalance { get; set; }
    public decimal SavingsRate { get; set; }
    public List<InsightAlert> Alerts { get; set; } = [];
    public List<GoalProgress> Goals { get; set; } = [];
    public int CheckInStreak { get; set; }
    public List<string> Badges { get; set; } = [];
    public string[] HeatmapLabels { get; set; } = [];
    public decimal[] HeatmapValues { get; set; } = [];
    public decimal PulseTotal { get; set; }
    public string PulsePeakLabel { get; set; } = "";
    public bool HasPulseData { get; set; }
}
public class SettingsViewModel
{
    [Required, RegularExpression("Auto|Gemini|OpenAI|Local")] public string AiProvider { get; set; } = "Auto";
    [StringLength(500), DataType(DataType.Password), Display(Name = "Personal API key")] public string? ApiKey { get; set; }
    public bool RemoveApiKey { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasPendingAdminRequest { get; set; }
    public bool IsAdministrator { get; set; }
    [Required, RegularExpression("Auto|USD|QAR|EUR|GBP|AED|SAR|PKR|INR|CAD|AUD"), Display(Name = "Display currency")]
    public string CurrencyCode { get; set; } = "Auto";
    [RegularExpression("Fixed|PerUser"), Display(Name = "Admin money display")]
    public string AdminCurrencyMode { get; set; } = "Fixed";
    public bool FutureYouMessagesEnabled { get; set; } = true;
    public bool XpProgressEnabled { get; set; } = true;
}

public class ChangePasswordForm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword)), DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int DisabledUsers { get; set; }
    public int TotalTransactions { get; set; }
    public int TotalIncomeTransactions { get; set; }
    public int TotalExpenseTransactions { get; set; }
    public string[] CategoryLabels { get; set; } = [];
    public int[] CategoryCounts { get; set; } = [];
    public decimal[] CategoryPercentages { get; set; } = [];
    public List<AdminActivityLog> RecentLogs { get; set; } = [];
}

public class AdminUserItemViewModel
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsDisabled { get; set; }
    public string? AcademicYear { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool CanDisableUsers { get; set; }
    public bool CanResetPasswords { get; set; }
    public bool CanManageCategories { get; set; }
    public bool CanManageAnnouncements { get; set; }
    public bool CanManageTipTemplates { get; set; }
    public bool CanManagePermissions { get; set; }
}

public class SidebarNavViewModel
{
    public bool ShowAdminUi { get; set; }
    public bool IsAdministrator { get; set; }
    public string CurrentMode { get; set; } = "student";
    public CampusCoin.Services.AdminNavPermissions Permissions { get; set; } = CampusCoin.Services.AdminNavPermissions.None;
}

public class AnnouncementForm
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [Required] public string Message { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [Range(1, 3650)] public int DurationAmount { get; set; } = 7;
    [RegularExpression("Minutes|Hours|Days|Months")] public string DurationUnit { get; set; } = "Days";
}

public class TipTemplateForm
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [Required] public string Tip { get; set; } = "";
    [Required, StringLength(60)] public string Category { get; set; } = "General";
    public bool IsActive { get; set; } = true;
}

