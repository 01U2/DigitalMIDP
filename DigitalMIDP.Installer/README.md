# DigitalMIDP MSI

From the repository root on Windows, run `dotnet build DigitalMIDP.sln -c Release`. The release workflow supplies `-p:Version=x.y.z`. The installer publishes the two framework-dependent win-x64 applications into `DigitalMIDP.Installer/obj/x64/Release/publish/{Client,Runner}`. It embeds their non-PDB files in `DigitalMIDP.Installer/bin/x64/Release/en-US/DigitalMIDP-Setup.msi`. Both applications require the x64 .NET 8 Desktop Runtime.

The build runs the read-only packaging gate. To repeat it without restore, build, network or installation:

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File DigitalMIDP.Installer/Test-Package.ps1 -Msi DigitalMIDP.Installer/bin/x64/Release/en-US/DigitalMIDP-Setup.msi -StageRoot DigitalMIDP.Installer/obj/x64/Release/publish`

## Selection and disposable-VM smoke checklist

Run lifecycle checks only in a fresh disposable Windows VM, as the same non-elevated user with the prerequisite runtime. Below, `setup.msi` is the built MSI. The client installs at `%LOCALAPPDATA%\Programs\DigitalMIDP` by default. The runner installs below it in `ManifestRunner`; shortcuts are under the user's Start Menu `Programs\DigitalMIDP` folder.

| Check | Command or UI action | Expected result |
| --- | --- | --- |
| Standard install | `msiexec /i setup.msi /qn` | Client and client shortcut only; no runner directory or shortcut. |
| Interactive selection | From a clean snapshot, open Setup and choose **Manifest Runner → Will be installed on local hard drive** in the feature tree. | Client remains mandatory; both executables and shortcuts are present. Maintenance **Change** allows deselecting the runner. |
| Unattended opt-in (fresh install or change) | `msiexec /i setup.msi ADDLOCAL=Main,ManifestRunner /qn` | Both applications and shortcuts are present. Feature IDs are case-sensitive. |
| Repair | Delete a packaged file and shortcut for an installed feature, then `msiexec /fa setup.msi /qn`. Repeat for client-only and opted-in states. | Missing installed resources return; repair does not add an absent runner. |
| Remove optional feature | `msiexec /i setup.msi REMOVE=ManifestRunner /qn` | Runner files and shortcut disappear; client remains usable. |
| Major upgrade | Build a strictly higher three-part version and run `msiexec /i newer.msi /qn` over each state, **without** `ADDLOCAL` or `REMOVE`. | Previous version is replaced; selected runner stays installed, absent runner stays absent. Upgrade from a pre-runner MSI remains client-only. |
| Uninstall | `msiexec /x setup.msi /qn` (use the currently installed MSI) | Both features' installed resources and shortcuts are removed; no recursive deletion of user-created files. |

Wait for each command to finish and check its exit code before checking paths or launching via the shortcuts. Restore the snapshot between independent scenarios. MSI table checks verify authoring and payload, not interactive or runtime behavior.
