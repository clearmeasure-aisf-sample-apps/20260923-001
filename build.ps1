#!/usr/bin/env pwsh
. .\BuildFunctions.ps1

# Clean environment variables that may interfere with local builds
if ($env:ConnectionStrings__SqlConnectionString -and -not (Test-IsGitHubActions)) {
	$env:ConnectionStrings__SqlConnectionString = $null
	[Environment]::SetEnvironmentVariable("ConnectionStrings__SqlConnectionString", $null, "User")
}

$projectName = "ChurchBulletin"
$base_dir = resolve-path .\
$source_dir = Join-Path $base_dir "src"
$solutionName = Join-Path $source_dir "$projectName.sln"
$unitTestProjectPath = Join-Path $source_dir "UnitTests"
$integrationTestProjectPath = Join-Path $source_dir "IntegrationTests"
$acceptanceTestProjectPath = Join-Path $source_dir "AcceptanceTests"
$uiProjectPath =  Join-PathSegments $source_dir "UI" "Server"
$databaseProjectPath = Join-Path $source_dir "Database"
$projectConfig = $env:BuildConfiguration
$framework = "net10.0"
$version = $env:BUILD_BUILDNUMBER

$verbosity = "minimal"

$build_dir = Join-Path $base_dir "build"
$test_dir = Join-Path $build_dir "test"
$coverletRunSettings = Join-Path $base_dir "coverlet.runsettings"

$databaseAction = $env:DatabaseAction
if ([string]::IsNullOrEmpty($databaseAction)) { $databaseAction = "Update" }

if (Test-IsArmArchitecture) {
	$env:database_engine = "SQLite"
	$env:DATABASE_ENGINE = "SQLite"
}
$script:databaseEngine = $env:DATABASE_ENGINE

$databaseName = $projectName

$script:databaseServer = $databaseServer;
$script:databaseScripts = Join-PathSegments $source_dir "Database" "scripts"

if ([string]::IsNullOrEmpty($version)) { $version = "1.0.0" }
if ([string]::IsNullOrEmpty($projectConfig)) { $projectConfig = "Release" }

# ── Main Functions ──────────────────────────────────────────────────────────────

Function Init {
	$pwshPath = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
	if (-not $pwshPath) {
		throw "PowerShell 7 is required to run this build script."
	}

	Initialize-SqlServerModule

	if (Test-IsLinux) {
		# A cache location that is already set is kept: /tmp can be a small, quota-limited tmpfs.
		if (-not (Test-IsGitHubActions) -and [string]::IsNullOrEmpty($env:NUGET_PACKAGES)) {
			$env:NUGET_PACKAGES = "/tmp/nuget-packages"
		}
	}

	if ([string]::IsNullOrEmpty($script:databaseServer)) {
		$script:databaseServer = Get-DefaultDatabaseServer -engine $script:databaseEngine
	}

	switch ($script:databaseEngine) {
		"SQL-Container" {
			# SQL_EXTERNAL=true => connect to an already-running SQL Server (a k8s
			# sidecar or a shared server) instead of starting a local Docker
			# container, so no Docker daemon is required in the build environment.
			if ($env:SQL_EXTERNAL -ne "true" -and -not (Test-IsDockerRunning)) {
				throw "Docker is not running. Start Docker (for example, Docker Desktop) so the container-based SQL Server required for 'SQL-Container' builds can run, then rerun this build script."
			}
		}
		"SQLite" {
			if ([string]::IsNullOrEmpty($env:ConnectionStrings__SqlConnectionString)) {
				$env:ConnectionStrings__SqlConnectionString = "Data Source=ChurchBulletin.db"
			}
		}
	}

	if (Test-Path "build") {
		Remove-Item -Path "build" -Recurse -Force
	}

	New-Item -Path $build_dir -ItemType Directory -Force | Out-Null

	if ($script:skipClean) {
		Log-Message -Message "Skipping dotnet clean (ACCEPTANCE_SKIP_CLEAN=true); Compile will build incrementally." -Type "INFO"
	}
	else {
		exec {
			& dotnet clean $solutionName -nologo -v $verbosity /p:SuppressNETCoreSdkPreviewMessage=true
		}
	}

	exec {
		& dotnet restore $solutionName -nologo --interactive -v $verbosity /p:SuppressNETCoreSdkPreviewMessage=true
	}
}

