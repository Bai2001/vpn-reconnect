using System.Text.Encodings.Web;
using System.Text.Json;
using VpnReconnect.Settings;

namespace VpnReconnect.App;

public static class SettingsFile
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VpnReconnect");

    public static string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    public static string CredentialsPath => Path.Combine(DirectoryPath, "credentials.bin");

    public static string LogPath => Path.Combine(DirectoryPath, "connection.log");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Default()
    {
        return new AppSettings("公司", null, null, "", 60, false);
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return Default();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            if (settings is null || string.IsNullOrWhiteSpace(settings.ConnectionName))
            {
                return Default();
            }

            return settings;
        }
        catch (JsonException)
        {
            return Default();
        }
        catch (IOException)
        {
            return Default();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
