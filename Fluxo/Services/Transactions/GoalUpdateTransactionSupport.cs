using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Transactions;

public static class GoalUpdateTransactionSupport
{
    public const string GoalUpdateTagName = "Goal Update";
    public const string GoalUpdateTagColor = "#aed4e1";

    public static bool IsEligibleGoalSourceType(AccountType sourceType)
    {
        return sourceType is AccountType.Cash or AccountType.Checking;
    }

    public static async Task<Tag> ResolveGoalUpdateTagAsync(IAppDataService appData)
    {
        var tags = await appData.GetTagsAsync();
        var existingTag = tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, GoalUpdateTagName, StringComparison.OrdinalIgnoreCase));
        if (existingTag is not null)
            return existingTag;

        var goalUpdateTag = new Tag
        {
            Name = GoalUpdateTagName,
            HexCode = GoalUpdateTagColor
        };

        await appData.AddTagAsync(goalUpdateTag);
        await appData.SaveChangesAsync();
        return goalUpdateTag;
    }
}
