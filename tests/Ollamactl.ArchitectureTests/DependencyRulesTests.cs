using System.Reflection;
using Ollamactl.Application.Services;
using Ollamactl.Cli.Runtime;
using Ollamactl.Domain.Models;
using Ollamactl.Infrastructure.Http;
using Xunit;

namespace Ollamactl.ArchitectureTests;

[Trait("Category", "Architecture")]
public sealed class DependencyRulesTests
{
    [Fact]
    public void DomainHasNoDependenciesOnOtherProductLayers() =>
        AssertReferencesOnly(typeof(OllamaModel).Assembly);

    [Fact]
    public void ApplicationDependsOnlyOnDomain() =>
        AssertReferencesOnly(typeof(OllamaService).Assembly, "Ollamactl.Domain");

    [Fact]
    public void InfrastructureDependsOnlyOnInnerLayers() =>
        AssertReferencesOnly(
            typeof(OllamaHttpClient).Assembly,
            "Ollamactl.Application",
            "Ollamactl.Domain");

    [Fact]
    public void CliDependsOnlyOnApplicationDomainAndInfrastructure() =>
        AssertReferencesOnly(
            typeof(CliApplication).Assembly,
            "Ollamactl.Application",
            "Ollamactl.Domain",
            "Ollamactl.Infrastructure");

    [Fact]
    public void DomainExportsOnlySealedModelTypes()
    {
        var domainTypes = typeof(OllamaModel).Assembly.GetExportedTypes();

        Assert.NotEmpty(domainTypes);
        Assert.All(domainTypes, type =>
        {
            Assert.StartsWith("Ollamactl.Domain.Models", type.Namespace, StringComparison.Ordinal);
            Assert.True(type.IsSealed, $"Domain type {type.FullName} must be sealed.");
        });
    }

    private static void AssertReferencesOnly(Assembly assembly, params string[] allowedReferences)
    {
        var unexpectedReferences = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("Ollamactl.", StringComparison.Ordinal))
            .Except(allowedReferences, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpectedReferences.Length == 0,
            $"{assembly.GetName().Name} has forbidden product references: {string.Join(", ", unexpectedReferences)}");
    }
}
