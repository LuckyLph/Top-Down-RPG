[CmdletBinding()]
param(
    [ValidateSet('All', 'EditMode', 'PlayMode', 'Smoke')]
    [string]$Mode = 'All',
    [string]$UnityPath,
    [string]$TestFilter,
    [ValidateRange(1, 600)]
    [int]$SmokeTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ResultsRoot = Join-Path $ProjectRoot 'TestResults\Phase01'
$BuildRoot = Join-Path $ProjectRoot 'Builds\Phase01'

function Get-UnityExecutablePath
{
    param([string]$ExplicitPath)

    $candidate = if ([string]::IsNullOrWhiteSpace($ExplicitPath))
    {
        $env:UNITY_EDITOR_PATH
    }
    else
    {
        $ExplicitPath
    }

    if ([string]::IsNullOrWhiteSpace($candidate))
    {
        return $null
    }

    try
    {
        $resolved = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path
        if ((Test-Path -LiteralPath $resolved -PathType Leaf) -and [System.IO.Path]::GetExtension($resolved) -eq '.exe')
        {
            return $resolved
        }
    }
    catch
    {
    }

    return $null
}

function Assert-TestFilter
{
    param([string]$Filter)

    if (-not [string]::IsNullOrWhiteSpace($Filter) -and $Filter -notmatch '^[A-Za-z0-9_.]+$')
    {
        throw 'TestFilter may contain only letters, digits, underscores, and periods.'
    }
}

function ConvertTo-QuotedArgument
{
    param([string]$Value)

    return '"' + $Value.Replace('"', '\"') + '"'
}

function New-VerificationOutcome
{
    param(
        [string]$Channel,
        [string]$EvidencePath,
        [string[]]$DiagnosticPaths
    )

    return [pscustomobject]@{
        Channel = $Channel
        Passed = $false
        NativeUnityExitCode = $null
        EvidencePath = $EvidencePath
        DiagnosticPaths = ($DiagnosticPaths -join '; ')
        Reason = ''
    }
}

function Invoke-UnityProcess
{
    param(
        [string]$ExecutablePath,
        [string[]]$Arguments,
        [string]$StandardOutputPath,
        [string]$StandardErrorPath
    )

    if ([string]::IsNullOrWhiteSpace($ExecutablePath))
    {
        return [pscustomobject]@{
            ExitCode = $null
            Reason = 'Unity executable is unavailable. Pass -UnityPath or set UNITY_EDITOR_PATH.'
        }
    }

    try
    {
        $process = Start-Process -FilePath $ExecutablePath `
            -ArgumentList ($Arguments -join ' ') `
            -RedirectStandardOutput $StandardOutputPath `
            -RedirectStandardError $StandardErrorPath `
            -Wait `
            -PassThru `
            -NoNewWindow

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Reason = ''
        }
    }
    catch
    {
        return [pscustomobject]@{
            ExitCode = $null
            Reason = $_.Exception.Message
        }
    }
}

function Test-UnityReport
{
    param([string]$ReportPath)

    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf))
    {
        return [pscustomobject]@{ Passed = $false; Reason = 'Expected XML report was not created.' }
    }

    try
    {
        [xml]$xml = Get-Content -LiteralPath $ReportPath -Raw -ErrorAction Stop
        $testRun = $xml.SelectSingleNode('/test-run')
        if ($null -eq $testRun)
        {
            return [pscustomobject]@{ Passed = $false; Reason = 'XML report does not contain a test-run element.' }
        }

        if ($testRun.GetAttribute('result') -ne 'Passed')
        {
            return [pscustomobject]@{ Passed = $false; Reason = "XML test-run result is '$($testRun.GetAttribute('result'))'." }
        }

        return [pscustomobject]@{ Passed = $true; Reason = '' }
    }
    catch
    {
        return [pscustomobject]@{ Passed = $false; Reason = "XML report could not be parsed: $($_.Exception.Message)" }
    }
}

