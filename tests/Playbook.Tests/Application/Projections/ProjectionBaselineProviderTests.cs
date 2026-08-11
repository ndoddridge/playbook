using Playbook.Application.Projections;
using Playbook.Core.Players;
using Xunit;

namespace Playbook.Tests.Application.Projections;

public class ProjectionBaselineProviderTests
{
    [Fact]
    public void FrozenBaselineProvider_ReturnsConsistentValuesRegardlessOfSeason()
    {
        // Arrange
        var baselines = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.0m,
            [nameof(Position.RB)] = 12.0m,
            [nameof(Position.WR)] = 11.0m,
            [nameof(Position.TE)] = 8.0m,
        };
        var provider = new FrozenBaselineProvider(baselines);

        // Act
        var baseline2015 = provider.GetBaseline(Position.WR, 2015);
        var baseline2020 = provider.GetBaseline(Position.WR, 2020);
        var baseline2023 = provider.GetBaseline(Position.WR, 2023);

        // Assert
        Assert.Equal(11.0m, baseline2015);
        Assert.Equal(11.0m, baseline2020);
        Assert.Equal(11.0m, baseline2023);
    }

    [Fact]
    public void FrozenBaselineProvider_ReturnsCorrectBaselineForEachPosition()
    {
        // Arrange
        var baselines = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.QB)] = 18.0m,
            [nameof(Position.RB)] = 12.0m,
            [nameof(Position.WR)] = 11.0m,
            [nameof(Position.TE)] = 8.0m,
        };
        var provider = new FrozenBaselineProvider(baselines);

        // Act & Assert
        Assert.Equal(18.0m, provider.GetBaseline(Position.QB, 2015));
        Assert.Equal(12.0m, provider.GetBaseline(Position.RB, 2015));
        Assert.Equal(11.0m, provider.GetBaseline(Position.WR, 2015));
        Assert.Equal(8.0m, provider.GetBaseline(Position.TE, 2015));
    }

    [Fact]
    public void FrozenBaselineProvider_ReturnsDefaultForUnknownPosition()
    {
        // Arrange
        var baselines = new Dictionary<string, decimal>();
        var provider = new FrozenBaselineProvider(baselines);

        // Act
        var baseline = provider.GetBaseline(Position.WR, 2015);

        // Assert
        Assert.Equal(10m, baseline);
    }

    [Fact]
    public void EraSegmentedBaselineProvider_ReturnsEraABaselineFor2019AndEarlier()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 10.8m,
            [nameof(Position.TE)] = 7.5m,
            [nameof(Position.RB)] = 12.0m,
        };
        var eraB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 11.5m,
            [nameof(Position.TE)] = 8.5m,
            [nameof(Position.RB)] = 11.5m,
        };
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act
        var baseline2012 = provider.GetBaseline(Position.WR, 2012);
        var baseline2015 = provider.GetBaseline(Position.WR, 2015);
        var baseline2019 = provider.GetBaseline(Position.WR, 2019);

        // Assert
        Assert.Equal(10.8m, baseline2012);
        Assert.Equal(10.8m, baseline2015);
        Assert.Equal(10.8m, baseline2019);
    }

    [Fact]
    public void EraSegmentedBaselineProvider_ReturnsEraBBaselineFor2020AndLater()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 10.8m,
            [nameof(Position.TE)] = 7.5m,
            [nameof(Position.RB)] = 12.0m,
        };
        var eraB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 11.5m,
            [nameof(Position.TE)] = 8.5m,
            [nameof(Position.RB)] = 11.5m,
        };
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act
        var baseline2020 = provider.GetBaseline(Position.WR, 2020);
        var baseline2021 = provider.GetBaseline(Position.WR, 2021);
        var baseline2023 = provider.GetBaseline(Position.WR, 2023);

        // Assert
        Assert.Equal(11.5m, baseline2020);
        Assert.Equal(11.5m, baseline2021);
        Assert.Equal(11.5m, baseline2023);
    }

    [Fact]
    public void EraSegmentedBaselineProvider_ShowsWRBaselineIncreaseFromEraAToEraB()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 10.8m,
        };
        var eraB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 11.5m,
        };
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act
        var baselineA = provider.GetBaseline(Position.WR, 2019);
        var baselineB = provider.GetBaseline(Position.WR, 2020);

        // Assert
        Assert.True(baselineB > baselineA, "Era B WR baseline should be higher than Era A");
        Assert.Equal(0.7m, baselineB - baselineA);
    }

    [Fact]
    public void EraSegmentedBaselineProvider_ShowsRBBaselineDecreaseFromEraAToEraB()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.RB)] = 12.0m,
        };
        var eraB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.RB)] = 11.5m,
        };
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act
        var baselineA = provider.GetBaseline(Position.RB, 2019);
        var baselineB = provider.GetBaseline(Position.RB, 2020);

        // Assert
        Assert.True(baselineA > baselineB, "Era A RB baseline should be higher than Era B");
        Assert.Equal(0.5m, baselineA - baselineB);
    }

    [Fact]
    public void EraSegmentedBaselineProvider_GetEra_Returns_A_For2019AndEarlier()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>();
        var eraB = new Dictionary<string, decimal>();
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act & Assert
        Assert.Equal("A", provider.GetEra(2012));
        Assert.Equal("A", provider.GetEra(2019));
    }

    [Fact]
    public void EraSegmentedBaselineProvider_GetEra_Returns_B_For2020AndLater()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>();
        var eraB = new Dictionary<string, decimal>();
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act & Assert
        Assert.Equal("B", provider.GetEra(2020));
        Assert.Equal("B", provider.GetEra(2023));
    }

    [Fact]
    public void ProjectionRuleOptionsExtensions_CreateFrozenBaseline_ReturnsFrozenProvider()
    {
        // Arrange
        var options = new ProjectionRuleOptions();

        // Act
        var provider = options.CreateFrozenBaselineProvider();

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<FrozenBaselineProvider>(provider);
    }

    [Fact]
    public void ProjectionRuleOptionsExtensions_CreateEraSegmentedBaseline_ReturnsEraSegmentedProvider()
    {
        // Act
        var provider = ProjectionRuleOptionsExtensions.CreateEraSegmentedBaselineProvider();

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<EraSegmentedBaselineProvider>(provider);
    }

    [Fact]
    public void ProjectionRuleOptionsExtensions_GetEraSegmentedBaselines_ReturnsPreCommittedValues()
    {
        // Act
        var (eraA, eraB) = ProjectionRuleOptionsExtensions.GetEraSegmentedBaselines();

        // Assert
        // Era A values (2012-2019)
        Assert.Equal(18.0m, eraA[nameof(Position.QB)]);
        Assert.Equal(12.0m, eraA[nameof(Position.RB)]);
        Assert.Equal(10.8m, eraA[nameof(Position.WR)]);
        Assert.Equal(7.5m, eraA[nameof(Position.TE)]);
        Assert.Equal(8.0m, eraA[nameof(Position.K)]);
        Assert.Equal(8.0m, eraA[nameof(Position.DST)]);

        // Era B values (2020-2023)
        Assert.Equal(18.5m, eraB[nameof(Position.QB)]);
        Assert.Equal(11.5m, eraB[nameof(Position.RB)]);
        Assert.Equal(11.5m, eraB[nameof(Position.WR)]);
        Assert.Equal(8.5m, eraB[nameof(Position.TE)]);
        Assert.Equal(8.0m, eraB[nameof(Position.K)]);
        Assert.Equal(8.0m, eraB[nameof(Position.DST)]);
    }

    [Fact]
    public void EraSegmentedBaselineProvider_IsThreadSafeAndDeterministic()
    {
        // Arrange
        var eraA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 10.8m,
        };
        var eraB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Position.WR)] = 11.5m,
        };
        var provider = new EraSegmentedBaselineProvider(eraA, eraB);

        // Act - Call multiple times in sequence
        var result1 = provider.GetBaseline(Position.WR, 2015);
        var result2 = provider.GetBaseline(Position.WR, 2015);
        var result3 = provider.GetBaseline(Position.WR, 2015);

        // Assert - All results should be identical
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }
}
