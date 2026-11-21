using Xunit;

namespace HoyoFileClean.Tests;

public sealed class GameIdentifierTests
{
    private readonly string root = Path.Combine(TestTemporaryFiles.Root, "identification-" + Guid.NewGuid().ToString("N"));

    private const string Sample = """
        [General]
        game_version=7.2.0
        channel=1
        sub_channel=1
        uapc={"hk4e_cn":{"uapc":""},"hyp":{"uapc":""}}
        plugin_COa9vWaYX9_version=1.0.0
        [General]
        uapc={"hk4e_cn":{"uapc":""},"hyp":{"uapc":""}}
        """;

    private string Write(string text, string name = "config.ini")
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void SampleIdentifiesGameVersionChannelAndPluginWithoutChangingFile()
    {
        string path = Write(Sample);
        DateTime modified = File.GetLastWriteTimeUtc(path);
        GameIdentification result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("7.2.0", result.Detections["ini_version"]);
        Assert.False(result.Information.ContainsKey("game"));
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.Equal("1", result.Detections["ini_channel"]);
        Assert.Equal("official", Assert.Single(result.Information["channel"].Values).RawValue);
        Assert.Equal("1.0.0", Assert.Single(result.Information["plugins"].Values).RawValue);
        Assert.Equal("COa9vWaYX9", Assert.Single(result.Information["plugins"].Values).Item);
        Assert.Equal("IdentificationPluginVersion", Assert.Single(result.Information["plugins"].Values).DisplayFormatTextId);
        Assert.Equal(Sample, File.ReadAllText(path));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("game_version=7.10.0", "7.10.0")]
    [InlineData("game_version=7.1.0", "7.2.0")]
    [InlineData("game_version=7.2.0", "7.2.0")]
    [InlineData("channel=1", "7.2.0")]
    public void DuplicateValuesUseNumericMaximumOrRequireEquality(string extra, string expected)
    {
        Write(Sample + "\n" + extra);
        Assert.Equal(expected, Assert.Single(GameIdentifier.Identify(root, TestContext.Current.CancellationToken).Information["version"].Values).RawValue);
    }