function Invoke-UnityTestChannel
{
    param(
        [ValidateSet('EditMode', 'PlayMode')]
        [string]$Platform,
        [string]$ExecutablePath,
        [string]$Filter
    )

    $channelDirectory = Join-Path $ResultsRoot $Platform
    New-Item -ItemType Directory -Force -Path $channelDirectory | Out-Null

    $reportFileName = if ([string]::IsNullOrWhiteSpace($Filter))
    {
        'TestResults.xml'
    }
    else
    {
        "$($Filter.Split('.')[-1]).xml"
    }

    $reportPath = Join-Path $channelDirectory $reportFileName
    $unityLogPath = Join-Path $channelDirectory 'Unity.log'
    $stdoutPath = Join-Path $channelDirectory 'Unity.stdout.log'
    $stderrPath = Join-Path $channelDirectory 'Unity.stderr.log'
    $outcome = New-VerificationOutcome -Channel $Platform -EvidencePath $reportPath -DiagnosticPaths @($unityLogPath, $stdoutPath, $stderrPath)

    $arguments = @(
        '-batchmode',
        '-nographics',
        '-projectPath', (ConvertTo-QuotedArgument $ProjectRoot),
        '-runTests',
        '-testPlatform', $Platform,
        '-testResults', (ConvertTo-QuotedArgument $reportPath),
        '-logFile', (ConvertTo-QuotedArgument $unityLogPath)
    )

    if (-not [string]::IsNullOrWhiteSpace($Filter))
    {
        $arguments += @('-testFilter', (ConvertTo-QuotedArgument $Filter))
    }

    $processResult = Invoke-UnityProcess -ExecutablePath $ExecutablePath -Arguments $arguments -StandardOutputPath $stdoutPath -StandardErrorPath $stderrPath
    $outcome.NativeUnityExitCode = $processResult.ExitCode
    $reportResult = Test-UnityReport -ReportPath $reportPath

    $outcome.Passed = $processResult.ExitCode -eq 0 -and $reportResult.Passed
    $outcome.Reason = if (-not [string]::IsNullOrWhiteSpace($processResult.Reason))
    {
        $processResult.Reason
    }
    elseif (-not $reportResult.Passed)
    {
        $reportResult.Reason
    }
    else
    {
        ''
    }

    return $outcome
}

