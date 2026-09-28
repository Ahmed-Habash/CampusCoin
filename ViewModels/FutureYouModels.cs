namespace CampusCoin.ViewModels;

public class FutureYouPageViewModel
{
    public string FirstName { get; set; } = "there";
    public bool HasTransactionHistory { get; set; }
    public bool HasIncome { get; set; }
    public bool MessagesEnabled { get; set; } = true;
    public bool ParallelLivesHidden { get; set; }

    public string? GoalName { get; set; }
    public decimal GoalTarget { get; set; }
    public decimal GoalSaved { get; set; }
    public decimal GoalRemaining { get; set; }
    public bool GoalReached { get; set; }
    public bool UsingTemporaryGoal { get; set; }
    public string CurrencyCode { get; set; } = "USD";

    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal CurrentMonthlySavings { get; set; }

    public List<FutureYouCategorySlider> Sliders { get; set; } = [];
    public decimal ExtraMonthlySavings { get; set; }
    public decimal SuggestedExtraSavings { get; set; }

    public FutureYouProjection CurrentYou { get; set; } = new();
    public FutureYouProjection SmarterYou { get; set; } = new();
    public FutureYouProjection YourScenario { get; set; } = new();

    public FutureYouMessageViewModel? FutureMessage { get; set; }
    public List<AlternativeTipViewModel> Tips { get; set; } = [];
    public ParallelLivesViewModel? ParallelLives { get; set; }
    public CampusChallengeViewModel Challenge { get; set; } = new();
}

public class FutureYouCategorySlider
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Current { get; set; }
    public decimal Smarter { get; set; }
    public decimal Scenario { get; set; }
    public decimal Max { get; set; }
    public bool IsPrimary { get; set; }
}

public class FutureYouProjection
{
    public string PathKey { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal MonthlySavings { get; set; }
    public string MonthlySavingsFormatted { get; set; } = "";
    public int? MonthsToGoal { get; set; }
    public string MonthsToGoalLabel { get; set; } = "";
    public DateOnly? GoalDate { get; set; }
    public string GoalDateLabel { get; set; } = "";
    public decimal ExtraVsCurrent { get; set; }
    public string ExtraVsCurrentFormatted { get; set; } = "";
    public string CoachingTone { get; set; } = "neutral"; // positive | neutral | caution
    public string CoachingMessage { get; set; } = "";
    public List<decimal> Accumulation { get; set; } = [];
    public List<string> MonthLabels { get; set; } = [];
}

public class FutureYouSimulateRequest
{
    public Dictionary<string, decimal> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal ExtraMonthlySavings { get; set; }
    public decimal? TemporaryGoalTarget { get; set; }
    public string? TemporaryGoalName { get; set; }
    public decimal? PurchaseAmount { get; set; }
}

public class FutureYouSimulateResult
{
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
    public FutureYouProjection CurrentYou { get; set; } = new();
    public FutureYouProjection SmarterYou { get; set; } = new();
    public FutureYouProjection YourScenario { get; set; } = new();
    public FutureYouDecisionImpact? DecisionImpact { get; set; }
    public FutureYouMessageViewModel? FutureMessage { get; set; }
    public decimal GoalTarget { get; set; }
    public string GoalTargetFormatted { get; set; } = "";
    public string GoalName { get; set; } = "";
    public decimal GoalRemaining { get; set; }
    public string GoalRemainingFormatted { get; set; } = "";
    public bool UsingTemporaryGoal { get; set; }
    public string EstimateDisclaimer { get; set; } = "Estimates only — nothing here changes your real transactions, budgets, or goals.";
}

public class FutureYouDecisionImpact
{
    public decimal PurchaseAmount { get; set; }
    public string PurchaseAmountFormatted { get; set; } = "";
    public int DelayDays { get; set; }
    public string Message { get; set; } = "";
}

public class FutureYouMessageViewModel
{
    public string FromLabel { get; set; } = "Message from Future You";
    public string Body { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class AlternativeTipViewModel
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string CategoryKey { get; set; } = "";
    public decimal SuggestedWeeklySave { get; set; }
    public string SuggestedWeeklySaveFormatted { get; set; } = "";
    public decimal SimulatorExtraMonthly { get; set; }
    public decimal? SimulatorCategoryCutPercent { get; set; }
}

public class ParallelLivesViewModel
{
    public string LifeA { get; set; } = "";
    public string LifeB { get; set; } = "";
    public bool Hidden { get; set; }
}

public class CampusChallengeViewModel
{
    public bool HasChallenge { get; set; }
    public int? ChallengeId { get; set; }
    public string Title { get; set; } = "30-Day Campus Challenge";
    public string Status { get; set; } = "None";
    public int DayNumber { get; set; }
    public int CompletedDays { get; set; }
    public int TotalDays { get; set; } = 30;
    public int StreakDays { get; set; }
    public int CompletionPercent { get; set; }
    public string? TodayMicroGoal { get; set; }
    public bool TodayCompleted { get; set; }
    public List<CampusChallengeDayViewModel> Days { get; set; } = [];
}

public class CampusChallengeDayViewModel
{
    public int DayNumber { get; set; }
    public DateOnly Date { get; set; }
    public string MicroGoal { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool IsToday { get; set; }
    public bool IsFuture { get; set; }
}

public class FutureYouDashboardCardViewModel
{
    public string GoalName { get; set; } = "your goal";
    public decimal GoalRemaining { get; set; }
    public string GoalRemainingFormatted { get; set; } = "";
    public string MonthsLabel { get; set; } = "";
    public string CoachingMessage { get; set; } = "";
    public FutureYouMessageViewModel? FutureMessage { get; set; }
    public List<AlternativeTipViewModel> Tips { get; set; } = [];
    public ParallelLivesViewModel? ParallelLives { get; set; }
    public CampusChallengeViewModel Challenge { get; set; } = new();
}

public class FutureYouAdminStatsViewModel
{
    public int DistinctSimulatorUsers { get; set; }
    public int SimulatorOpens { get; set; }
    public int ChallengesStarted { get; set; }
    public int ChallengesCompleted { get; set; }
    public decimal AverageChallengeCompletionPercent { get; set; }
    public List<ChallengeTemplateItem> Templates { get; set; } = [];
}

public class ChallengeTemplateItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string PatternKey { get; set; } = "";
    public string MicroGoalTemplate { get; set; } = "";
    public bool IsActive { get; set; }
}
