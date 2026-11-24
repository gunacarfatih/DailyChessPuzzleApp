
import 'package:flutter/foundation.dart';

import '../core/models/api_response.dart';
import '../core/storage/auth_storage.dart';
import '../features/auth/models/auth_response.dart';
import '../features/auth/services/auth_api_service.dart';

class AuthProvider extends ChangeNotifier {
  AuthProvider({AuthApiService? authApiService})
      : _authApiService = authApiService ?? AuthApiService(),
        _authStorage = AuthStorage.instance {
    AuthStorage.onSessionExpired = _handleSessionExpired;
  }

  final AuthApiService _authApiService;
  final AuthStorage _authStorage;

  // Oturum durumu.
  bool _isLoggedIn = false;
  bool _isLoading = false;

  // Eş zamanlı loading işlemlerinin sayısı.
  int _loadingOperations = 0;

  // Provider dispose edildikten sonra bildirim gönderilmesini önler.
  bool _isDisposed = false;

  // Oturum başlatma işleminin tekrar çalışmasını önler.
  Future<void>? _initializationFuture;

  // Kullanıcı bilgileri.
  int? _userId;
  String? _username;
  String? _email;
  String? _firstName;
  String? _lastName;
  String? _countryCode;
  bool _hasPassword = false;

  // Dışarıya açılan değişkenler.
  bool get isLoggedIn => _isLoggedIn;
  bool get isLoading => _isLoading;

  int? get userId => _userId;
  String? get username => _username;
  String? get email => _email;
  String? get firstName => _firstName;
  String? get lastName => _lastName;
  String? get countryCode => _countryCode;
  bool get hasPassword => _hasPassword;

  // Provider'ın ve ilgili oturumun hâlâ geçerli
  // olup olmadığını kontrol eder.
  bool _isCurrentSession(int generation) {
    return !_isDisposed &&
        _authStorage.isCurrentGeneration(generation);
  }

  void _ensureCurrentSession(int generation) {
    if (!_isCurrentSession(generation)) {
      throw StateError('AUTH_SESSION_CHANGED');
    }
  }

  // Dispose edilmiş Provider üzerinden bildirim gönderilmez.
  void _notifySafely() {
    if (!_isDisposed) {
      notifyListeners();
    }
  }

  // Uygulama açıldığında mevcut oturumu kontrol eder.
  // Birden fazla çağrıda aynı başlatma işlemini kullanır.
  Future<void> initialize() {
    if (_isDisposed) {
      return Future<void>.value();
    }

    return _initializationFuture ??= _initializeSession();
  }

  Future<void> _initializeSession() async {
    final generation = _authStorage.sessionGeneration;

    try {
      await _loadSession(generation);
    } catch (_) {
      // Eski oturuma ait başlatma işlemi yeni oturumun
      // initialization durumunu değiştiremez.
      if (_isCurrentSession(generation)) {
        _initializationFuture = null;
        rethrow;
      }
    }
  }

  // Kayıtlı oturum bilgilerini güvenli depolamadan okur.
  Future<void> _loadSession(int generation) async {
    if (!_isCurrentSession(generation)) return;

    final token = await _authStorage.getAccessToken();

    if (!_isCurrentSession(generation)) return;

    if (token == null || token.isEmpty) {
      _clearUserData();
      _notifySafely();
      return;
    }

    // Kullanıcı bilgileri önce yerel değişkenlere alınır.
    // Okuma işlemi tamamlanmadan Provider state'i değiştirilmez.
    final userId = await _authStorage.getUserId();

    if (!_isCurrentSession(generation)) return;

    final username = await _authStorage.getUsername();

    if (!_isCurrentSession(generation)) return;

    final email = await _authStorage.getEmail();

    if (!_isCurrentSession(generation)) return;

    final firstName = await _authStorage.getFirstName();

    if (!_isCurrentSession(generation)) return;

    final lastName = await _authStorage.getLastName();

    if (!_isCurrentSession(generation)) return;

    final countryCode = await _authStorage.getCountryCode();

    if (!_isCurrentSession(generation)) return;

    final hasPassword = await _authStorage.getHasPassword();

    // Bütün okumalar tamamlandıktan sonra son oturum kontrolü.
    if (!_isCurrentSession(generation)) return;

    _isLoggedIn = true;
    _userId = userId;
    _username = username;
    _email = email;
    _firstName = firstName;
    _lastName = lastName;
    _countryCode = countryCode;
    _hasPassword = hasPassword;

    _notifySafely();
  }

