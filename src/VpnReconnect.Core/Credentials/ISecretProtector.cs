namespace VpnReconnect.Credentials;

public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] blob);
}
