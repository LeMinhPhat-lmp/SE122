namespace Application.Interfaces.Security;

public interface IRegistrationKeyValidator
{
  bool IsValid(string presentedKey);
}