  // Yalnızca geçerli oturumun sona erme bildirimi işlenir.
  Future<void> _handleSessionExpired(int generation) async {
    if (!_isCurrentSession(generation)) return;

    _initializationFuture = null;

    _clearUserData();
    _notifySafely();
  }

  // Yeni giriş işlemi için önceki oturumu geçersiz kılar.
  Future<int> _beginSessionChange() async {
    if (_isDisposed) {
      throw StateError('AUTH_PROVIDER_DISPOSED');
    }

    // beginSessionChange çağrısı önceki nesli geçersiz kılar.
    final operation = _authStorage.beginSessionChange();

    _initializationFuture = null;

    _clearUserData();
    _notifySafely();

    final generation = await operation;

    _ensureCurrentSession(generation);

    return generation;
  }

  // Kullanıcının ülke bilgisini günceller.
  Future<ApiResponse<Map<String, dynamic>>> updateCountryCode(
      String? countryCode,
      ) async {
    final generation = _authStorage.sessionGeneration;

    _ensureCurrentSession(generation);

    final normalizedValue = countryCode?.trim().toUpperCase();

    final normalized =
    normalizedValue == null || normalizedValue.isEmpty
        ? null
        : normalizedValue;

    final result = await _authApiService.updateCountryCode(
      normalized,
    );

    _ensureCurrentSession(generation);

    if (result.success) {
      await _authStorage.updateCountryCode(
        normalized,
        expectedGeneration: generation,
      );

      _ensureCurrentSession(generation);

      _countryCode = normalized;

      _notifySafely();
    }

    return result;
  }

  // Mevcut kullanıcı hesabına Google hesabı bağlar.
  Future<ApiResponse<Map<String, dynamic>>> linkGoogleAccount({
    required String currentPassword,
  }) async {
    final generation = _authStorage.sessionGeneration;

    _ensureCurrentSession(generation);

    final result = await _authApiService.linkGoogleAccount(
      currentPassword: currentPassword,
    );

    _ensureCurrentSession(generation);

    return result;
  }

  // Ortak loading yönetimi.
  // Bir işlem tamamlandığında diğer işlemler devam ediyorsa
  // loading durumu kapatılmaz.
  Future<T> _withLoading<T>(
      Future<T> Function() action,
      ) async {
    if (_isDisposed) {
      throw StateError('AUTH_PROVIDER_DISPOSED');
    }

    _loadingOperations++;

    _isLoading = true;
    _notifySafely();

    try {
      return await action();
    } finally {
      if (_loadingOperations > 0) {
        _loadingOperations--;
      }

      _isLoading = _loadingOperations > 0;

      _notifySafely();
    }
  }

  // E-posta ve şifre ile giriş.
  Future<ApiResponse<AuthResponse>> login({
    required String email,
    required String password,
  }) {
    return _withLoading(() async {
      final generation = await _beginSessionChange();

      final result = await _authApiService.login(
        email: email,
        password: password,
        expectedGeneration: generation,
      );

      _ensureCurrentSession(generation);

      if (result.success && result.data != null) {
        await _loadSession(generation);
      }

      _ensureCurrentSession(generation);

      return result;
    });
  }

