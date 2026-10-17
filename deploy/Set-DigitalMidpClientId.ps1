<#
.SYNOPSIS
	Writes the company-wide Autodesk APS Client ID for DigitalMIDP.

.DESCRIPTION
	Creates %ProgramData%\DigitalMIDP\autodesk.json for all users on the machine. Intended for
	installers, Intune/SCCM, or Group Policy startup scripts. Requires administrator rights.
	The Client ID is a public PKCE identifier; never deploy a client secret.

.EXAMPLE
	.\Set-DigitalMidpClientId.ps1 -ClientId "abc123..."
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[ValidatePattern('^[A-Za-z0-9]{20,64}$')]
	[string]$ClientId
)

$ErrorActionPreference = 'Stop'

$folder = Join-Path $env:ProgramData 'DigitalMIDP'
$path = Join-Path $folder 'autodesk.json'

New-Item -ItemType Directory -Path $folder -Force | Out-Null

$config = @{ Autodesk = @{ ClientId = $ClientId } } | ConvertTo-Json -Depth 3
Set-Content -Path $path -Value $config -Encoding UTF8

Write-Host "DigitalMIDP Client ID written to $path"
