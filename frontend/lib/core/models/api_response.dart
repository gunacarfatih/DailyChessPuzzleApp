
class ApiResponse<T> {
  final bool success;
  final String? message;
  final int? status;
  final T? data;
  final List<dynamic> errors;
  final String? errorCode;
  final String? traceId;
  final DateTime? timestamp;

  const ApiResponse({
    required this.success,
    this.message,
    this.status,
    this.data,
    this.errors = const [],
    this.errorCode,
    this.traceId,
    this.timestamp,
  });

  factory ApiResponse.fromJson(
      Map<String, dynamic> json, {
        T Function(dynamic json)? fromJsonT,
      }) {
    final rawData = json['data'];

    return ApiResponse<T>(
      success: json['success'] == true,
      message: json['message']?.toString(),
      status: (json['status'] as num?)?.toInt(),
      data: rawData == null
          ? null
          : fromJsonT != null
          ? fromJsonT(rawData)
          : rawData as T,
      errors: json['errors'] is List
          ? List<dynamic>.from(json['errors'])
          : const [],
      errorCode: json['errorCode']?.toString(),
      traceId: json['traceId']?.toString(),
      timestamp: DateTime.tryParse(
        json['timestamp']?.toString() ?? '',
      ),
    );
  }
}
