[CmdletBinding()]
param(
	[Parameter(Mandatory)][string]$Msi,
	[Parameter(Mandatory)][string]$StageRoot
)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase((Resolve-Path $Msi).Path, 0)
function Rows([string]$sql, [int]$count) {
	$view = $db.OpenView($sql)
	$null = $view.Execute()
	try {
		while ($record = $view.Fetch()) {
			$fields = foreach ($i in 1..$count) { $record.StringData($i) }
			,@($fields)
		}
	} finally { $null = $view.Close() }
}
function Require($ok, $message) { if (!$ok) { throw $message } }

$props = @{}
foreach ($r in Rows 'SELECT `Property`,`Value` FROM `Property`' 2) { $props[$r[0]] = $r[1] }
Require ($props.INSTALLLEVEL -eq '1' -and !$props.ADDLOCAL) 'Unexpected default selection'
$levels = @{}
foreach ($r in Rows 'SELECT `Feature`,`Level`,`Display`,`Attributes` FROM `Feature`' 4) {
	$levels[$r[0]] = $r[1]
	Require ([int]$r[2] -gt 0) "Hidden feature: $($r[0])"
	Require (([bool]([int]$r[3] -band 16)) -eq ($r[0] -eq 'Main')) "Unexpected absent policy: $($r[0])"
}
Require ($levels.Count -eq 2 -and $levels.Main -eq '1' -and $levels.ManifestRunner -eq '2') 'Unexpected feature levels'
$owners = @{}
foreach ($r in Rows 'SELECT `Component_`,`Feature_` FROM `FeatureComponents`' 2) {
	Require (!$owners.ContainsKey($r[0])) "Shared component: $($r[0])"
	$owners[$r[0]] = $r[1]
}
$componentDirs = @{}
foreach ($r in Rows 'SELECT `Component`,`Directory_`,`Condition` FROM `Component`' 3) {
	Require ($owners.ContainsKey($r[0]) -and !$r[2]) "Unowned or conditional component: $($r[0])"
	$componentDirs[$r[0]] = $r[1]
}
$dirs = @{}
foreach ($r in Rows 'SELECT `Directory`,`Directory_Parent`,`DefaultDir` FROM `Directory`' 3) { $dirs[$r[0]] = $r }
Require ($dirs.RUNNERFOLDER[1] -eq 'INSTALLFOLDER' -and $dirs.RUNNERFOLDER[2].Split('|')[-1] -eq 'ManifestRunner') 'Incorrect runner directory'
$files = @{}
foreach ($r in Rows 'SELECT `Component_`,`FileName`,`FileSize` FROM `File`' 3) {
	$dir = $componentDirs[$r[0]]
	$path = $r[1].Split('|')[-1]
	while ($dir -ne 'INSTALLFOLDER') {
		Require ($dir -and $dirs.ContainsKey($dir)) "File outside install folder: $path"
		$path = Join-Path ($dirs[$dir][2].Split(':')[0].Split('|')[-1]) $path
		$dir = $dirs[$dir][1]
	}
	Require (!$files.ContainsKey($path)) "Colliding destination: $path"
	$files[$path] = @($owners[$r[0]], [long]$r[2])
}
foreach ($stage in 'Client','Runner') {
	$feature = if ($stage -eq 'Client') { 'Main' } else { 'ManifestRunner' }
	$root = (Resolve-Path (Join-Path $StageRoot $stage)).Path
	$app = if ($stage -eq 'Client') { 'DigitalMIDP' } else { 'DigitalMIDP.ManifestRunner' }
	foreach ($name in "$app.exe","$app.dll","$app.runtimeconfig.json","$app.deps.json",'appsettings.json') { Require (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf) "Missing $stage publish file: $name" }
	$expected = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Extension -ne '.pdb')
	foreach ($file in $expected) {
		$path = $file.FullName.Substring($root.Length + 1)
		if ($stage -eq 'Runner') { $path = Join-Path 'ManifestRunner' $path }
		Require ($files.ContainsKey($path) -and $files[$path][0] -eq $feature -and $files[$path][1] -eq $file.Length) "Missing, wrong size or wrong owner: $path"
		$files.Remove($path)
	}
	Write-Output "Verified $feature (level $($levels[$feature])): $($expected.Count) publish files"
}
Require ($files.Count -eq 0) "Unexpected files: $($files.Keys -join ', ')"
$targets = @{ Main = '[INSTALLFOLDER]DigitalMIDP.exe'; ManifestRunner = '[RUNNERFOLDER]DigitalMIDP.ManifestRunner.exe' }
foreach ($r in Rows 'SELECT `Component_`,`Target`,`Directory_`,`WkDir` FROM `Shortcut`' 4) {
	$owner = $owners[$r[0]]
	Require ($targets[$owner] -ceq $r[1] -and $r[2] -eq 'AppMenuFolder' -and $r[1].StartsWith("[$($r[3])]")) "Unexpected shortcut: $($r[1])"
	$targets.Remove($owner)
}
Require ($targets.Count -eq 0) 'Missing shortcut'
foreach ($r in Rows 'SELECT `Name` FROM `_Tables`' 1) {
	if ($r[0] -eq 'Condition') { Require (@(Rows 'SELECT `Feature_` FROM `Condition`' 1).Count -eq 0) 'Unexpected feature-level override' }
}
Require (@(Rows 'SELECT `Attributes` FROM `Upgrade`' 1 | Where-Object { [int]$_[0] -band 1 }).Count -gt 0) 'Missing feature-state migration'
Require (@(Rows 'SELECT `Type` FROM `Control`' 1 | Where-Object { $_[0] -eq 'SelectionTree' }).Count -gt 0) 'Missing feature-selection UI'
foreach ($r in Rows 'SELECT `Cabinet` FROM `Media`' 1) { Require ($r[0].StartsWith('#')) 'Expected an embedded cabinet' }
Write-Output "Verified MSI: $Msi"
