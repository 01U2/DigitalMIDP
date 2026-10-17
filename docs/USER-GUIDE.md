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
3. **Folder Tree**: Navigate folder structures in the left navigation pane.
4. **DataGrid View**: Files in the selected folder appear in the main grid with standard attributes (Name, Version, Updated At, Size).
5. **Custom Metadata**: Custom BIM 360 attributes are automatically fetched and displayed as dynamic columns.

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
