using System.Security.Cryptography;
using System.Text;
using Application.Interfaces.Security;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security;

/// <summary>Compares registration keys in constant time (Protocol Spec section 8.3).</summary>
public sealed class RegistrationKeyValidator(IOptionsMonitor<AuthOptions> options) : IRegistrationKeyValidator
{
  public bool IsValid(string presentedKey)
  {
    if (string.IsNullOrEmpty(presentedKey))
      return false;

    // Hashing both sides hides the key length; the loop never exits early so timing does not reveal which key matched.
    var presented = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
    var isValid = false;

    foreach (var configuredKey in options.CurrentValue.RegistrationKeys)
    {
      if (string.IsNullOrWhiteSpace(configuredKey))
        continue;

      var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
      isValid |= CryptographicOperations.FixedTimeEquals(presented, candidate);
    }

    return isValid;
  }
}
