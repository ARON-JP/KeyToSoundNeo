using System.IO.Compression;
using System.IO;
using System.Text.Json;

namespace KeyToSoundNeo;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyToSoundNeo");
    private string ProfilesPath => Path.Combine(_root, "Profiles");
    private string SoundsPath => Path.Combine(_root, "Sounds");
    private string PreferencesPath => Path.Combine(_root, "Preferences.json");

    public ProfileStore()
    {
        Directory.CreateDirectory(ProfilesPath);
        Directory.CreateDirectory(SoundsPath);
    }

    public string[] GetProfileNames() => Directory.GetFiles(ProfilesPath, "*.json")
        .Select(Path.GetFileNameWithoutExtension).Where(x => x is not null).Cast<string>()
        .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public AppPreferences LoadPreferences() =>
        File.Exists(PreferencesPath)
            ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PreferencesPath)) ?? new AppPreferences()
            : new AppPreferences();

    public void SavePreferences(AppPreferences preferences) =>
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(preferences, JsonOptions));

    public SoundProfile? Load(string name)
    {
        var path = Path.Combine(ProfilesPath, SafeName(name) + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<SoundProfile>(File.ReadAllText(path)) : null;
    }

    public void Save(SoundProfile profile)
    {
        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "My profile" : profile.Name.Trim();
        var path = Path.Combine(ProfilesPath, SafeName(profile.Name) + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public string ImportSound(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(SoundsPath, fileName);
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    public void ExportKsn(SoundProfile profile, string destination)
    {
        var manifest = new KsnManifest { Profile = Clone(profile) };
        var archivePath = destination.EndsWith(".ksn", StringComparison.OrdinalIgnoreCase)
            ? destination : destination + ".ksn";
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var packagedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in profile.Keys.SelectMany(k => k.PressSounds.Concat(k.ReleaseSounds)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Sound file not found: {path}", path);
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(path)}";
            var relativePath = $"sounds/{fileName}";
            packagedPaths[path] = relativePath;
            manifest.Sounds.Add(relativePath);
            archive.CreateEntryFromFile(path, relativePath, CompressionLevel.Optimal);
        }

        foreach (var key in manifest.Profile.Keys)
        {
            key.PressSounds = key.PressSounds.Select(p => packagedPaths[p]).ToList();
            key.ReleaseSounds = key.ReleaseSounds.Select(p => packagedPaths[p]).ToList();
        }
        var entry = archive.CreateEntry("profile.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public SoundProfile ImportKsn(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        if (archive.Entries.Count > 1024 || archive.Entries.Sum(item => item.Length) > 512L * 1024 * 1024)
            throw new InvalidDataException("This .ksn file exceeds the supported size limits.");
        var entry = archive.GetEntry("profile.json") ?? throw new InvalidDataException("This .ksn file has no profile.json.");
        if (entry.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("The .ksn profile data is too large.");
        using var reader = new StreamReader(entry.Open());
        var manifest = JsonSerializer.Deserialize<KsnManifest>(reader.ReadToEnd())
            ?? throw new InvalidDataException("Could not read the .ksn profile.");
        if (manifest.FormatVersion != 1)
            throw new InvalidDataException($"Unsupported .ksn format version: {manifest.FormatVersion}");
        if (manifest.Profile is null || manifest.Profile.Keys is null || manifest.Sounds is null)
            throw new InvalidDataException("The .ksn file is missing required profile data.");

        var extracted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in manifest.Sounds)
        {
            if (!relativePath.StartsWith("sounds/", StringComparison.Ordinal)
                || relativePath.Contains("..", StringComparison.Ordinal)
                || relativePath.Contains('\\')
                || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("The .ksn file contains an invalid sound path.");
            var soundEntry = archive.GetEntry(relativePath) ?? throw new InvalidDataException($"Missing sound file: {relativePath}");
            var extension = Path.GetExtension(relativePath);
            if (!new[] { ".wav", ".mp3", ".wma", ".aac", ".m4a", ".aiff", ".aif", ".mp4" }
                    .Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsupported audio file type in .ksn: {extension}");
            if (soundEntry.Length > 100L * 1024 * 1024)
                throw new InvalidDataException($"The sound file is too large: {relativePath}");
            var outputPath = Path.Combine(SoundsPath, $"{Guid.NewGuid():N}{extension}");
            soundEntry.ExtractToFile(outputPath);
            extracted[relativePath] = outputPath;
        }

        foreach (var key in manifest.Profile.Keys)
        {
            key.PressSounds = key.PressSounds.Select(p =>
                extracted.TryGetValue(p, out var path) ? path : throw new InvalidDataException($"Unbundled sound reference: {p}")).ToList();
            key.ReleaseSounds = key.ReleaseSounds.Select(p =>
                extracted.TryGetValue(p, out var path) ? path : throw new InvalidDataException($"Unbundled sound reference: {p}")).ToList();
        }
        manifest.Profile.Name = $"{manifest.Profile.Name} (imported)";
        return manifest.Profile;
    }

    private static SoundProfile Clone(SoundProfile profile) =>
        JsonSerializer.Deserialize<SoundProfile>(JsonSerializer.Serialize(profile))!;

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "My profile" : safe;
    }
}
