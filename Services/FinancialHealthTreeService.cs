using CampusCoin.ViewModels;

namespace CampusCoin.Services;

/// <summary>Compatibility wrapper — Money Garden is the source of truth.</summary>
public class FinancialHealthTreeService(IGardenService garden)
{
    public Task<TreeHealthViewModel> EvaluateAndPersistAsync(string userId, DateOnly month, CancellationToken cancellationToken = default)
        => garden.EvaluateAndPersistAsync(userId, month, cancellationToken);
}
