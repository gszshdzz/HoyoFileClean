using Xunit;

namespace HoyoFileClean.Tests;

public sealed class IdentificationEvaluatorTests
{
    private static Dictionary<string, IdentificationResultDefinition> Results(bool retainConflicts = false,
        bool multiple = false) => new()
    {
        ["biz"] = new("IdentificationLabelGame", Required: true, Multiple: multiple,
            RetainConflicts: retainConflicts, Rules:
            [
                new(new(), AnyDetection: ["first", "corroboration"], RawValue: "hk4e_cn"),
                new(new() { ["other"] = "other" }, RawValue: "hk4e_global")
            ])
    };

    private static (GameIdentification Game, IReadOnlyDictionary<string, ManifestMetadataField> Manifest) Identify(
        string[] clues, Dictionary<string, IdentificationResultDefinition> results)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "shared-identification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllLines(Path.Combine(root, "clues.txt"), clues.Select(id => id + "=" + id));
        string[] ids = ["first", "corroboration", "other"];
        var game = new IdentificationConfiguration<GameDetectionDefinition>(ids.ToDictionary(id => id,
            id => new GameDetectionDefinition(["clues.txt"], "^" + id + "=(?<value>[^\\r\\n]+)", false)), results);
        var manifest = new IdentificationConfiguration<ManifestDetectionDefinition[]>(ids.ToDictionary(id => id,
            id => new[] { new ManifestDetectionDefinition("files.path", id, Exact: true) }), results);
        var token = TestContext.Current.CancellationToken;
        return (GameIdentifier.Identify(root, game, token), new ManifestIdentifier(manifest).Identify(
            new FileListDocument(clues.Length, clues.Length, clues.Select(id => new ManifestFile(id, 1)).ToArray()), token));
    }

    [Fact]
    public void DifferentReadersUseTheSameMappingsAndDeduplicateTheSameValue()
    {
        var result = Identify(["first", "corroboration"], Results());
        Assert.Equal("hk4e_cn", Assert.Single(result.Game.Information["biz"].Values).RawValue);
        Assert.Equal("hk4e_cn", result.Manifest["biz"].Value);
        Assert.False(result.Manifest["biz"].Conflict);
        Assert.Equal(new[] { "corroboration", "first" }, result.Manifest["biz"].Matches.Select(match => match.Id));
    }

    [Fact]
    public void BothReadersRetainConflictingCandidatesWhenConfigured()
    {
        var result = Identify(["first", "other"], Results(retainConflicts: true));
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
                IdentificationEvaluator.Evaluate(clues, Results(), TestContext.Current.CancellationToken)).Code);
            return;
        }
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["first"] = [new("files.path", "other")], ["corroboration"] = [new("files.path", "unused")],
                ["other"] = [new("files.path", "other")] }, Results());
        Assert.Equal(CoreErrorCode.GameDetectionConflict, Assert.Throws<CoreException>(() =>
            new ManifestIdentifier(configuration).Identify(new(1, 1, [new("other", 1)]), TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void MissingRequiredResultAndCancellationAreShared()
    {
        Assert.Equal(CoreErrorCode.GameIdentificationFailed, Assert.Throws<CoreException>(() =>
            IdentificationEvaluator.Evaluate([], Results(), TestContext.Current.CancellationToken)).Code);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => IdentificationEvaluator.Evaluate(
            [], Results(), source.Token));
    }

    [Fact]
    public void DynamicCluesUseTheSamePresenceAndSourceSelectionRules()
    {
        var definitions = new Dictionary<string, IdentificationResultDefinition>
        {
            ["plugins"] = new(Multiple: true, Rules:
                [new(new(), AnyDetection: ["plugin"], Source: "plugin")])
        };
        IdentificationEvaluator.Validate(["plugin"], definitions);
        var result = IdentificationEvaluator.Evaluate(
            [new("plugin", "1.0.0", "first"), new("plugin", "2.0.0", "second")],
            definitions, TestContext.Current.CancellationToken)["plugins"];
        Assert.Equal(new[] { "first", "second" }, result.Values.Select(value => value.Item));
        Assert.Equal(new[] { "plugin:first", "plugin:second" }, result.Matches.Select(match => match.Id));
    }

    [Fact]
    public void BothReadersAcceptConfiguredMultipleValuesWithoutCallingThemConflicts()
    {
        var result = Identify(["first", "other"], Results(multiple: true));
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
            new() { ["paths"] = [new("files.path", "Data/", Prefix: true)] },
            new() { ["paths"] = new(Multiple: true, Rules: [new(new(), Source: "paths")]) });
        var result = new ManifestIdentifier(configuration).Identify(new(3, 3,
            [new("Data/a", 1), new("Other", 1), new("Data/b", 1)]), TestContext.Current.CancellationToken)["paths"];
        Assert.False(result.Conflict);
        Assert.Equal(new[] { "Data/a", "Data/b" }, result.Matches.Select(match => match.Value));
        Assert.All(result.Matches, match => Assert.Equal("paths", match.Id));
        Assert.DoesNotContain(result.Matches, match => match.Value == "matched");
    }

    [Fact]
    public void FieldValueConditionsInspectEveryMatchingFieldNotOnlyTheFirst()
    {
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["paths"] = [new("files.path", "Data/", Prefix: true)] },
            new() { ["game"] = new(Rules: [new(new() { ["paths"] = "Data/b" }, RawValue: "game")]) });
        var result = new ManifestIdentifier(configuration).Identify(new(2, 2,
            [new("Data/a", 1), new("Data/b", 1)]), TestContext.Current.CancellationToken);
        Assert.Equal("game", result["game"].Value);
    }

    [Fact]
    public void PresenceOnlyManifestRuleDoesNotRetainEveryMatchingPath()
    {
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["paths"] = [new("files.path", "Data/", Prefix: true)] },
            new() { ["game"] = new(Rules: [new(new(), AnyDetection: ["paths"], RawValue: "game")]) });
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
            new() { ["id"] = [new("files.path", "a")] },
            new() { ["game"] = new(Rules: [new(new(), AnyDetection: ["id"], TextId: "GameLabel")]) });
        Assert.Throws<ArgumentException>(() => new ManifestIdentifier(configuration));
    }

    [Fact]
    public void CompiledManifestRulesDoNotRetainMutableConfigurationArrays()
    {
        string[] ids = ["id"];
        var configuration = new IdentificationConfiguration<ManifestDetectionDefinition[]>(
            new() { ["id"] = [new("files.path", "a")] },
            new() { ["game"] = new(Rules: [new(new(), AnyDetection: ids, RawValue: "game")]) });
        var identifier = new ManifestIdentifier(configuration);
        ids[0] = "changed";
        configuration.Detections.Clear();
        configuration.Results.Clear();
        Assert.Equal("game", identifier.Identify(new(1, 1, [new("a", 1)]),
            TestContext.Current.CancellationToken)["game"].Value);
    }

    [Fact]
    public void InvalidMappingReferencesAndAmbiguousOutputModesAreRejected()
    {
        var results = Results();
        Assert.Throws<ArgumentException>(() => IdentificationEvaluator.Validate(["first"], results));
        results["biz"] = results["biz"] with
        {
            Rules = [new(new(), RawValue: "raw", TextId: "translated")]
        };
        Assert.Throws<ArgumentException>(() => IdentificationEvaluator.Validate([], results));
        results["biz"] = results["biz"] with
        {
            Rules = [new(new(), AnyDetection: [], RawValue: "raw")]
        };
        Assert.Throws<ArgumentException>(() => IdentificationEvaluator.Validate([], results));
    }
}
