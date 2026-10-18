using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Application.Interfaces.Security;
using Application.Models;

namespace Infrastructure.Security;

/// <summary>
/// Agent API key: "aers_" followed by the unpadded base64url form of 32 CSPRNG bytes.
/// Only the SHA-256 hash (lowercase hex) is stored (Protocol Spec section 8.2).
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
  private const string Prefix = "aers_";
  private const int KeyByteLength = 32;

  public IssuedApiKey Generate()
  {
    var rawKey = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyByteLength));
    return new IssuedApiKey(rawKey, Hash(rawKey));
  }

  public string Hash(string rawKey) =>
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
}