Function Compile {
	# --no-incremental forces a full rebuild; dropped only when Init skipped the clean
	# (ACCEPTANCE_SKIP_CLEAN=true), so MSBuild reuses up-to-date outputs from this checkout.
	$incrementalArgs = @()
	if (-not $script:skipClean) {
		$incrementalArgs = @("--no-incremental")
	}
	exec {
		& dotnet build $solutionName -nologo --no-restore -v `
			$verbosity -maxcpucount --configuration $projectConfig @incrementalArgs `
			/p:TreatWarningsAsErrors="true" `
			/p:MSBuildTreatAllWarningsAsErrors="true" `
			/p:SuppressNETCoreSdkPreviewMessage=true `
			/p:Version=$version /p:Authors="Programming with Palermo" `
			/p:Product="Church Bulletin"
	}
}

Function UnitTests {
	Push-Location -Path $unitTestProjectPath

	try {
		exec {
			& dotnet test /p:CopyLocalLockFileAssemblies=true -nologo -v $verbosity --logger:trx `
				--results-directory $(Join-Path $test_dir "UnitTests") --no-build `
				--no-restore --configuration $projectConfig `
				--settings:$coverletRunSettings `
				--collect:"XPlat Code Coverage"
		}
	}
	finally {
		Pop-Location
	}
}

Function Setup-DatabaseForBuild {
	if ($script:databaseEngine -eq "SQL-Container") {
		# With an external/shared SQL Server (SQL_EXTERNAL=true) the server already
		# exists, so skip creating a per-build Docker container. New-SqlServerDatabase
		# still runs, dropping and recreating THIS build's database on that server -
		# so every PrivateBuild starts from a clean, freshly-migrated database.
		if ($env:SQL_EXTERNAL -ne "true") {
			New-DockerContainerForSqlServer -containerName $(Get-ContainerName $script:databaseName)
		}
		New-SqlServerDatabase -serverName $script:databaseServer -databaseName $script:databaseName
		$containerName = Get-ContainerName -DatabaseName $script:databaseName
		$sqlPassword = Get-SqlServerPassword -ContainerName $containerName
		$env:ConnectionStrings__SqlConnectionString = New-SqlServerConnectionString -server $script:databaseServer -database $script:databaseName -password $sqlPassword
		MigrateDatabaseLocal -databaseServerFunc $script:databaseServer -databaseNameFunc $script:databaseName
	}
	elseif ($script:databaseEngine -eq "LocalDB") {
		$env:ConnectionStrings__SqlConnectionString = New-IntegratedConnectionString -server $script:databaseServer -database $script:databaseName
		MigrateDatabaseLocal -databaseServerFunc $script:databaseServer -databaseNameFunc $script:databaseName
	}
}

