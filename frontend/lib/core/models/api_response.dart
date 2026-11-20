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
      'GOOGLE_EMAIL_MISMATCH' => 'Google hesabının e-posta adresi eşleşmiyor.',
      'GOOGLE_ACCOUNT_ALREADY_LINKED' => 'Bu Google hesabı başka bir kullanıcıya bağlı.',
      'USERNAME_ALREADY_EXISTS' => 'Bu kullanıcı adı zaten kullanılıyor.',
      'EMAIL_ALREADY_EXISTS' => 'Bu e-posta adresi zaten kullanılıyor.',
      'EMAIL_NOT_VERIFIED' => 'Giriş yapmadan önce e-posta adresini doğrula.',
      'EMAIL_VERIFICATION_CODE_INVALID' => 'Doğrulama kodu yanlış veya süresi dolmuş.',
      'EMAIL_DELIVERY_NOT_CONFIGURED' => 'E-posta gönderimi sunucuda henüz yapılandırılmamış.',
      'EMAIL_DELIVERY_FAILED' => 'Doğrulama e-postası gönderilemedi. Yeniden göndermeyi dene.',
      'USER_NOT_FOUND' => 'Kullanıcı bulunamadı.',
      'USER_ID_NOT_FOUND' => 'Kullanıcı oturumu doğrulanamadı. Yeniden giriş yapın.',
      'PASSWORD_NOT_SET' => 'Önce hesabınız için bir şifre belirleyin.',
      'PASSWORD_ALREADY_SET' => 'Bu hesapta zaten bir şifre var.',
      'SESSION_NOT_FOUND' => 'Oyun oturumu bulunamadı. Bulmacayı yeniden başlatın.',
      'SESSION_NOT_COMPLETED' => 'Oturum henüz tamamlanmadı.',
      'BONUS_NOT_FOUND' => 'Bonus bulmaca bulunamadı.',
      'BONUS_NOT_STARTED' => 'Önce bonus bulmacayı başlatın.',
      'BONUS_NOT_SOLVED' => 'Bonus bulmaca henüz çözülmedi.',
      'BONUS_ALREADY_SOLVED' => 'Bonus bulmaca zaten çözüldü.',
      'BONUS_ALREADY_COMPLETED' => 'Bonus bulmaca zaten tamamlandı.',
      'BONUS_TIME_EXPIRED' => 'Bonus bulmacanın süresi doldu.',
      'PUZZLE_NOT_FOUND' => 'Bugünün bulmacası henüz hazır değil.',
      'LEADERBOARD_RANGE_TOO_LARGE' => 'Tarih aralığı en fazla 92 gün olabilir.',
      'AUTHENTICATION_REQUIRED' || 'UNAUTHORIZED' =>
      'Oturumunuz sona erdi. Lütfen yeniden giriş yapın.',
      'RATE_LIMIT_EXCEEDED' =>
      'Çok fazla istek gönderdiniz. Biraz sonra tekrar deneyin.',
      _ => fallback,
    };
  }
}
