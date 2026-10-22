using System.Text.Json;

namespace VpnReconnect.Credentials;

public sealed class CredentialStore
{
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public CredentialStore(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public void Save(string username, string password)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new StoredCredentials(username, password));
        File.WriteAllBytes(_path, _protector.Protect(plaintext));
    }

    public (string Username, string Password)? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredCredentials>(_protector.Unprotect(File.ReadAllBytes(_path)));
        if (stored is null)
        {
            return null;
        }

        return (stored.Username, stored.Password);
    }

    private sealed record StoredCredentials(string Username, string Password);
}
