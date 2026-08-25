using Fluxo.Core.Interfaces;
using Fluxo.ViewModels.Shell.Main;
using Fluxo.Views.Shell.Main;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class LifetimeSafetyTests
{
    [Fact]
    public void LifetimeSafety_SingletonUiRoots_DoNotDependOnUnitOfWorkOrRepositoryInterfaces()
    {
        AssertConstructorIsSafe(typeof(MainVM));
        AssertConstructorIsSafe(typeof(RecentActivitiesVM));
        AssertConstructorIsSafe(typeof(NotificationPanelVM));
        AssertConstructorIsSafe(typeof(SavingGoalsPanelVM));
        AssertConstructorIsSafe(typeof(UpcomingEventsPanelVM));
        AssertConstructorIsSafe(typeof(MainWindow));
    }

    private static void AssertConstructorIsSafe(Type targetType)
    {
        var constructor = targetType.GetConstructors().Single();

        foreach (var parameter in constructor.GetParameters())
        {
            Assert.NotEqual(typeof(IUnitOfWork), parameter.ParameterType);

            var namespaceName = parameter.ParameterType.Namespace ?? string.Empty;
            var interfaceName = parameter.ParameterType.Name;
            var looksLikeRepository = namespaceName.Contains(".Interfaces.Repositories", StringComparison.Ordinal) ||
                                      interfaceName.EndsWith("Repository", StringComparison.Ordinal);

            Assert.False(looksLikeRepository,
                $"{targetType.Name} should not depend directly on {parameter.ParameterType.FullName}.");
        }
    }
}
