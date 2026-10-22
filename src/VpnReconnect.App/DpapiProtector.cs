using System.Security.Cryptography;
using VpnReconnect.Credentials;

namespace VpnReconnect.App;

public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] blob)
    {
        return ProtectedData.Unprotect(blob, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}
