using System.Text.Json;
using Application.DTOs.Result.Enums;

namespace Application.DTOs.Result.Responses;

/// <summary>Outcome of an operation that returns no data. Contract between the Application and API layers.</summary>
public class Result
{
  public bool IsSuccess => ResultStatus == ResultStatus.Success;
  public ResultStatus ResultStatus { get; set; }
  public IList<string> Errors { get; set; } = new List<string>();

  /// <summary>Machine-readable error code from Protocol Spec section 12 (for example AGENT_NOT_FOUND).</summary>
  public string? ErrorCode { get; set; }

  /// <summary>Per-field problems, used by validation failures.</summary>
  public IReadOnlyList<ErrorDetail> ErrorDetails { get; set; } = [];

  /// <summary>
  /// Structured details object of the wire error (A57: RATE_LIMITED, DEPTH_EXCEEDED, CYCLE_DETECTED). When set it replaces
  /// the { "errors": [...] } object that is built from <see cref="ErrorDetails"/>.
  /// </summary>
  public JsonElement? StructuredDetails { get; set; }

  /// <summary>The retryable member of the wire error; null when the code says nothing about it.</summary>
  public bool? Retryable { get; set; }

  protected Result() { }

  public static Result Success() => new() { ResultStatus = ResultStatus.Success };
  public static Result Failure(ResultStatus status, string error) => new() { ResultStatus = status, Errors = [error] };
  public static Result Failure(ResultStatus status, IList<string> errors) => new() { ResultStatus = status, Errors = errors };
  public static Result Failure(ResultStatus status, string code, string message, IReadOnlyList<ErrorDetail>? details = null) =>
    new() { ResultStatus = status, ErrorCode = code, Errors = [message], ErrorDetails = details ?? [] };
  public static Result Failure(ResultStatus status, string code, string message, JsonElement? structuredDetails, bool? retryable) =>
    new() { ResultStatus = status, ErrorCode = code, Errors = [message], StructuredDetails = structuredDetails, Retryable = retryable };
}

/// <summary>Outcome of an operation that returns data.</summary>
public sealed class Result<T> : Result
{
  public T? Data { get; set; }

  private Result() { }

  public static new Result<T> Success(T? data = default) => new() { ResultStatus = ResultStatus.Success, Data = data };
  public static new Result<T> Failure(ResultStatus status, string error) => new() { ResultStatus = status, Errors = [error] };
  public static new Result<T> Failure(ResultStatus status, IList<string> errors) => new() { ResultStatus = status, Errors = errors };
  public static new Result<T> Failure(ResultStatus status, string code, string message, IReadOnlyList<ErrorDetail>? details = null) =>
    new() { ResultStatus = status, ErrorCode = code, Errors = [message], ErrorDetails = details ?? [] };
  public static new Result<T> Failure(ResultStatus status, string code, string message, JsonElement? structuredDetails, bool? retryable) =>
    new() { ResultStatus = status, ErrorCode = code, Errors = [message], StructuredDetails = structuredDetails, Retryable = retryable };
}
