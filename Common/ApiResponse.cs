namespace HauntedVoiceUniverse.Common;

/// <summary>
/// Standard API Response - sabhi APIs yahi format return karenge
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }
    public int? StatusCode { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Success") =>
        new() { Success = true, Message = message, Data = data, StatusCode = 200 };

    public static ApiResponse<T> Created(T data, string message = "Created successfully") =>
        new() { Success = true, Message = message, Data = data, StatusCode = 201 };

    public static ApiResponse<T> Fail(string message, int statusCode = 400) =>
        new() { Success = false, Message = message, StatusCode = statusCode };

    public static ApiResponse<T> Fail(List<string> errors, string message = "Validation failed") =>
        new() { Success = false, Message = message, Errors = errors, StatusCode = 400 };

    public static ApiResponse<T> Unauthorized(string message = "Unauthorized access") =>
        new() { Success = false, Message = message, StatusCode = 401 };

    public static ApiResponse<T> Forbidden(string message = "Access denied") =>
        new() { Success = false, Message = message, StatusCode = 403 };

    public static ApiResponse<T> NotFound(string message = "Not found") =>
        new() { Success = false, Message = message, StatusCode = 404 };

    public static ApiResponse<T> ServerError(string message = "Internal server error") =>
        new() { Success = false, Message = message, StatusCode = 500 };
}

// Non-generic version for responses with no data
public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse OkNoData(string message = "Success") =>
        new() { Success = true, Message = message, StatusCode = 200 };

    public static new ApiResponse Fail(string message, int statusCode = 400) =>
        new() { Success = false, Message = message, StatusCode = statusCode };
}