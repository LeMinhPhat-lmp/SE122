using Application.Models;

namespace Application.Interfaces.Security;

public interface IApiKeyService
{
  IssuedApiKey Generate();
  string Hash(string rawKey);
}