  // Yeni kullanıcı kaydı.
  Future<ApiResponse<Map<String, dynamic>>> register({
    required String username,
    required String firstName,
    required String lastName,
    required String email,
    required String password,
    String? countryCode,
  }) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      final result = await _authApiService.register(
        username: username,
        firstName: firstName,
        lastName: lastName,
        email: email,
        password: password,
        countryCode: countryCode,
      );

      _ensureCurrentSession(generation);

      return result;
    });
  }

  // E-posta doğrulama işlemi.
  Future<ApiResponse<Map<String, dynamic>>> verifyEmail({
    required String email,
    required String code,
  }) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      final result = await _authApiService.verifyEmail(
        email: email,
        code: code,
      );

      _ensureCurrentSession(generation);

      return result;
    });
  }

  // E-posta doğrulama kodunu tekrar gönderir.
  Future<ApiResponse<Map<String, dynamic>>> resendVerification({
    required String email,
  }) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      final result = await _authApiService.resendVerification(
        email: email,
      );

      _ensureCurrentSession(generation);

      return result;
    });
  }

  // Google ile giriş.
  Future<ApiResponse<AuthResponse>> loginWithGoogle() {
    return _withLoading(() async {
      final generation = await _beginSessionChange();

      final result = await _authApiService.loginWithGoogle(
        expectedGeneration: generation,
      );

      _ensureCurrentSession(generation);

      if (result.success && result.data != null) {
        await _loadSession(generation);
      }

      _ensureCurrentSession(generation);

      return result;
    });
  }

  // Google hesabı için ilk şifreyi belirleme.
  Future<ApiResponse<Map<String, dynamic>>> setPassword({
    required String newPassword,
  }) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      _ensureCurrentSession(generation);

      final result = await _authApiService.setPassword(
        newPassword: newPassword,
      );

      _ensureCurrentSession(generation);

      if (result.success) {
        // Güvenli depolama AuthApiService tarafından güncellendi.
        // Burada yalnızca Provider state'i güncellenir.
        _hasPassword = true;

        _notifySafely();
      }

      return result;
    });
  }

  // Mevcut şifreyi değiştirme.
  Future<ApiResponse<Map<String, dynamic>>> changePassword({
    required String currentPassword,
    required String newPassword,
  }) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      _ensureCurrentSession(generation);

      final result = await _authApiService.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      );

      _ensureCurrentSession(generation);

      if (result.success) {
        // Backend bütün refresh token'ları iptal etti.
        // Yalnızca parola değişikliğinin yapıldığı oturum temizlenir.
        final operation = _authStorage.clearSession(
          expectedGeneration: generation,
        );

        _initializationFuture = null;

        _clearUserData();
        _notifySafely();

        await operation;
      }

      return result;
    });
  }

  // Oturumu kapatma.
  Future<void> logout() async {
    await _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      _ensureCurrentSession(generation);

      try {
        await _authApiService.logout();
      } finally {
        if (!_isDisposed) {
          final currentGeneration =
              _authStorage.sessionGeneration;

          // Logout sırasında başka bir hesaba giriş yapılmışsa
          // yeni kullanıcının state'i temizlenmez.
          if (currentGeneration == generation + 1) {
            final token = await _authStorage.getAccessToken();

            if (_isCurrentSession(currentGeneration) &&
                token == null) {
              _initializationFuture = null;

              _clearUserData();
              _notifySafely();
            }
          }
        }
      }
    });
  }

  // Kullanıcı bilgilerini bellekte temizler.
  void _clearUserData() {
    _isLoggedIn = false;
    _userId = null;
    _username = null;
    _email = null;
    _firstName = null;
    _lastName = null;
    _countryCode = null;
    _hasPassword = false;
  }

  @override
  void dispose() {
    _isDisposed = true;

    // Başka bir Provider tarafından atanmış callback silinmez.
    if (AuthStorage.onSessionExpired == _handleSessionExpired) {
      AuthStorage.onSessionExpired = null;
    }

    super.dispose();
  }
}
