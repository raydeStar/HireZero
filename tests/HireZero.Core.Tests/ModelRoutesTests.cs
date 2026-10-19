using System.Text.RegularExpressions;
using HireZero.Core;

// The route catalog is the only place model ids live (docs/adr/0008-model-routes.md).
public sealed partial class ModelRoutesTests
{
    private static readonly string Repository = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../..")
    );

    [Fact]
    public void CompanyRoutesComeFromTheCatalog()
    {
        var routing = new CompanyModelRouting();
        Assert.Equal(ModelRoutes.Route(ModelRoutes.CompanyText), routing.TextModel);
        Assert.Equal(ModelRoutes.Route(ModelRoutes.CompanyCoding), routing.CodingModel);
        routing.Validate();
    }

    [Fact]
    public void UnknownPurposesAreRefused() =>
        Assert.Throws<InvalidOperationException>(() => ModelRoutes.Route("nobody.configured-this"));

    [Fact]
    public void NamesAndNativeIds()
    {
        var text = ModelRoutes.Route(ModelRoutes.CompanyText);
        Assert.Equal("openai", ModelRoutes.Provider(text));
        Assert.DoesNotContain('/', ModelRoutes.Native(text));
        Assert.Equal("unlisted/model", ModelRoutes.Name("unlisted/model"));
    }

    // Every purpose the source names must be configured, and no source file may name a model id.
    [Fact]
    public void SourceNamesPurposesNotModels()
    {
        var sources = Directory
            .EnumerateFiles(Path.Combine(Repository, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToDictionary(path => Path.GetRelativePath(Repository, path), File.ReadAllText);
        Assert.Empty(sources.Where(source => ModelId().IsMatch(source.Value)).Select(source => source.Key));
        foreach (
            var purpose in sources
                .Values.SelectMany(text => PurposeConstant().Matches(text))
                .Select(m => m.Groups[1].Value)
                .Distinct()
        )
            Assert.NotNull(ModelRoutes.Route(purpose));
    }

    [GeneratedRegex(@"""(openai/)?gpt-[0-9]")]
    private static partial Regex ModelId();

    [GeneratedRegex(@"const string \w+ = ""((?:company|assistant|developer)\.[a-z-]+)""")]
    private static partial Regex PurposeConstant();
}