Function IntegrationTest {
	Push-Location -Path $integrationTestProjectPath

	try {
		exec {
				if ($script:useSqlite) {
				& dotnet test /p:CopyLocalLockFileAssemblies=true -nologo -v $verbosity --logger:trx `
					--results-directory $(Join-Path $test_dir "IntegrationTests") --no-build `
					--no-restore --configuration $projectConfig `
					--settings:$coverletRunSettings `
					--collect:"XPlat Code Coverage" `
					--filter "Category!=SqlServerOnly"
			}
			else {
				& dotnet test /p:CopyLocalLockFileAssemblies=true -nologo -v $verbosity --logger:trx `
					--results-directory $(Join-Path $test_dir "IntegrationTests") --no-build `
					--no-restore --configuration $projectConfig `
					--settings:$coverletRunSettings `
					--collect:"XPlat Code Coverage"
			}
		}
	}
	finally {
		Pop-Location
	}
}

Function Test-SerialTestsRequested {
	return ($env:BUILD_SERIAL_TESTS -eq "true")
}

Function Invoke-UnitAndIntegrationTests {
	# Runs UnitTests concurrently with Setup-DatabaseForBuild + IntegrationTest.
	# Set BUILD_SERIAL_TESTS=true to run them one after the other (debugging).
	if (Test-SerialTestsRequested) {
		Log-Message -Message "BUILD_SERIAL_TESTS=true - running unit and integration tests serially" -Type "INFO"
		UnitTests
		Setup-DatabaseForBuild
		IntegrationTest
		return
	}

	$unitResultsDir = Join-Path $test_dir "UnitTests"
	$unitArgs = @(
		"test", "/p:CopyLocalLockFileAssemblies=true", "-nologo", "-v", $verbosity, "--logger:trx",
		"--results-directory", $unitResultsDir, "--no-build",
		"--no-restore", "--configuration", $projectConfig,
		"--settings:$coverletRunSettings",
		"--collect:XPlat Code Coverage"
	)

	Log-Message -Message "Starting unit tests in background (parallel with integration tests)" -Type "INFO"
	$unitJob = Start-Job -ScriptBlock {
		param($workDir, $testArgs)
		Set-Location -LiteralPath $workDir
		& dotnet @testArgs 2>&1 | ForEach-Object { "[UnitTests] $_" }
		"__EXITCODE__:$LASTEXITCODE"
	} -ArgumentList $unitTestProjectPath, $unitArgs

	$integrationError = $null
	try {
		Setup-DatabaseForBuild
		IntegrationTest
	}
	catch {
		$integrationError = $_
	}

	Wait-Job -Job $unitJob | Out-Null
	$unitOutput = @(Receive-Job -Job $unitJob -ErrorAction Continue)
	$unitJobState = $unitJob.State
	Remove-Job -Job $unitJob -Force

	$unitExitCode = -1
	foreach ($line in $unitOutput) {
		$text = [string]$line
		if ($text -match '^__EXITCODE__:(-?\d+)$') {
			$unitExitCode = [int]$Matches[1]
		}
		else {
			Write-Host $text
		}
	}

	$failures = @()
	if ($unitJobState -ne "Completed" -or $unitExitCode -ne 0) {
		$failures += "Unit tests failed (exit code $unitExitCode, job state $unitJobState)"
	}
	if ($null -ne $integrationError) {
		$failures += "Integration tests failed: $integrationError"
	}
	if ($failures.Count -gt 0) {
		throw ($failures -join [Environment]::NewLine)
	}
}

Function Package-Everything{

	# Allow Octopus.DotNet.Cli (targets net6.0) to run on the current .NET SDK
	$env:DOTNET_ROLL_FORWARD = "LatestMajor"

	dotnet tool install --global Octopus.DotNet.Cli 2>$null # prevents red 'already installed' message

	# Ensure dotnet tools are in PATH
	$dotnetToolsPath = [System.IO.Path]::Combine([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile), ".dotnet", "tools")
	$pathEntries = $env:PATH -split [System.IO.Path]::PathSeparator
	$dotnetToolsPathPresent = $pathEntries | Where-Object { $_.Trim().ToLowerInvariant() -eq $dotnetToolsPath.Trim().ToLowerInvariant() }
	if (-not $dotnetToolsPathPresent) {
		$env:PATH = "$dotnetToolsPath$([System.IO.Path]::PathSeparator)$env:PATH"
	}

	PackageUI
	PackageDatabase
	PackageAcceptanceTests
	PackageScript
}

Function Build {
	param (
		[Parameter(Mandatory = $false)]
		[string]$databaseServer = "",

		[Parameter(Mandatory = $false)]
		[string]$databaseName = "",

		[Parameter(Mandatory = $false)]
		[switch]$UseSqlite
	)

	if ($UseSqlite) {
		$script:databaseEngine = "SQLite"
	}

	Resolve-DatabaseEngine

	if ($script:databaseEngine -ne "SQLite") {
		if (-not [string]::IsNullOrEmpty($databaseServer)) {
			$script:databaseServer = $databaseServer
		}
		$script:databaseName = Get-ResolvedDatabaseName -explicitName $databaseName -baseName $projectName -onLinux (Test-IsLinux) -localBuild (Test-IsLocalBuild)
	}

	$script:buildStopwatch = [Diagnostics.Stopwatch]::StartNew()

	Init
	Compile
	Invoke-UnitAndIntegrationTests

	$script:buildStopwatch.Stop()
	$elapsed = $script:buildStopwatch.Elapsed.ToString()
	if ($script:databaseEngine -eq "SQLite") {
		Log-Message -Message "BUILD SUCCEEDED (SQLite) - Build time: $elapsed" -Type "INFO"
	}
	else {
		Log-Message -Message "BUILD SUCCEEDED - Build time: $elapsed" -Type "INFO"
	}
}

Function Invoke-CIBuild {
	Resolve-DatabaseEngine

	if ($script:databaseEngine -ne "SQLite") {
		$script:databaseName = Get-ResolvedDatabaseName -baseName $projectName -onLinux (Test-IsLinux) -localBuild $false
	}

	$script:buildStopwatch = [Diagnostics.Stopwatch]::StartNew()

	Init
	Compile
	Invoke-UnitAndIntegrationTests

	$script:buildStopwatch.Stop()
	$elapsed = $script:buildStopwatch.Elapsed.ToString()
	if ($script:databaseEngine -eq "SQLite") {
		Log-Message -Message "BUILD SUCCEEDED (SQLite) - Build time: $elapsed" -Type "INFO"
	}
	else {
		Log-Message -Message "BUILD SUCCEEDED - Build time: $elapsed" -Type "INFO"
	}
}

# ── Helper Functions (in call order) ────────────────────────────────────────────

Function Resolve-DatabaseEngine {
	$onLinux = Test-IsLinux
	$dockerAvailable = $false
	if ($onLinux) {
		$dockerAvailable = Test-IsDockerRunning
	}
	$script:databaseEngine = Get-ResolvedDatabaseEngine -currentEngine $script:databaseEngine -onLinux $onLinux -dockerAvailable $dockerAvailable
	$script:useSqlite = ($script:databaseEngine -eq "SQLite")
}

Function MigrateDatabaseLocal {
	param (
	 [Parameter(Mandatory = $true)]
		[ValidateNotNullOrEmpty()]
		[string]$databaseServerFunc,

		[Parameter(Mandatory = $true)]
		[ValidateNotNullOrEmpty()]
		[string]$databaseNameFunc
	)
	$databaseDll = Join-PathSegments $source_dir "Database" "bin" $projectConfig $framework "ClearMeasure.Bootcamp.Database.dll"

	if (Test-IsLinux) {
		$containerName = Get-ContainerName -DatabaseName $databaseNameFunc
		$sqlPassword = Get-SqlServerPassword -ContainerName $containerName
		$dbArgs = @($databaseDll, $databaseAction, $databaseServerFunc, $databaseNameFunc, $script:databaseScripts, "sa", $sqlPassword)
	}
	else {
		$dbArgs = @($databaseDll, $databaseAction, $databaseServerFunc, $databaseNameFunc, $script:databaseScripts)
	}

	& dotnet $dbArgs
	if ($LASTEXITCODE -ne 0) {
		throw "Database migration failed with exit code $LASTEXITCODE"
	}
}

Function PackageUI {
	exec {
		& dotnet publish $uiProjectPath -nologo --no-restore --no-build -v $verbosity --configuration $projectConfig
	}

	exec {
		& dotnet-octo pack --id "$projectName.UI" --version $version --basePath $(Join-PathSegments $uiProjectPath "bin" $projectConfig $framework "publish") --outFolder $build_dir  --overwrite
	}

}

Function PackageDatabase {
	exec {
		& dotnet publish $databaseProjectPath -nologo --no-restore -v $verbosity --configuration Debug
	}
	exec {
		& dotnet-octo pack --id "$projectName.Database" --version $version --basePath $databaseProjectPath --outFolder $build_dir --overwrite
	}

}

Function PackageAcceptanceTests {
	# The package is consumed only on linux-x64 (Octopus step image platform/ci-dotnet, which ships
	# Chromium for Playwright under PLAYWRIGHT_BROWSERS_PATH=/ms-playwright). Microsoft.Playwright
	# needs just .playwright/node/<platform>/node and .playwright/package at runtime; browsers are
	# never part of .playwright. Publishing for one platform drops the other four Node drivers
	# (~410 MB) that the csproj's PlaywrightPlatform=all brings into local build output.
	$playwrightPackagePlatform = "linux-x64"
	$publishDir = Join-PathSegments $acceptanceTestProjectPath "bin" "Debug" $framework "publish"
	$publishedPlaywrightDir = Join-Path $publishDir ".playwright"

	# A stale publish directory would keep drivers from an earlier all-platform publish.
	if (Test-Path $publishedPlaywrightDir) {
		Remove-Item -Path $publishedPlaywrightDir -Recurse -Force
	}

	# Use Debug configuration so full symbols are available to display better error messages in test failures
	exec {
		& dotnet publish $acceptanceTestProjectPath -nologo --no-restore -v $verbosity --configuration Debug -p:PlaywrightPlatform=$playwrightPackagePlatform
	}

	$requiredPlaywrightFiles = @(
		(Join-PathSegments $publishedPlaywrightDir "node" $playwrightPackagePlatform "node"),
		(Join-PathSegments $publishedPlaywrightDir "package" "cli.js"),
		(Join-PathSegments $publishedPlaywrightDir "package" "browsers.json")
	)
	foreach ($requiredFile in $requiredPlaywrightFiles) {
		if (-not (Test-Path $requiredFile)) {
			throw "Playwright runtime file missing from acceptance test publish output: $requiredFile"
		}
	}
	$extraDrivers = Get-ChildItem -Path (Join-Path $publishedPlaywrightDir "node") -Directory | Where-Object { $_.Name -ne $playwrightPackagePlatform }
	if ($extraDrivers) {
		throw "Unexpected Playwright drivers in acceptance test publish output: $($extraDrivers.Name -join ', ')"
	}

	exec {
		& dotnet-octo pack --id "$projectName.AcceptanceTests" --version $version --basePath $publishDir --outFolder $build_dir --overwrite
	}

}

Function PackageScript {
	exec {
		& dotnet publish $uiProjectPath -nologo --no-restore --no-build -v $verbosity --configuration $projectConfig
	}
	exec {
		& dotnet-octo pack --id "$projectName.Script" --version $version --basePath $uiProjectPath --include "*.ps1" --outFolder $build_dir  --overwrite
	}

}

Function AcceptanceTests {
	$projectConfig = "Release"
	Push-Location -Path $acceptanceTestProjectPath

	$playwrightScript = Join-PathSegments "bin" "Release" $framework "playwright.ps1"

	if (Test-Path $playwrightScript) {
		# '--with-deps' installs OS packages through apt-get on Linux; a distribution without it gets the browser only.
		$playwrightInstallArguments = @("install", "chromium", "--with-deps")
		if ((Test-IsLinux) -and -not (Get-Command apt-get -ErrorAction SilentlyContinue)) {
			Log-Message -Message "apt-get not found: installing Playwright chromium without OS dependencies." -Type "WARNING"
			$playwrightInstallArguments = @("install", "chromium")
		}
		& pwsh $playwrightScript @playwrightInstallArguments
		if ($LASTEXITCODE -ne 0) {
			throw "Failed to install Playwright chromium"
		}
	}
	else {
		throw "Playwright script not found at $playwrightScript. Cannot run acceptance tests without the browsers."
	}

	$uiServerProcess = Get-Process -Name "ClearMeasure.Bootcamp.UI.Server" -ErrorAction SilentlyContinue
	if ($uiServerProcess) {
		Log-Message -Message "Warning: ClearMeasure.Bootcamp.UI.Server is already running in background (PID: $($uiServerProcess.Id)). This may interfere with acceptance tests." -Type "WARNING"
	}

	$runSettingsPath = Join-Path $acceptanceTestProjectPath "AcceptanceTests.runsettings"
	try {
		exec {
		& dotnet test /p:CopyLocalLockFileAssemblies=true -nologo -v minimal --logger:trx `
				--results-directory $(Join-Path $test_dir "AcceptanceTests") --no-build `
				--no-restore --configuration $projectConfig `
				--settings:$runSettingsPath
		}
	}
	finally {
		Pop-Location
	}
}

Function Invoke-AcceptanceTests {
	param (
		[Parameter(Mandatory = $false)]
		[string]$databaseServer = "",
		[Parameter(Mandatory=$false)]
		[string]$databaseName ="",

		[Parameter(Mandatory = $false)]
		[switch]$UseSqlite
	)

	$projectConfig = "Release"
	$sw = [Diagnostics.Stopwatch]::StartNew()

	if ($UseSqlite) {
		$script:databaseEngine = "SQLite"
	}

	Resolve-DatabaseEngine

	if ($script:databaseEngine -ne "SQLite") {
		if (-not [string]::IsNullOrEmpty($databaseServer)) {
			$script:databaseServer = $databaseServer
		}
		$script:databaseName = Get-ResolvedDatabaseName -explicitName $databaseName -baseName $projectName -onLinux (Test-IsLinux) -localBuild (Test-IsLocalBuild)
	}

	# Opt-in: ACCEPTANCE_SKIP_CLEAN=true skips 'dotnet clean' and does an incremental Compile,
	# reusing a Release build already present in this checkout. Default keeps the full clean rebuild.
	$script:skipClean = ($env:ACCEPTANCE_SKIP_CLEAN -eq "true")
	try {
		Init
		Compile
		Setup-DatabaseForBuild
		AcceptanceTests
	}
	finally {
		$script:skipClean = $false
	}

	$sw.Stop()
	$elapsed = $sw.Elapsed.ToString()
	if ($script:databaseEngine -eq "SQLite") {
		Log-Message -Message "ACCEPTANCE BUILD SUCCEEDED (SQLite) - Build time: $elapsed" -Type "INFO"
	}
	else {
		Log-Message -Message "ACCEPTANCE BUILD SUCCEEDED - Build time: $elapsed" -Type "INFO"
	}
}

Function Create-SqlServerInDocker {
	param (
		[Parameter(Mandatory = $true)]
			[ValidateNotNullOrEmpty()]
			[string]$serverName,
		[Parameter(Mandatory = $true)]
			[ValidateNotNullOrEmpty()]
			[string]$dbAction,
		[Parameter(Mandatory = $true)]
			[ValidateNotNullOrEmpty()]
			[string]$scriptDir
		)
	$tempDatabaseName = Generate-UniqueDatabaseName -baseName $projectName -generateUnique $true
	$containerName = Get-ContainerName -DatabaseName $tempDatabaseName
	$sqlPassword = Get-SqlServerPassword -ContainerName $containerName

	New-DockerContainerForSqlServer -containerName $containerName
	New-SqlServerDatabase -serverName $serverName -databaseName $tempDatabaseName

	$env:ConnectionStrings__SqlConnectionString = New-SqlServerConnectionString -server $serverName -database $tempDatabaseName -password $sqlPassword
	$databaseDll = Join-PathSegments $source_dir "Database" "bin" $projectConfig $framework "ClearMeasure.Bootcamp.Database.dll"
	$dbArgs = @($databaseDll, $dbAction, $serverName, $tempDatabaseName, $scriptDir, "sa", $sqlPassword)
	& dotnet $dbArgs
	if ($LASTEXITCODE -ne 0) {
		throw "Database migration failed with exit code $LASTEXITCODE"
	}
	# Restore connection string to default project database
	$env:ConnectionStrings__SqlConnectionString = New-SqlServerConnectionString -server $script:databaseServer -database $projectName -password (Get-SqlServerPassword -ContainerName (Get-ContainerName -DatabaseName $projectName))
}
