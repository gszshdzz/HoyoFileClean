param(
    [Parameter(Mandatory)]
    [ValidateSet('Core', 'Desktop', 'All')]
    [string] $Suite,
    [string[]] $Class = @(),
    [switch] $Full,
    [switch] $Explicit,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
if ($Full -and $Class.Count) { throw '-Full 和 -Class 不能同时指定。' }
if (!$Full -and !$Class.Count) { throw '请用 -Class 指定相关测试类；全量回归必须明确指定 -Full。' }
if ($Explicit -and $Full) { throw '联网和真实样本测试必须用 -Class 明确选择，不能随全量回归启动。' }
if ($Suite -eq 'All' -and !$Full) { throw '按需测试请指定 Core 或 Desktop；All 仅用于明确的全量回归。' }

$root = Split-Path $PSScriptRoot -Parent
$suites = if ($Suite -eq 'All') { @('Core', 'Desktop') } else { @($Suite) }
foreach ($item in $suites) {
    $project = if ($item -eq 'Core') {
        Join-Path $root 'tests\HoyoFileClean.Tests.csproj'
    } else {
        Join-Path $root 'desktop.tests\HoyoFileClean.Desktop.Tests.csproj'
    }
    if (!(Test-Path $project -PathType Leaf)) { throw "测试项目不存在：$project" }
    $arguments = @('run', '--project', $project, '--no-restore')
    if ($NoBuild) { $arguments += '--no-build' }
    $arguments += @('--', '-explicit', $(if ($Explicit) { 'only' } else { 'off' }))
    foreach ($name in $Class) {
        if ([string]::IsNullOrWhiteSpace($name)) { throw '测试类名不能为空。' }
        $qualified = if ($name.Contains('.')) { $name } elseif ($item -eq 'Core') {
            "HoyoFileClean.Tests.$name"
        } else { "HoyoFileClean.Desktop.Tests.$name" }
        $arguments += @('-class', $qualified)
    }
    & dotnet @arguments | Tee-Object -Variable runnerOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (($runnerOutput -join "`n") -match '\bTotal:\s+0\b') {
        throw '没有选中任何测试，请检查 -Class 类名和 -Explicit 选择。'
    }
}
