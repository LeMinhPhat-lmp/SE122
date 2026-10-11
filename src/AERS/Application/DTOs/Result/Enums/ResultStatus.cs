namespace Application.DTOs.Result.Enums;

public enum ResultStatus
{
  Success,
  NotFound,
  BadRequest,
  ValidationError,
  Conflict,
  Unauthorized,
  Forbidden,
  InternalError
}