using Xunit;

namespace HoyoFileClean.Tests;

public sealed class IdentificationEvaluatorTests
{
    private static Dictionary<string, Dictionary<string, IdentificationMeaning>> Meanings(bool retainConflicts = false,
        bool multiple = false) => new()
    {
        ["first"] = new() { ["biz"] = new("hk4e_cn", LabelTextId: "IdentificationLabelGame", Required: true,
            Multiple: multiple, RetainConflicts: retainConflicts) },
        ["corroboration"] = new() { ["biz"] = new("hk4e_cn", LabelTextId: "IdentificationLabelGame", Required: true,
            Multiple: multiple, RetainConflicts: retainConflicts) },
        ["other"] = new() { ["biz"] = new("hk4e_global", LabelTextId: "IdentificationLabelGame", Required: true,
            Multiple: multiple, RetainConflicts: retainConflicts) }
    };

    private static IReadOnlyDictionary<string, IdentificationEvaluator.Definition> Compile(
        Dictionary<string, Dictionary<string, IdentificationMeaning>> meanings)
        => IdentificationEvaluator.Compile(meanings.Select(pair => (pair.Key, (Dictionary<string, IdentificationMeaning>?)pair.Value)));

    private static (GameIdentification Game, IReadOnlyDictionary<string, ManifestMetadataField> Manifest) Identify(
        string[] clues, Dictionary<string, Dictionary<string, IdentificationMeaning>> meanings)
    {
        string root = Path.Combine(TestTemporaryFiles.Root, "shared-identification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllLines(Path.Combine(root, "clues.txt"), clues.Select(id => id + "=" + id));
        string[] ids = ["first", "corroboration", "other"];
        var game = new IdentificationConfiguration<GameDetectionDefinition>(ids.ToDictionary(id => id,
            id => new GameDetectionDefinition(["clues.txt"], "^" + id + "=(?<value>[^\\r\\n]+)", false, Meanings: meanings[id])));
        var manifest = new IdentificationConfiguration<ManifestDetectionDefinition[]>(ids.ToDictionary(id => id,
            id => new[] { new ManifestDetectionDefinition("files.path", id, Exact: true, Meanings: meanings[id]) }));
        var token = TestContext.Current.CancellationToken;
        return (GameIdentifier.Identify(root, game, token), new ManifestIdentifier(manifest).Identify(
            new FileListDocument(clues.Length, clues.Length, clues.Select(id => new ManifestFile(id, 1)).ToArray()), token));
    }

    [Fact]
    public void DifferentReadersUseTheSameMappingsAndDeduplicateTheSameValue()
    {
        var result = Identify(["first", "corroboration"], Meanings());
        Assert.Equal("hk4e_cn", Assert.Single(result.Game.Information["biz"].Values).RawValue);
        Assert.Equal("hk4e_cn", result.Manifest["biz"].Value);
        Assert.False(result.Manifest["biz"].Conflict);
        Assert.Equal(new[] { "corroboration", "first" }, result.Manifest["biz"].Matches.Select(match => match.Id));
    }

    [Fact]
    public void BothReadersRetainConflictingCandidatesWhenConfigured()
    {
        var result = Identify(["first", "other"], Meanings(retainConflicts: true));
        Assert.Equal(new[] { "hk4e_cn", "hk4e_global" }, result.Game.Information["biz"].Values.Select(value => value.RawValue));
        Assert.True(result.Manifest["biz"].Conflict);
        Assert.Equal(result.Game.Information["biz"].Values.Select(value => value.RawValue).Order(),
            result.Manifest["biz"].Matches.Select(match => match.Value).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictConflictPolicyIsShared(bool manifest)
    {
        IdentificationClue[] clues = [new("first", "first"), new("other", "other")];
        if (!manifest)
        {
            Assert.Equal(CoreErrorCode.GameDetectionConflict, Assert.Throws<CoreException>(() =>
                IdentificationEvaluator.Evaluate(clues, Compile(Meanings()), TestContext.Current.CancellationToken)).Code);
            return;
        }
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["first"] = [new("files.path", "other", Meanings: Meanings()["first"])],
                ["other"] = [new("files.path", "other", Meanings: Meanings()["other"])] });
        Assert.Equal(CoreErrorCode.GameDetectionConflict, Assert.Throws<CoreException>(() =>
            new ManifestIdentifier(configuration).Identify(new(1, 1, [new("other", 1)]), TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void MissingRequiredResultAndCancellationAreShared()
    {
        Assert.Equal(CoreErrorCode.GameIdentificationFailed, Assert.Throws<CoreException>(() =>
            IdentificationEvaluator.Evaluate([], Compile(Meanings()), TestContext.Current.CancellationToken)).Code);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => IdentificationEvaluator.Evaluate(
            [], Compile(Meanings()), source.Token));
    }

    [Fact]
    public void DynamicCluesUseTheSamePresenceAndSourceSelectionRules()
    {
        var definitions = new Dictionary<string, Dictionary<string, IdentificationMeaning>>
        {
            ["plugin"] = new() { ["plugins"] = new(Multiple: true, FromDetection: true) }
        };
        var result = IdentificationEvaluator.Evaluate(
            [new("plugin", "1.0.0", "first"), new("plugin", "2.0.0", "second")],
            Compile(definitions), TestContext.Current.CancellationToken)["plugins"];
        Assert.Equal(new[] { "first", "second" }, result.Values.Select(value => value.Item));
        Assert.Equal(new[] { "plugin:first", "plugin:second" }, result.Matches.Select(match => match.Id));
    }

    [Fact]
    public void BothReadersAcceptConfiguredMultipleValuesWithoutCallingThemConflicts()
    {
        var result = Identify(["first", "other"], Meanings(multiple: true));
        Assert.Equal(2, result.Game.Information["biz"].Values.Count);
        Assert.True(result.Manifest["biz"].Multiple);
        Assert.False(result.Manifest["biz"].Conflict);
        Assert.Null(result.Manifest["biz"].Value);
        Assert.Equal(result.Game.Information["biz"].Values.Select(value => value.RawValue).Order(),
            result.Manifest["biz"].Matches.Select(match => match.Value).Order());
        string json = System.Text.Json.JsonSerializer.Serialize(result.Manifest["biz"]);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ManifestMetadataField>(json)!;
        Assert.True(restored.Multiple);
        Assert.False(restored.Conflict);
        Assert.Null(restored.Value);
        Assert.Equal(result.Manifest["biz"].Matches, restored.Matches);
    }

    [Fact]
    public void ManifestSourceReturnsActualMatchedFieldsAndPreservesEveryDistinctValue()
    {
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["paths"] = [new("files.path", "Data/", Prefix: true,
                Meanings: new() { ["paths"] = new(Multiple: true, FromDetection: true) })] });
        var result = new ManifestIdentifier(configuration).Identify(new(3, 3,
            [new("Data/a", 1), new("Other", 1), new("Data/b", 1)]), TestContext.Current.CancellationToken)["paths"];
        Assert.False(result.Conflict);
        Assert.Equal(new[] { "Data/a", "Data/b" }, result.Matches.Select(match => match.Value));
        Assert.All(result.Matches, match => Assert.Equal("paths", match.Id));
        Assert.DoesNotContain(result.Matches, match => match.Value == "matched");
    }

    [Fact]
    public void EachDetectionOwnsItsMeaningsWithoutCrossDetectionConditions()
    {
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["paths"] = [new("files.path", "Data/b", Exact: true,
                Meanings: new() { ["game"] = new("game") })] });
        var result = new ManifestIdentifier(configuration).Identify(new(2, 2,
            [new("Data/a", 1), new("Data/b", 1)]), TestContext.Current.CancellationToken);
        Assert.Equal("game", result["game"].Value);
    }

