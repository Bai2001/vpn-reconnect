using VpnReconnect.Credentials;

namespace VpnReconnect.Tests;

public class CredentialStoreTests
{
    [Fact]
    public void 保存后能读回且磁盘上没有明文密码()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            var store = new CredentialStore(path, new XorProtector(0x5A));
            store.Save("bjj", "secret-value");
            var loaded = store.Load();
            Assert.Equal(("bjj", "secret-value"), loaded);
            var disk = File.ReadAllText(path);
            Assert.DoesNotContain("secret-value", disk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 文件不存在时读取返回空()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        var store = new CredentialStore(path, new XorProtector(0x5A));
        Assert.Null(store.Load());
    }
}

file sealed class XorProtector : ISecretProtector
{
    private readonly byte _key;

    public XorProtector(byte key)
    {
        _key = key;
    }

    public byte[] Protect(byte[] plaintext)
    {
        return Transform(plaintext);
    }

    public byte[] Unprotect(byte[] blob)
    {
        return Transform(blob);
    }

    private byte[] Transform(byte[] input)
    {
        var output = new byte[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            output[i] = (byte)(input[i] ^ _key);
        }

        return output;
    }
}
