using System.Net;
using System.Runtime.ExceptionServices;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Exceptions;

namespace DailyChessPuzzleApi.Middlewares
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        // HTTP isteği boyunca oluşabilecek hataları yakalar.
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (
                context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "HTTP request was cancelled by the client.");

                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                await HandleExceptionAsync(
                    context,
                    ex,
                    HttpStatusCode.Unauthorized);
            }
            catch (AppException ex)
            {
                await HandleAppExceptionAsync(context, ex);
            }
            catch (KeyNotFoundException ex)
            {
                await HandleExceptionAsync(
                    context,
                    ex,
                    HttpStatusCode.NotFound);
            }
            catch (ArgumentException ex)
            {
                await HandleExceptionAsync(
                    context,
                    ex,
                    HttpStatusCode.BadRequest);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(
                    context,
                    ex,
                    HttpStatusCode.InternalServerError);
            }
        }

        // Yakalanan exception için standart API hata cevabı oluşturur.
        private async Task HandleExceptionAsync(
            HttpContext context,
            Exception exception,
            HttpStatusCode statusCode)
        {
            if (context.Response.HasStarted)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
                return;
            }

            _logger.LogError(
                exception,
                "Request failed. Method: {Method}, Path: {Path}, StatusCode: {StatusCode}",
                context.Request.Method,
                context.Request.Path,
                (int)statusCode);

            var message = statusCode switch
            {
                HttpStatusCode.Unauthorized => "Authentication is required or invalid.",
                HttpStatusCode.NotFound => "The requested resource was not found.",
                HttpStatusCode.BadRequest => "The request is invalid.",
                _ => "An unexpected error occurred."
            };

            var errorCode = statusCode switch
            {
                HttpStatusCode.BadRequest => "BAD_REQUEST",
                HttpStatusCode.Unauthorized => "UNAUTHORIZED",
                HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
                HttpStatusCode.InternalServerError =>
                    "INTERNAL_SERVER_ERROR",
                _ => "UNKNOWN_ERROR"
            };

            var errors = (List<string>?)null;

            var response = ResponseFactory.Failure<object>(
                context,
                message,
                (int)statusCode,
                errorCode,
                errors);

            context.Response.Clear();
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(response);
        }

        private async Task HandleAppExceptionAsync(
            HttpContext context,
            AppException exception)
        {
            if (context.Response.HasStarted)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
                return;
            }

            _logger.LogInformation(exception, "Request rejected with {ErrorCode}", exception.ErrorCode);
            var response = ResponseFactory.Failure<object>(
                context,
                "The request could not be completed.",
                exception.StatusCode,
                exception.ErrorCode);
            context.Response.Clear();
            context.Response.StatusCode = exception.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
