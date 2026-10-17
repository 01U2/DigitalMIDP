# Copilot Instructions

## Project Guidelines
- For DigitalMIDP, Description is the canonical main file attribute everywhere (UI, DataGrid, file creation, file naming, and file attributes). Title must no longer drive behavior, but remains included in SharedMetadataFields.
- In DigitalMIDP, Deliverable Title maps to Description as a related project metadata/custom attribute, which may not be part of the project's naming-standard metadata. The creation workflow must resolve naming-convention fields and related metadata separately rather than require Description in the naming standard.
- For DigitalMIDP, the static visual column label and data/filter key should be "Deliverable Title"; reserve the static column by the metadata name "Deliverable Title" rather than generic "Title", including bindings, dynamic-column exclusion, column filtering, selector de-duplication, and title-specific value selectors.
- DigitalMIDP should not display a generic 'Title' DataGrid column or column-selector entry when 'Title' is absent from the current project's metadata and naming convention; investigate any such phantom column rather than removing a valid discovered Title field.