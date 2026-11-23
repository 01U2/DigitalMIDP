using System.Text.Json;

namespace DigitalMIDP.Infrastructure.Manifests;

internal sealed record Enrollment(string HubId, string ProjectId, TimeSpan? Interval)
{
    public TimeSpan EffectiveInterval => Interval ?? TimeSpan.FromMinutes(10);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(HubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProjectId);
        if (EffectiveInterval < TimeSpan.FromMilliseconds(1) || EffectiveInterval.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(Interval));
    }
}

internal sealed class ManifestEnrollmentStore(string path)
{
    internal static ManifestEnrollmentStore ForOperator() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalMIDP", "manifest-enrollment.json"));

    internal async Task<Enrollment[]> LoadAsync()
    {
        try
        {
            using var stream = File.OpenRead(path);
            var rows = await JsonSerializer.DeserializeAsync<Enrollment[]>(stream).ConfigureAwait(false)
                ?? throw new InvalidDataException("Expected a manifest enrollment list.");
            var keys = new HashSet<(string, string)>();
            foreach (var row in rows)
            {
                if (row is null || !keys.Add((row.HubId, row.ProjectId)))
                    throw new InvalidDataException("Invalid or duplicate manifest enrollment.");
                row.Validate();
            }
            return rows;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    internal async Task SaveAsync(Enrollment[] rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, rows).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}
