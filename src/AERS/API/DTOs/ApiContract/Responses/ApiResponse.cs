namespace API.DTOs.ApiContract.Responses;

public sealed class ApiResponse<T>
{
  public int StatusCode { get; set; }
  public T? Data { get; set; }
  public IList<string>? Errors { get; set; }

  private ApiResponse() { }
  private ApiResponse(int statusCode, T? data, IList<string>? errors = default)
  {
    StatusCode = statusCode;
    Data = data;
    Errors = errors;
  }

  public static ApiResponse<T> Success(int statusCode, T? data) => new ApiResponse<T>(statusCode, data, default);
  public static ApiResponse<T> Failure(int statusCode, string error) => new ApiResponse<T>(statusCode, default, [error]);
  public static ApiResponse<T> Failure(int statusCode, IList<string> errors) => new ApiResponse<T>(statusCode, default, errors);
}