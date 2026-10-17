namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// The ISO 19650 lifecycle stage a folder represents. A deliverable matures through these stages,
/// and each stage is a physical Level 1 folder in the project.
/// </summary>
public enum CdeLifecycleStage
{
    /// <summary>Work in progress - the author's drafting area, and the only stage this app writes to.</summary>
    Wip,

    /// <summary>Issued to the wider project team for coordination. Populated by the review workflow, not by this app.</summary>
    Shared,

    /// <summary>Formally approved and issued. Populated by the review workflow, not by this app.</summary>
    Published,

    /// <summary>Superseded material.</summary>
    Archive
}

/// <summary>
/// The resolved CDE folder layout for a project: the Level 1 lifecycle folders, the template
/// repository, and the discipline subfolders under WIP.
///
/// Every member is resolved by <em>name</em> at runtime rather than configured, because folder ids
/// are project-specific and the discipline list is not knowable ahead of time (projects can have
/// 10+, and the set differs per project).
/// </summary>
/// <param name="LifecycleFolders">The Level 1 folders that were found. A stage missing from this map does not exist in the project.</param>
/// <param name="TemplatesFolder">The template repository folder, or null when it could not be found.</param>
/// <param name="Disciplines">Discipline folders expected under WIP, whether or not they exist yet.</param>
public sealed record CdeStructureDto(
    IReadOnlyDictionary<CdeLifecycleStage, FolderDto> LifecycleFolders,
    FolderDto? TemplatesFolder,
    IReadOnlyList<DisciplineFolderDto> Disciplines
)
{
    public FolderDto? Wip => LifecycleFolders.GetValueOrDefault(CdeLifecycleStage.Wip);

    public FolderDto? Shared => LifecycleFolders.GetValueOrDefault(CdeLifecycleStage.Shared);

    public FolderDto? Published => LifecycleFolders.GetValueOrDefault(CdeLifecycleStage.Published);

    public FolderDto? Archive => LifecycleFolders.GetValueOrDefault(CdeLifecycleStage.Archive);

    /// <summary>
    /// True when the project has the minimum needed to create a deliverable: a WIP folder to create
    /// in, and a templates folder to create from.
    /// </summary>
    public bool SupportsDeliverableCreation => Wip is not null && TemplatesFolder is not null;

    public DisciplineFolderDto? FindDiscipline(string code) =>
        Disciplines.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A discipline (equivalently, a role) and the WIP subfolder that holds its deliverables.
///
/// The client confirmed that discipline and role are the same concept, and that the folder is named
/// "{Code} {Description}" - so role option "AR" described as "Architect" corresponds to the folder
/// "AR Architect". That pairing comes straight from the naming convention's role field options,
/// which is why no separate discipline configuration exists.
/// </summary>
/// <param name="Code">The role code as it appears in the document number, e.g. "AR".</param>
/// <param name="Description">The role description from ACC, e.g. "Architect". Empty when ACC supplies none.</param>
/// <param name="ExpectedFolderName">The folder name derived from code and description, e.g. "AR Architect".</param>
/// <param name="Folder">
/// The matching folder in the project, or null when it does not exist yet. A null folder is not an
/// error: the agreed behaviour is to create the folder on demand at deliverable-creation time
/// rather than failing.
/// </param>
public sealed record DisciplineFolderDto(
    string Code,
    string Description,
    string ExpectedFolderName,
    FolderDto? Folder
)
{
    /// <summary>False when the WIP subfolder has not been created in the project yet.</summary>
    public bool Exists => Folder is not null;

    /// <summary>
    /// Builds the conventional folder name for a role option. Falls back to the bare code when ACC
    /// has no description configured for the option, which keeps a partially-configured project
    /// usable instead of producing a folder named "AR ".
    /// </summary>
    public static string BuildFolderName(string code, string? description) =>
        string.IsNullOrWhiteSpace(description) ? code.Trim() : $"{code.Trim()} {description.Trim()}";
}