    [Fact]
    public void PresenceOnlyManifestRuleDoesNotRetainEveryMatchingPath()
    {
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["paths"] = [new("files.path", "Data/", Prefix: true,
                Meanings: new() { ["game"] = new("game") })] });
        var identifier = new ManifestIdentifier(configuration);
        ManifestFile[] files = Enumerable.Range(0, 100000).Select(index => new ManifestFile($"Data/{index}", 1)).ToArray();
        var document = new FileListDocument(files.Length, files.Length, files);
        _ = identifier.Identify(document, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = identifier.Identify(document, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal("game", result["game"].Value);
        Assert.Single(result["game"].Matches);
        Assert.True(allocated < 64 * 1024, $"100000 matched paths allocated={allocated}");
    }

    [Fact]
    public void ManifestRejectsPresentationOnlyValuesInsteadOfSerializingNullMetadata()
    {
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["id"] = [new("files.path", "a",
                Meanings: new() { ["game"] = new(TextId: "GameLabel") })] });
        Assert.Equal(CoreErrorCode.IdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => new ManifestIdentifier(configuration)).Code);
    }

    [Fact]
    public void CompiledManifestRulesDoNotRetainMutableConfigurationArrays()
    {
        var meanings = new Dictionary<string, IdentificationMeaning> { ["game"] = new("game") };
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["id"] = [new("files.path", "a", Meanings: meanings)] });
        var identifier = new ManifestIdentifier(configuration);
        meanings["game"] = new("changed");
        configuration.Detections.Clear();
        Assert.Equal("game", identifier.Identify(new(1, 1, [new("a", 1)]),
            TestContext.Current.CancellationToken)["game"].Value);
    }

    [Fact]
    public void InconsistentPoliciesAndAmbiguousOutputModesAreRejected()
    {
        var meanings = Meanings();
        meanings["first"]["biz"] = meanings["first"]["biz"] with { Multiple = true };
        Assert.Equal(CoreErrorCode.IdentificationConfigurationInvalid, Assert.Throws<CoreException>(() => Compile(meanings)).Code);
        meanings = Meanings();
        meanings["first"]["biz"] = new(RawValue: "raw", TextId: "translated");
        Assert.Equal(CoreErrorCode.IdentificationConfigurationInvalid, Assert.Throws<CoreException>(() => Compile(meanings)).Code);
        meanings["first"]["biz"] = new();
        Assert.Equal(CoreErrorCode.IdentificationConfigurationInvalid, Assert.Throws<CoreException>(() => Compile(meanings)).Code);
    }
}
