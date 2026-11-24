# DigitalMIDP Manifest Runner

## Install and configure

Install the per-user x64 MSI with the .NET 8 x64 Desktop Runtime. Reader machines need only the default client (`Main`). On the designated operator machine select **Manifest Runner → Will be installed on local hard drive**, or use `msiexec /i setup.msi ADDLOCAL=Main,ManifestRunner /qn`. The optional feature adds a separate Start Menu shortcut; it does not start a service. See the [MSI selection and lifecycle checklist](../DigitalMIDP.Installer/README.md).

For a standalone self-contained build **instead of the MSI**, run from the repository root on Windows:

```powershell
dotnet publish .\DigitalMIDP.ManifestRunner\DigitalMIDP.ManifestRunner.csproj -c Release -r win-x64 --self-contained true
```

Package the **contents** of `DigitalMIDP.ManifestRunner\bin\Release\net8.0-windows\win-x64\publish\` together in a dedicated runner directory. Point the shortcut to `DigitalMIDP.ManifestRunner.exe` in that directory. This is a self-contained folder deployment, not a single-file executable; keep `appsettings.json`, the runtime and dependency files beside the executable.

## Seed each project before rolling out its clients

1. **Agree on coverage and audience with the project owner.** Use an authorized operator who can list all intended Project Files folders, read item details, definitions and values, and create missing folders, upload the manifest and add versions under Templates. Readers need access to sync and read the manifest. It contains the operator's catalog, not a view filtered for each reader: restrict access to people authorized for **all** metadata it contains. Seeing a row does not grant access to open or edit the source file.
2. Assign **one runner on one machine** per project. The single-instance guard is session-local; it does not coordinate other sessions or machines. The pre-publication tip check is not a distributed lock. Stop the old runner before transferring responsibility.
3. Open the runner and **Sign in** separately with the operator account. Client ID and callback configuration are shared with the client ([settings](../docs/CONFIGURATION.md)); opening the runner alone starts no checks. Select the hub → **Load projects** → project → **Enroll project**. **Interval (minutes)** defaults to 10 (1–71582 whole minutes). **Apply interval to selected** changes one enrollment. Hub/project IDs and intervals persist in `%LOCALAPPDATA%\DigitalMIDP\manifest-enrollment.json`. Sign-out or closing stops checks; sign-in resumes enrollments. **Remove selected** stops that enrollment, not the published catalog.
4. Select the enrolled row and use **Check now** or wait for its scheduled check. The first successful scan seeds `Project Files/00 ProjectFiles/Templates/digitalmidp-manifest.json` (reuses matching folders; creates missing ones). Check **Last success (UTC)** and **Published (UTC)**, verify the item in ACC, and compare inventory against known folders and files. APS can return a successful, permission-filtered listing: an omitted file is indistinguishable from a deletion. A successful check does not prove complete coverage. Stop polling and investigate unexpected omissions before rollout.
5. On each client sync the project in Desktop Connector and make the manifest and its directory path available locally. Sign in, select the project and check representative rows and metadata. Do not roll out an unseeded project: Refresh rereads the local file; it does not ask APS to rebuild the grid.

## Monitoring and recovery

| Observation | Meaning and next action |
| --- | --- |
| **Checking**, **Last attempt**, **Next check** | A check is active, was attempted, or is scheduled. Times are UTC. Keep the runner signed in and running. |
| **Last success** advances; **Changed** is false | Inventory matched the prior revision. **Published** stays at the previous publication time. **HTTP requests** counts instrumented requests for the last successful check, not just file-detail GETs. |
| **Last error** after an attempt | Last-success fields may describe an earlier attempt. Check sign-in, network, permissions, duplicate folder/file names, and the current manifest tip. Resolve the cause and use **Check now**. If a write response was lost, inspect ACC version history before retrying. Do not delete the manifest or hand-edit revision numbers to clear an error. |
| Client reports **unavailable** or **stale** | Confirm the correct project was seeded, the reader has Templates access, and Desktop Connector has synced a unique, hydrated local path. Check for an invalid, older or conflicting revision. Recover a valid publication with the project owner; wait for sync and Refresh. See [reader status and Save](../docs/USER-GUIDE.md). |

## Manual acceptance gates (not yet run)

- **Authorized project pilot:** Use one runner and two separately authorized reader workspaces. Seed a pilot project, record the ACC version and publication time, then confirm both clients show the expected catalog. Check again unchanged: last success should advance without a new version. Make one approved metadata change on a test file; check the Save result against ACC, run **Check now**, and verify both readers converge after sync. In an isolated local copy, check that a missing manifest on first read is unavailable and an unreadable manifest after a good read is stale. Restore the valid copy and Refresh. Do not delete a synced ACC item to simulate failure. Simply pausing sync may leave a readable revision marked available. Record revisions, timestamps and results without credentials.
- **Installer:** On a disposable Windows machine follow the [MSI checklist](../DigitalMIDP.Installer/README.md) for default and opted-in installs, change, repair, upgrade and uninstall. A successful build or package-table inspection is not lifecycle verification.

The solution build and 518 automated tests passed. The reconciliation fixture counted **3 cold, 0 warm, 1 changed file-detail GETs**, not total HTTP traffic. No live project or installer lifecycle smoke was run; these results do not establish production readiness.