function Invoke-DevelopmentBuildSmoke
{
    param([string]$ExecutablePath)

    $channelDirectory = Join-Path $ResultsRoot 'Smoke'
    New-Item -ItemType Directory -Force -Path $channelDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null

    $buildResultPath = Join-Path $channelDirectory 'BuildResult.json'
    $unityLogPath = Join-Path $channelDirectory 'Unity.log'
    $stdoutPath = Join-Path $channelDirectory 'Unity.stdout.log'
    $stderrPath = Join-Path $channelDirectory 'Unity.stderr.log'
    $playerLogPath = Join-Path $channelDirectory 'Player.log'
    $playerStdoutPath = Join-Path $channelDirectory 'Player.stdout.log'
    $playerStderrPath = Join-Path $channelDirectory 'Player.stderr.log'
    $buildPath = Join-Path $BuildRoot 'TopDownRPGPhase01.exe'
    $outcome = New-VerificationOutcome -Channel 'Smoke' -EvidencePath $buildResultPath -DiagnosticPaths @($unityLogPath, $stdoutPath, $stderrPath, $playerLogPath, $playerStdoutPath, $playerStderrPath)

    $arguments = @(
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath', (ConvertTo-QuotedArgument $ProjectRoot),
        '-executeMethod', 'SliceVerificationCommands.BuildPhase01DevelopmentPlayer',
        '-logFile', (ConvertTo-QuotedArgument $unityLogPath)
    )

    $processResult = Invoke-UnityProcess -ExecutablePath $ExecutablePath -Arguments $arguments -StandardOutputPath $stdoutPath -StandardErrorPath $stderrPath
    $outcome.NativeUnityExitCode = $processResult.ExitCode

    $buildSucceeded = $false
    $buildReason = ''
    if (-not (Test-Path -LiteralPath $buildResultPath -PathType Leaf))
    {
        $buildReason = 'Build result evidence was not created.'
    }
    else
    {
        try
        {
            $buildResult = Get-Content -LiteralPath $buildResultPath -Raw | ConvertFrom-Json -ErrorAction Stop
            if ($buildResult.result -eq 'Succeeded')
            {
                $buildSucceeded = $true
            }
            else
            {
                $buildReason = "Build result is '$($buildResult.result)': $($buildResult.message)"
            }
        }
        catch
        {
            $buildReason = "Build result evidence could not be parsed: $($_.Exception.Message)"
        }
    }

    $smokePassed = $false
    if ($processResult.ExitCode -eq 0 -and $buildSucceeded -and (Test-Path -LiteralPath $buildPath -PathType Leaf))
    {
        try
        {
            $playerProcess = Start-Process -FilePath $buildPath `
                -ArgumentList ('-logFile ' + (ConvertTo-QuotedArgument $playerLogPath)) `
                -RedirectStandardOutput $playerStdoutPath `
                -RedirectStandardError $playerStderrPath `
                -PassThru
            $completed = $playerProcess.WaitForExit($SmokeTimeoutSeconds * 1000)
            if (-not $completed)
            {
                Stop-Process -Id $playerProcess.Id -Force -ErrorAction SilentlyContinue
                $buildReason = "Smoke timed out after $SmokeTimeoutSeconds seconds without a SMOKE-PASS sentinel."
            }
            elseif (Test-Path -LiteralPath $playerLogPath -PathType Leaf)
            {
                $smokePassed = Select-String -LiteralPath $playerLogPath -Pattern 'SMOKE-PASS' -Quiet
                if (-not $smokePassed)
                {
                    $buildReason = 'Player log did not contain the SMOKE-PASS sentinel.'
                }
            }
            else
            {
                $buildReason = 'Player log was not created.'
            }
        }
        catch
        {
            $buildReason = "Player smoke could not start: $($_.Exception.Message)"
        }
    }
    elseif ([string]::IsNullOrWhiteSpace($buildReason))
    {
        $buildReason = 'Development Player was not produced.'
    }

    $outcome.Passed = $processResult.ExitCode -eq 0 -and $buildSucceeded -and $smokePassed
    $outcome.Reason = if (-not [string]::IsNullOrWhiteSpace($processResult.Reason)) { $processResult.Reason } else { $buildReason }
    return $outcome
}

try
{
    Assert-TestFilter -Filter $TestFilter
}
catch
{
    Write-Error $_.Exception.Message
    exit 1
}

$unityExecutable = Get-UnityExecutablePath -ExplicitPath $UnityPath
$outcomes = @()

switch ($Mode)
{
    'All'
    {
        $outcomes += Invoke-UnityTestChannel -Platform 'EditMode' -ExecutablePath $unityExecutable -Filter $TestFilter
        $outcomes += Invoke-UnityTestChannel -Platform 'PlayMode' -ExecutablePath $unityExecutable -Filter $TestFilter
        $outcomes += Invoke-DevelopmentBuildSmoke -ExecutablePath $unityExecutable
    }
    'EditMode'
    {
        $outcomes += Invoke-UnityTestChannel -Platform 'EditMode' -ExecutablePath $unityExecutable -Filter $TestFilter
    }
    'PlayMode'
    {
        $outcomes += Invoke-UnityTestChannel -Platform 'PlayMode' -ExecutablePath $unityExecutable -Filter $TestFilter
    }
    'Smoke'
    {
        $outcomes += Invoke-DevelopmentBuildSmoke -ExecutablePath $unityExecutable
    }
}

$outcomes | Format-Table Channel, Passed, NativeUnityExitCode, EvidencePath, DiagnosticPaths, Reason -AutoSize

if ($outcomes.Passed -contains $false)
{
    exit 1
}
