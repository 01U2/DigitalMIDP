# User Guide - DigitalMIDP

DigitalMIDP is a Windows desktop application designed to streamline Autodesk Forma (ACC) and BIM 360 project management, metadata validation, and deliverable creation.

---

## 1. Authentication

1. Launch DigitalMIDP.
2. Click **Sign In with Autodesk**.
3. Your default web browser will open requesting authorization to your Autodesk account.
4. Review requested permissions and click **Allow**.
5. Once authenticated, DigitalMIDP will automatically load your accessible hubs.

---

## 2. Browsing Projects & Files

1. **Select Hub**: Choose an active hub from the dropdown list.
2. **Select Project**: Choose the target ACC or BIM 360 project.
3. **Read the local catalog**: The project must first be [seeded by the operator](../DigitalMIDP.ManifestRunner/README.md) and synced in Desktop Connector. Rows, columns, filter values and editor choices come from the manifest, not live folder or metadata requests. **Refresh** rereads it locally. A missing file never triggers a live fallback.
4. **Interpret status**: Available means the local revision validated, not that it matches ACC right now. Stale means a read failed after a good read; the grid keeps its last validated rows. Unavailable means there is no usable snapshot; it does not mean the project is empty. Check project selection, Templates access, sync and hydration, then Refresh. The reader rejects offline/recall placeholders and filesystem links; it does not download them. This is not an offline sign-in, discovery or editing mode.
5. **Edit attributes**: Select one or more rows and open **Edit Attributes**. Bulk edit offers compatible fields only. Save requires a current snapshot, connectivity and write access to the target versions. Only changed values are submitted after live checks of file location, version, field definitions and original values. Only matching field confirmations appear in the grid; an HTTP success alone does not confirm the batch. Reopen after confirmation. A newer manifest replaces provisional values, even if it disagrees.

If Save reports a conflict, partial confirmation or a lost response, do not repeat the whole batch. Check affected values in ACC, ask the operator for a fresh check, wait for sync, Refresh and reopen before retrying only outstanding changes. Bulk Save is not atomic and the client never writes the manifest.

---

## 3. Filtering & Column Customization

1. **Column Selector**: Click the **Columns** icon to toggle visibility of standard and custom metadata columns. Preferences are saved automatically.
2. **Metadata Dropdowns**: Click any column header filter icon to filter by specific values. Options show user-friendly descriptions (e.g. "In Progress", "Approved") rather than raw internal codes.

---

## 4. Opening Files (Desktop Connector)

1. Select a file in the grid.
2. Click **Open File** or double-click the row:
   - If Desktop Connector is installed and the file is synced, DigitalMIDP opens the file locally in its native authoring tool (e.g. Revit, AutoCAD, Navisworks).
   - If Desktop Connector is not synced, DigitalMIDP offers to open the file in the ACC web viewer.
3. Click **Open Location** to open Windows File Explorer directly to the containing folder on disk.

---

## 5. Creating Deliverables

1. Click **New Deliverable**.
2. Select a deliverable template from the catalog.
3. Fill in the required naming standard fields (Discipline, Zone, Level, Type, Role). The document number validator verifies compliance in real time.
4. Click **Create** to upload the template, apply metadata, and reserve the document number.
