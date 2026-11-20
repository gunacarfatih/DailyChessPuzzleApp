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
      message: _localizedMessage(
        json['errorCode']?.toString(),
        json['message']?.toString(),
      ),
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

  static String? _localizedMessage(String? code, String? fallback) {
    return switch (code) {
      'INVALID_CREDENTIALS' => 'E-posta veya şifre hatalı.',
      'GOOGLE_ACCOUNT_LINK_REQUIRED' =>
      'Bu e-posta başka bir hesapta kayıtlı. E-posta ve şifrenizle giriş yapıp hesabınızı bağlayın.',
      'GOOGLE_EMAIL_NOT_VERIFIED' => 'Google e-posta adresi doğrulanmamış.',
      'USERNAME_ALREADY_EXISTS' => 'Bu kullanıcı adı zaten kullanılıyor.',
      'EMAIL_ALREADY_EXISTS' => 'Bu e-posta adresi zaten kullanılıyor.',
      'AUTHENTICATION_REQUIRED' || 'UNAUTHORIZED' =>
      'Oturumunuz sona erdi. Lütfen yeniden giriş yapın.',
      'RATE_LIMIT_EXCEEDED' =>
      'Çok fazla istek gönderdiniz. Biraz sonra tekrar deneyin.',
      _ => fallback,
    };
  }
}