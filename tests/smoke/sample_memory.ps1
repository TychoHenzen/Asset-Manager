param(
	[Parameter(Mandatory)][string]$Godot,
	[Parameter(Mandatory)][string]$Workspace,
	[string]$Project = (Resolve-Path "$PSScriptRoot\..\.."),
	[string]$Script = 'res://tests/smoke/open_memory.gd'
)
# Runs a headless smoke and prints the engine's private bytes and disk reads
# twice a second, next to the STAGE lines open_memory.gd prints:
#   pwsh tests/smoke/sample_memory.ps1 -Godot <Godot_..._console.exe> -Workspace Z:/Tycho/Projects/AssetLibrary
# The process has to be watched from outside: GDScript cannot see the .NET heap.
$log = New-TemporaryFile
$wrapper = Start-Process -FilePath $Godot -ArgumentList @('--headless', '--path', $Project, '-s', $Script, '--', $Workspace) -RedirectStandardOutput $log -PassThru -NoNewWindow
$start = Get-Date
while (-not $wrapper.HasExited) {
	# The _console exe is a wrapper; the engine is its child.
	$engine = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($wrapper.Id)" -ErrorAction SilentlyContinue | Where-Object Name -like 'Godot*' | Select-Object -First 1
	if ($engine) {
		"{0,6:F1}s private {1,6:N0} MB  read {2,6:N0} MB" -f ((Get-Date) - $start).TotalSeconds, ($engine.PrivatePageCount / 1MB), ($engine.ReadTransferCount / 1MB)
	}
	Start-Sleep -Milliseconds 500
}
Get-Content $log | Select-String 'STAGE'
Remove-Item $log