    [Theory]
    [InlineData("game_version=invalid", CoreErrorCode.GameDetectionInvalid)]
    [InlineData("game_version=", CoreErrorCode.GameDetectionInvalid)]
    [InlineData("channel=2", CoreErrorCode.GameDetectionConflict)]
    [InlineData("sub_channel=2", CoreErrorCode.GameDetectionConflict)]
    [InlineData("uapc={\"hk4e_global\":{}}", CoreErrorCode.GameDetectionConflict)]
    public void InvalidOrConflictingDuplicatesFail(string extra, CoreErrorCode code)
    {
        Write(Sample + "\n" + extra);
        Assert.Equal(code, Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("game_version=7.2.0")]
    public void MissingIdentityOrUnknownMappingFails(string text)
    {
        Write(text);
        Assert.Equal(CoreErrorCode.GameIdentificationFailed,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void FutureGameUsesDifferentFilesRulesAndMappingsWithoutLogicChanges()
    {
        Write("release=1.9.0\nrelease=1.10.0", "future.txt");
        Write("identity=future\nchannel=42", "identity.txt");
        var configuration = new IdentificationConfiguration<GameDetectionDefinition>(new()
        {
            ["release"] = new(["./missing.ini", "./future.txt"], @"^release=(?<value>[^\r\n]+)", true,
                Meanings: new() { ["version"] = new(FromDetection: true, LabelTextId: "VersionLabel", Required: true) }),
            ["id"] = new(["identity.txt"], @"^identity=(?<value>[^\r\n]+)", false,
                Meanings: new() { ["game"] = new(TextId: "FutureGame", LabelTextId: "GameLabel", Required: true) }),
            ["channel"] = new(["identity.txt"], @"^channel=(?<value>[^\r\n]+)", false)
        });
        GameIdentification result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal("FutureGame", Assert.Single(result.Information["game"].Values).TextId);
        Assert.Equal("1.10.0", Assert.Single(result.Information["version"].Values).RawValue);
    }

    [Theory]
    [InlineData("empty-detections")]
    [InlineData("empty-information")]
    [InlineData("null-detections")]
    public void InvalidConfigurationFailsExplicitly(string kind)
    {
        var original = GameIdentifier.LoadConfiguration();
        var configuration = kind switch
        {
            "empty-detections" => original with { Detections = [] },
            "empty-information" => original with { Detections = original.Detections.ToDictionary(pair => pair.Key,
                pair => pair.Value with { Meanings = null }) },
            _ => original with { Detections = null! }
        };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CandidatesCannotEscapeOrFollowLinksAndHaveSizeLimits()
    {
        string path = Write(Sample);
        var config = GameIdentifier.LoadConfiguration();
        config.Detections["ini_version"] = config.Detections["ini_version"] with { Paths = ["../outside.ini"] };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
        File.WriteAllText(path, new string('x', Maintenance.MetadataLimit + 1));
        Assert.Equal(CoreErrorCode.GameDetectionInvalid, Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
        string linkRoot = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(linkRoot, root);
        Assert.Equal(CoreErrorCode.DirectoryIsLink, Assert.Throws<CoreException>(() => GameIdentifier.Identify(linkRoot, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CancellationPreventsIdentificationAndBothScanModes()
    {
        Write(Sample);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => GameIdentifier.Identify(root, source.Token));
        foreach (bool hashes in new[] { false, true })
            Assert.ThrowsAny<OperationCanceledException>(() => DirectoryScanner.Scan(root,
                computeHashes: hashes, cancellationToken: source.Token));
    }

    [Fact]
    public void HashCancellationDoesNotPublishOutputAndAllowsRetry()
    {
        Write(Sample);
        using var source = new CancellationTokenSource();
        string output = Path.Combine(Path.GetDirectoryName(root)!, Guid.NewGuid() + ".json");
        Assert.ThrowsAny<OperationCanceledException>(() => DirectoryScanner.Scan(root, output,
            computeHashes: true, progress: _ => source.Cancel(), cancellationToken: source.Token));
        Assert.False(File.Exists(output));
        Assert.Equal(1, DirectoryScanner.Scan(root, computeHashes: true, cancellationToken: TestContext.Current.CancellationToken).FileCount);
    }

    [Fact]
    public void BuildTimeConfigurationsAreCompiledAndIndependent()
    {
        string[] resources = typeof(GameIdentifier).Assembly.GetManifestResourceNames();
        Assert.DoesNotContain("HoyoFileClean.game-identification.json", resources);
        Assert.DoesNotContain("HoyoFileClean.network-services.json", resources);
        Assert.NotEmpty(NetworkManifestClient.Services);
        var first = GameIdentifier.LoadConfiguration();
        first.Detections.Clear();
        Assert.NotEmpty(GameIdentifier.LoadConfiguration().Detections);
    }

    [Fact(Explicit = true)]
    public void ProvidedRealConfigIdentifiesExpectedGame()
    {
        string repository = RepositoryRoot();
        string path = Path.Combine(repository, "refs", "config.ini");
        byte[] before = File.ReadAllBytes(path);
        var result = GameIdentifier.Identify(Path.GetDirectoryName(path)!, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.Equal("7.2.0", Assert.Single(result.Information["version"].Values).RawValue);
        Assert.False(result.Information.ContainsKey("channel"));
        Assert.Equal("1.0.0", Assert.Single(result.Information["plugins"].Values).RawValue);
        Assert.Equal(before, File.ReadAllBytes(path));
        string acceptance = Path.Combine(TestTemporaryFiles.Root, "acceptance-" + Guid.NewGuid().ToString("N"));
        foreach ((string name, string extra) in new[]
        {
            ("valid", ""), ("version-max", "\ngame_version=7.10.0"),
            ("version-invalid", "\ngame_version=bad"), ("channel-conflict", "\nchannel=2")
        })
        {
            string folder = Path.Combine(acceptance, name);
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, "config.ini");
            File.WriteAllBytes(copy, before);
            File.AppendAllText(copy, extra);
        }
        Directory.CreateDirectory(Path.Combine(acceptance, "unknown"));
        string dynamicRoot = Path.Combine(acceptance, "dynamic-plugin");
        Directory.CreateDirectory(dynamicRoot);
        string dynamicConfig = Path.Combine(dynamicRoot, "config.ini");
        File.WriteAllBytes(dynamicConfig, before);
        File.AppendAllText(dynamicConfig, "\nplugin_futurePlugin_version=2.9.0\nplugin_futurePlugin_version=2.10.0");
        string recordedRoot = Path.Combine(acceptance, "recorded-game");
        Directory.CreateDirectory(recordedRoot);
        File.WriteAllBytes(Path.Combine(recordedRoot, "config.ini"), before);
        DirectoryScanner.Scan(recordedRoot, Path.Combine(acceptance, "identified-scan.json"),
            cancellationToken: TestContext.Current.CancellationToken,
            identification: GameIdentifier.Identify(recordedRoot, TestContext.Current.CancellationToken));
        Directory.Move(recordedRoot, recordedRoot + "-moved");
        File.WriteAllText(Path.Combine(acceptance, "reference.json"), """{"fileCount":0,"totalSize":0,"files":[]}""");
        Console.WriteLine("ACCEPTANCE " + acceptance);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    [Fact]
    public void DuplicatesAcrossCandidateFilesUseTheSameResolutionPolicy()
    {
        Write(Sample);
        Write("game_version=7.10.0\nchannel=1", "other.ini");
        var config = GameIdentifier.LoadConfiguration();
        foreach (string id in new[] { "ini_version", "ini_channel" })
            config.Detections[id] = config.Detections[id] with { Paths = ["config.ini", "other.ini"] };
        var result = GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken);
        Assert.Equal("7.10.0", result.Detections["ini_version"]);
        Write("channel=2", "other.ini");
        Assert.Equal(CoreErrorCode.GameDetectionConflict,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void MeaningfulItemsAllowAlternativeSourcesAndRejectAmbiguousSingleValues()
    {
        Write(Sample);
        var config = GameIdentifier.LoadConfiguration();
        config.Detections["alternative_version"] = new(["missing.ini"], @"^version=(?<value>.+)",
            Meanings: new() { ["version"] = new("other-version", LabelTextId: "IdentificationLabelVersion") });
        Assert.Equal("7.2.0", Assert.Single(GameIdentifier.Identify(root, config,
            TestContext.Current.CancellationToken).Information["version"].Values).RawValue);
        config.Detections["alternative_version"] = config.Detections["alternative_version"] with { Paths = ["config.ini"], Pattern = @"^channel=(?<value>.+)" };
        Assert.Equal(CoreErrorCode.GameDetectionConflict,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
        foreach (string id in new[] { "ini_version", "alternative_version" })
            config.Detections[id].Meanings!["version"] = config.Detections[id].Meanings!["version"] with { Multiple = true };
        Assert.Equal(2, GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken).Information["version"].Values.Count);
    }

    [Fact]
    public void InvalidRuleReferencesAndMissingCaptureFailBeforeReading()
    {
        Write(Sample);
        var config = GameIdentifier.LoadConfiguration();
        config.Detections["ini_game_id"].Meanings!["biz"] = new(RawValue: "game", FromDetection: true,
            LabelTextId: "IdentificationLabelGame");
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
        config = GameIdentifier.LoadConfiguration();
        config.Detections["ini_version"] = config.Detections["ini_version"] with { Pattern = "game_version=(.+)" };
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CandidateFileLinksAreSkippedWithoutReadingTheTarget()
    {
        string outside = Write(Sample, "outside.ini");
        File.CreateSymbolicLink(Path.Combine(root, "config.ini"), outside);
        Write("game", "YuanShen.exe");
        using var locked = new FileStream(outside, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.False(result.Detections.ContainsKey("ini_game_id"));
    }

    [Theory]
    [InlineData("future_beta")]
    [InlineData("game-without-region")]
    [InlineData("hk4e_cn")]
    public void UapcExtractsActualPropertyNameWithoutRegionSuffixFiltering(string gameId)
    {
        Write("game_version=7.2.0\nuapc={\"hyp\":{},\"" + gameId + "\":{}}");
        var configuration = GameIdentifier.LoadConfiguration();
        var result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken);
        Assert.Equal(gameId, result.Detections["ini_game_id"]);
        Assert.Equal(gameId, Assert.Single(result.Information["biz"].Values).RawValue);
    }

    [Theory]
    [InlineData("uapc={bad}", CoreErrorCode.GameDetectionInvalid)]
    [InlineData("uapc={\"another-game\":{}}", CoreErrorCode.GameDetectionConflict)]
    [InlineData("uapc={\"hk4e_cn\":{},\"hk4e_cn\":{}}", CoreErrorCode.GameDetectionInvalid)]
    public void MalformedOrConflictingUapcIsNotSilentlyFiltered(string extra, CoreErrorCode code)
    {
        Write(Sample + "\n" + extra);
        Assert.Equal(code, Assert.Throws<CoreException>(() => GameIdentifier.Identify(root,
            TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void PluginIdsAreExtractedDynamicallyAndVersionsComparedPerPlugin()
    {
        Write(Sample + "\nplugin_futurePlugin_version=2.9.0\nplugin_futurePlugin_version=2.10.0\nplugin_another_version=1.0.0");
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        var plugins = result.Information["plugins"].Values.ToDictionary(value => value.Item!, value => value.RawValue);
        Assert.Equal(3, plugins.Count);
        Assert.Equal("1.0.0", plugins["COa9vWaYX9"]);
        Assert.Equal("2.10.0", plugins["futurePlugin"]);
        Assert.Equal("1.0.0", plugins["another"]);
        Assert.Equal("2.10.0", result.Detections["ini_plugin_version:futurePlugin"]);
    }

    [Fact]
    public void InvalidNewPluginVersionFailsWithoutAddingAnAdaptation()
    {
        Write(Sample + "\nplugin_neverConfigured_version=invalid");
        Assert.Equal(CoreErrorCode.GameDetectionInvalid, Assert.Throws<CoreException>(() =>
            GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void IdentifiedScanJsonRoundTripsWithoutAccessingGameRoot()
    {
        Write(Sample + "\nplugin_newPlugin_version=3.0.0");
        Write("game", "YuanShen.exe");
        var identification = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        string output = Path.Combine(Path.GetDirectoryName(root)!, Guid.NewGuid() + ".json");
        DirectoryScanner.Scan(root, output, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output)))
        {
            var recorded = json.RootElement.GetProperty("scan").GetProperty("gameDetections");
            Assert.Equal("hk4e_cn", recorded.GetProperty("ini_game_id").GetString());
            Assert.Equal("3.0.0", recorded.GetProperty("ini_plugin_version:newPlugin").GetString());
            Assert.DoesNotContain("IdentificationLabel", recorded.GetRawText());
        }
        Directory.Move(root, root + "-moved");
        var imported = FileComparer.ReadInputs([output]);
        var restored = GameIdentifier.FromDetections(imported.Sources[0].Scan!.GameDetections!);
        Assert.Equal(Assert.Single(restored.Information["biz"].Values).RawValue,
            imported.Sources[0].Metadata!["biz"].Value);
        Assert.Equal("hk4e", imported.Sources[0].Metadata!["game"].Value);
        Assert.Equal("game", imported.Sources[0].Metadata!["package"].Value);
        Assert.Equal("game", Assert.Single(restored.Information["package"].Values).RawValue);
        Assert.Equal(identification.Detections, restored.Detections);
        Assert.Equal(identification.Information["plugins"].Values, restored.Information["plugins"].Values);
        string converted = output + ".converted.json";
        ManifestConverter.Convert(output, converted);
        Assert.Equal(identification.Detections, FileComparer.ReadInputs([converted]).Sources[0].Scan!.GameDetections);
        string exported = output + ".exported.json";
        ManifestConverter.Export(imported, exported);
        var reimported = FileComparer.ReadInputs([exported]);
        Assert.Equal(identification.Detections, reimported.Sources[0].Scan!.GameDetections);
        Assert.Equal("hk4e_cn", reimported.Sources[0].Metadata!["biz"].Value);
        Assert.Equal(imported.Files.Values, reimported.Files.Values);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("ini_version", "invalid")]
    [InlineData("unknown", "value")]
    [InlineData("ini_plugin_version", "1.0.0")]
    [InlineData("ini_plugin_version:", "1.0.0")]
    [InlineData("ini_plugin_version:bad id", "1.0.0")]
    [InlineData("ini_plugin_version:bad=id", "1.0.0")]
    [InlineData("ini_plugin_version:   ", "1.0.0")]
    [InlineData("ini_plugin_version:bad\nname", "1.0.0")]
    public void InvalidRecordedDetectionsAreRejected(string key, string value)
    {
        Write(Sample);
        var recorded = new Dictionary<string, string>(GameIdentifier.Identify(root,
            TestContext.Current.CancellationToken).Detections) { [key] = value };
        Assert.Throws<CoreException>(() => GameIdentifier.FromDetections(recorded));
    }

    [Fact]
    public void CustomConfigurationSurvivesScanningExportAndExplicitRestoration()
    {
        Write("identity=future\nrelease=1.2.0", "future.txt");
        var config = new IdentificationConfiguration<GameDetectionDefinition>(new()
        {
            ["future_id"] = new(["future.txt"], @"^identity=(?<value>[^\r\n]+)", false,
                Meanings: new() { ["biz"] = new(FromDetection: true, LabelTextId: "IdentificationLabelGame", Required: true) }),
            ["future_version"] = new(["future.txt"], @"^release=(?<value>[^\r\n]+)", true,
                Meanings: new() { ["version"] = new(FromDetection: true, LabelTextId: "IdentificationLabelVersion", Required: true) })
        });
        var identification = GameIdentifier.Identify(root, config, TestContext.Current.CancellationToken);
        var snapshot = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        Assert.Equal("future", snapshot.Sources[0].Scan!.GameDetections!["future_id"]);
        string output = root + ".json";
        DirectoryScanner.Scan(root, output, cancellationToken: TestContext.Current.CancellationToken,
            identification: identification);
        var recorded = FileComparer.ReadInputs([output]).Sources[0].Scan!.GameDetections!;
        Assert.Equal("1.2.0", Assert.Single(GameIdentifier.FromDetections(recorded, config)
            .Information["version"].Values).RawValue);
    }

    [Theory]
    [InlineData("future id")]
    [InlineData("future=id")]
    [InlineData("future\nid")]
    [InlineData("future:id")]
    public void UnpersistableDetectionIdsFailAtConfigurationEntry(string id)
    {
        var config = GameIdentifier.LoadConfiguration();
        config.Detections.Add(id, new(["missing.ini"], @"(?<value>.+)", false));
        Assert.Equal(CoreErrorCode.GameIdentificationConfigurationInvalid,
            Assert.Throws<CoreException>(() => GameIdentifier.Identify(root, config,
                 TestContext.Current.CancellationToken)).Code);
    }

    [Theory]
    [InlineData("", "", null)]
    [InlineData("1", "1", "official")]
    [InlineData("14", "0", "bilibili")]
    [InlineData("1", "2", null)]
    [InlineData("2", "1", null)]
    [InlineData("9", "42", null)]
    public void ChannelSwitchesDoNotGateIdentityOrInventManifestDifferences(string channel, string subChannel, string? expectedId)
    {
        Write("uapc={\"hk4e_cn\":{}}\nchannel=" + channel + "\nsub_channel=" + subChannel);
        // Absent fields are unknown; empty matched values remain invalid input.
        if (channel.Length == 0) Write("uapc={\"hk4e_cn\":{}}");
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.Equal(expectedId, result.Information.GetValueOrDefault("channel")?.Values.Single().RawValue);
        Assert.False(result.Information.ContainsKey("version"));
        Assert.False(result.Information.ContainsKey("plugins"));
        Assert.Equal(channel.Length == 0 ? null : channel, result.Detections.GetValueOrDefault("ini_channel"));
        Assert.Equal(subChannel.Length == 0 ? null : subChannel, result.Detections.GetValueOrDefault("ini_sub_channel"));
    }

    [Theory]
    [InlineData("YuanShen.exe", "hk4e", "hk4e_cn")]
    [InlineData("GenshinImpact.exe", "hk4e", "hk4e_global")]
    [InlineData("StarRail.exe", "hkrpg", null)]
    [InlineData("ZenlessZoneZero.exe", "nap", null)]
    [InlineData("BH3.exe", "bh3", null)]
    public void RootExecutableIdentifiesOnlyWhatItsOwnRuleProves(string executable, string game, string? biz)
    {
        string path = Write("not text", executable);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal(game, Assert.Single(result.Information["game"].Values).RawValue);
        Assert.Equal(biz, result.Information.GetValueOrDefault("biz")?.Values.Single().RawValue);
        Assert.False(result.Information.ContainsKey("version"));
        Assert.Equal("game", Assert.Single(result.Information["package"].Values).RawValue);
    }

    [Theory]
    [InlineData("YuanShen_Data/StreamingAssets/AudioAssets/", "Chinese", "English(US)", "Japanese", "Korean", "hk4e", "hk4e_cn")]
    [InlineData("GenshinImpact_Data/StreamingAssets/AudioAssets/", "Chinese", "English(US)", "Japanese", "Korean", "hk4e", "hk4e_global")]
    [InlineData("StarRail_Data/Persistent/Audio/AudioPackage/Windows/", "Chinese(PRC)", "English", "Japanese", "Korean", "hkrpg", null)]
    public void VoicePathsIdentifyMultipleLanguagesAndRestoreWithoutReadingResourceBodies(string prefix,
        string zh, string en, string ja, string ko, string game, string? biz)
    {
        foreach (string directory in new[] { zh, en, ja, ko })
        {
            string folder = Path.Combine(root, prefix, directory, "nested");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "voice.pck"), [0xff, 0xfe, 0x00]);
        }
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.Equal(game, Assert.Single(result.Information["game"].Values).RawValue);
        Assert.Equal(biz, result.Information.GetValueOrDefault("biz")?.Values.Single().RawValue);
        Assert.Equal(new[] { "en-us", "ja-jp", "ko-kr", "zh-cn" },
            result.Information["package"].Values.Select(value => value.RawValue).Order());
        Directory.Move(root, root + "-moved");
        var restored = GameIdentifier.FromDetections(result.Detections);
        Assert.Equal(result.Information["package"].Values, restored.Information["package"].Values);
        Assert.Equal(result.Detections, restored.Detections);
    }

    [Fact]
    public void EmptyDirectoriesBackupsAndSimilarPathNamesDoNotProveResources()
    {
        Write(Sample);
        Directory.CreateDirectory(Path.Combine(root, "YuanShen_Data/StreamingAssets/AudioAssets/Chinese"));
        string[] paths = ["Backup/YuanShen.exe", "YuanShen.exe.bak",
            "Backup/YuanShen_Data/StreamingAssets/AudioAssets/Japanese/voice.pck",
            "YuanShen_Data/StreamingAssets/AudioAssets/ChineseOther/voice.pck"];
        foreach (string path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            File.WriteAllText(Path.Combine(root, path), "resource");
        }
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.False(result.Information.ContainsKey("package"));
        Assert.False(result.Information.ContainsKey("game"));
    }

    [Fact]
    public void ResourceIdentificationUsesExistingLightScanInsteadOfReadingDirectoriesAgain()
    {
        Write(Sample);
        string folder = Path.Combine(root, "YuanShen_Data/StreamingAssets/AudioAssets/Chinese");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "voice.pck"), "resource");
        var scan = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, scan.HashedFileCount);
        Directory.Move(folder, folder + "-moved");
        var result = GameIdentifier.Identify(root, GameIdentifier.LoadConfiguration(),
            TestContext.Current.CancellationToken, scan.Files.Keys);
        Assert.Equal("zh-cn", Assert.Single(result.Information["package"].Values).RawValue);
    }

    [Fact]
    public void DiscoveredPackagesIncludeMainVoiceAndConfiguredNonVoicePackages()
    {
        Write(Sample);
        var configuration = GameIdentifier.LoadConfiguration();
        configuration.Detections["extra_package"] = new(["Extra/"], Prefix: true,
            Meanings: new() { ["package"] = new("extra", LabelTextId: "IdentificationLabelPackages", Multiple: true) });
        string[] paths = ["YuanShen.exe", "YuanShen_Data/StreamingAssets/AudioAssets/Chinese/voice.pck", "Extra/file.bin"];
        var result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken, paths);
        Assert.Equal(new[] { "extra", "game", "zh-cn" },
            result.Information["package"].Values.Select(value => value.RawValue).Order());
        var restored = GameIdentifier.FromDetections(result.Detections, configuration);
        Assert.Equal(result.Information["package"].Values, restored.Information["package"].Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludedIniDoesNotContributeIdentityOrVersionInEitherScanMode(bool hashes)
    {
        Write("uapc={\"other_game\":{}}\ngame_version=invalid");
        Write("game", "YuanShen.exe");
        var scan = DirectoryScanner.Scan(root, excludedFiles: ["config.ini"], computeHashes: hashes,
            cancellationToken: TestContext.Current.CancellationToken, identifyGame: true);
        var result = GameIdentifier.FromDetections(scan.Sources[0].Scan!.GameDetections!);
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
        Assert.False(result.Information.ContainsKey("version"));
        Assert.False(result.Detections.ContainsKey("ini_game_id"));
        Assert.Single(scan.Files);
    }

    [Fact]
    public void TextCandidatesInExcludedSubdirectoriesAreNotOpened()
    {
        Write("game", "YuanShen.exe");
        Directory.CreateDirectory(Path.Combine(root, "Excluded"));
        string candidate = Write("game_version=invalid", "Excluded/config.ini");
        var configuration = GameIdentifier.LoadConfiguration();
        configuration.Detections["ini_version"] = configuration.Detections["ini_version"] with { Paths = ["Excluded/config.ini"] };
        var scan = DirectoryScanner.Scan(root, excludedDirectories: ["Excluded"], cancellationToken: TestContext.Current.CancellationToken);
        using var locked = new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken, scan.Files.Keys);
        Assert.False(result.Information.ContainsKey("version"));
        Assert.Equal("hk4e_cn", Assert.Single(result.Information["biz"].Values).RawValue);
    }

    [Fact]
    public void TextCandidatesUnderSkippedDirectoryLinksDoNotContributeEvidence()
    {
        Write("game", "YuanShen.exe");
        Directory.CreateSymbolicLink(Path.Combine(root, "Linked"), root);
        var configuration = GameIdentifier.LoadConfiguration();
        configuration.Detections["ini_version"] = configuration.Detections["ini_version"] with { Paths = ["Linked/config.ini"] };
        var scan = DirectoryScanner.Scan(root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Linked", scan.Sources[0].Scan!.SkippedLinks);
        Assert.False(GameIdentifier.Identify(root, configuration, TestContext.Current.CancellationToken,
            scan.Files.Keys).Information.ContainsKey("version"));
    }

    [Fact]
    public void FilePathIdentityConflictsWithDifferentRecordedBizRatherThanOverridingIt()
    {
        Write(Sample);
        Write("game", "GenshinImpact.exe");
        Assert.Equal(CoreErrorCode.GameDetectionConflict, Assert.Throws<CoreException>(() =>
            GameIdentifier.Identify(root, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void PathEvidenceDoesNotFollowResourceLinksAndRejectsUnsafeRecordedPaths()
    {
        Write(Sample);
        string voice = Path.Combine(root, "YuanShen_Data/StreamingAssets/AudioAssets/Chinese");
        Directory.CreateDirectory(voice);
        string target = Write("outside", "target.pck");
        File.CreateSymbolicLink(Path.Combine(voice, "linked.pck"), target);
        var result = GameIdentifier.Identify(root, TestContext.Current.CancellationToken);
        Assert.False(result.Information.ContainsKey("package"));
        var detections = new Dictionary<string, string>(result.Detections)
        {
            ["voice_hk4e_cn_zh"] = "YuanShen_Data/StreamingAssets/AudioAssets/Chinese/../outside.pck"
        };
        Assert.Throws<CoreException>(() => GameIdentifier.FromDetections(detections));
    }
}
