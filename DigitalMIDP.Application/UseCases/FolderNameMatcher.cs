using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Matches CDE folders by name.
///
/// Folder names are typed by people in the ACC web UI, so exact string comparison is too brittle to
/// rely on: the same folder appears as "WIP", "wip", "01 - WIP" or "Work In Progress" across
/// projects, and a discipline folder as "AR Architect", "AR-Architect" or just "AR". Matching is
/// therefore done on a normalised form, and discipline matching falls back through progressively
/// looser rules rather than failing outright.
/// </summary>
internal static class FolderNameMatcher
{
    /// <summary>
    /// Folder names accepted for each ISO 19650 lifecycle stage, already normalised by
    /// <see cref="Normalize"/>. Several spellings are accepted per stage because these folders are
    /// created by hand in ACC, and projects commonly number them ("01 - WIP") or spell them out
    /// ("Work In Progress").
    ///
    /// Single source of truth for two unrelated consumers that must never drift apart: resolving
    /// which physical folder is "WIP" (<see cref="CdeStructureUseCase"/>, for deliverable creation)
    /// and classifying which stage an arbitrary file's path falls under (<see cref="ClassifyLifecycleStage"/>,
    /// for the files grid). A folder recognised as WIP by one and not the other would be a silent
    /// inconsistency between "where a new file goes" and "what stage an existing file shows as".
    /// </summary>
    internal static readonly IReadOnlyDictionary<CdeLifecycleStage, string[]> LifecycleFolderNames =
        new Dictionary<CdeLifecycleStage, string[]>
        {
            [CdeLifecycleStage.Wip] = ["wip", "workinprogress", "01wip"],
            [CdeLifecycleStage.Shared] = ["shared", "share","02shared"],
            [CdeLifecycleStage.Published] = ["published", "publish","03published"],
            [CdeLifecycleStage.Archive] = ["archive", "archived","04archived"]
        };

    /// <summary>
    /// Accepted names (already normalised) for the admin/reference container folder nested directly
    /// under "Project Files" that must never appear in the files grid or be treated as real project
    /// content - currently holds file-creation templates (see <see cref="CdeStructureUseCase"/>,
    /// the one sanctioned reader of what's inside it) and any other non-deliverable reference material
    /// a project keeps alongside its real files. See <see cref="IsHiddenAdminFolder"/>.
    /// </summary>
    internal static readonly string[] HiddenAdminFolderNames = ["00projectfiles"];

    /// <summary>
    /// True for a folder that must be skipped entirely wherever the app walks a project's file tree -
    /// see <see cref="HiddenAdminFolderNames"/>.
    /// </summary>
    internal static bool IsHiddenAdminFolder(string? name) =>
        HiddenAdminFolderNames.Contains(Normalize(name));

    /// <summary>
    /// Reduces a folder name to comparable form: lowercase letters and digits only. This discards
    /// separators, spacing and the numeric prefixes projects commonly use for ordering, so
    /// "01 - WIP" and "WIP" both normalise to "wip".
    /// </summary>
    internal static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    /// <summary>
    /// Finds the first folder whose normalised name matches any of the accepted names. Accepted
    /// names must already be normalised.
    /// </summary>
    internal static FolderDto? FindByName(IEnumerable<FolderDto> folders, IReadOnlyCollection<string> acceptedNames)
    {
        var candidates = folders.ToList();

        // Exact normalised match first. A looser rule would let "Archive" claim a folder named
        // "Archived Drawings", so precision is tried before tolerance.
        var exact = candidates.FirstOrDefault(f => acceptedNames.Contains(Normalize(f.Name)));
        if (exact is not null)
        {
            return exact;
        }

        // Then a prefix match, which catches suffixed names like "WIP Models" while still refusing
        // unrelated folders that merely contain the token somewhere in the middle.
        return candidates.FirstOrDefault(f =>
        {
            var normalized = Normalize(f.Name);
            return acceptedNames.Any(accepted =>
                normalized.StartsWith(accepted, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// Finds the WIP subfolder for a discipline, trying the conventional "{Code} {Description}"
    /// form first and then looser forms.
    ///
    /// The looser fallbacks exist because the code is the part that carries meaning: a project that
    /// named its folder just "AR", or that spelled the description differently from ACC's
    /// configured text, still unambiguously means the same discipline.
    /// </summary>
    internal static FolderDto? FindDisciplineFolder(
        IEnumerable<FolderDto> folders,
        string code,
        string? description,
        string expectedName)
    {
        var candidates = folders.ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var normalizedExpected = Normalize(expectedName);
        var normalizedCode = Normalize(code);
        var normalizedDescription = Normalize(description);

        // 1. The conventional form: "AR Architect".
        var match = candidates.FirstOrDefault(f => Normalize(f.Name) == normalizedExpected);
        if (match is not null)
        {
            return match;
        }

        // 2. The bare code: "AR".
        if (!string.IsNullOrEmpty(normalizedCode))
        {
            match = candidates.FirstOrDefault(f => Normalize(f.Name) == normalizedCode);
            if (match is not null)
            {
                return match;
            }
        }

        // 3. The description alone: "Architect".
        if (!string.IsNullOrEmpty(normalizedDescription))
        {
            match = candidates.FirstOrDefault(f => Normalize(f.Name) == normalizedDescription);
            if (match is not null)
            {
                return match;
            }
        }

        // 4. Code as a leading token, e.g. "AR Architectural Drawings". Only applied when the code is
        //    at least two characters - a single letter would match far too much to be safe.
        if (normalizedCode.Length >= 2)
        {
            match = candidates.FirstOrDefault(f =>
                Normalize(f.Name).StartsWith(normalizedCode, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines which lifecycle stage a file's folder path falls under, e.g. "Project
    /// Files/WIP/AR Architect" resolves to <see cref="CdeLifecycleStage.Wip"/>. This is what lets the
    /// files grid show and filter by WIP/Shared/Published/Archive without a separate lookup against
    /// the project's actual folder tree - the path segments already collected while listing files
    /// (see <c>FileListingUseCase.CollectProjectFilesAsync</c>) are enough on their own.
    ///
    /// Returns null when no segment matches any known stage - a file directly under the project root,
    /// or one in a folder structure that doesn't follow the WIP/Shared/Published/Archive convention.
    /// </summary>
    internal static CdeLifecycleStage? ClassifyLifecycleStage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = Normalize(segment);
            if (normalized.Length == 0)
            {
                continue;
            }

            foreach (var (stage, acceptedNames) in LifecycleFolderNames)
            {
                if (acceptedNames.Any(accepted =>
                        normalized == accepted || normalized.StartsWith(accepted, StringComparison.Ordinal)))
                {
                    return stage;
                }
            }
        }

        return null;
    }
}
