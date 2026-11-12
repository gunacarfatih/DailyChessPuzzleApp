
using System.Net;
using System.Runtime.ExceptionServices;
using DailyChessPuzzleApi.Responses;

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
            catch (KeyNotFoundException ex)
            {
                await HandleExceptionAsync(
                    context,
                    ex,
                    HttpStatusCode.NotFound);
            }
            catch (InvalidOperationException ex)
            {
                await HandleExceptionAsync(
                    context,
                    ex,
                    HttpStatusCode.Conflict);
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

            var isInternalServerError =
                statusCode == HttpStatusCode.InternalServerError;

            var message = isInternalServerError
                ? "An unexpected error occurred."
                : exception.Message;

            var errorCode = statusCode switch
            {
                HttpStatusCode.BadRequest => "BAD_REQUEST",
                HttpStatusCode.Unauthorized => "UNAUTHORIZED",
                HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
                HttpStatusCode.Conflict => "CONFLICT",
                HttpStatusCode.InternalServerError =>
                    "INTERNAL_SERVER_ERROR",
                _ => "UNKNOWN_ERROR"
            };

            var errors = isInternalServerError
                ? null
                : new List<string> { exception.Message };

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
    }
}
