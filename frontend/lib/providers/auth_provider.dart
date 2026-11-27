import 'package:flutter/foundation.dart';

import 'dart:async';

import '../core/services/country_sync_service.dart';
import '../core/services/location_service.dart';
import '../core/models/api_response.dart';
import '../core/storage/auth_storage.dart';
import '../features/auth/models/auth_response.dart';
import '../features/auth/services/auth_api_service.dart';

class AuthProvider extends ChangeNotifier {
  AuthProvider({
    AuthApiService? authApiService,
    LocationService? locationService,
  }) : _authApiService = authApiService ?? AuthApiService(),
        _countrySyncService = CountrySyncService(
          countryLookup: (locationService ?? LocationService()).getCountryCode,
        ),
        _authStorage = AuthStorage.instance {
    AuthStorage.onSessionExpired = _handleSessionExpired;
  }

  final AuthApiService _authApiService;
  final CountrySyncService _countrySyncService;
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

  // Aynı oturuma ait eş zamanlı logout işlemlerini birleştirir.
  final Map<int, Future<void>> _logoutOperations = {};
  Future<void>? _countrySyncOperation;

  // Devam eden giriş işleminin oturum nesli.
  int? _pendingLoginGeneration;

  // Kullanıcı bilgileri.
  int? _userId;
  String? _username;
  String? _email;
  String? _firstName;
  String? _lastName;
  String? _countryCode;
  bool _hasPassword = false;
  bool _emailVerified = false;

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
  bool get emailVerified => _emailVerified;

  // Provider'ın ve ilgili oturumun hâlâ geçerli
  // olup olmadığını kontrol eder.
  bool _isCurrentSession(int generation) {
    return !_isDisposed && _authStorage.isCurrentGeneration(generation);
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
      if (_isCurrentSession(generation) && _isLoggedIn) {
        unawaited(syncCountryFromDevice());
      }
    } catch (_) {
      // Eski oturuma ait başlatma işlemi yeni oturumun
      // initialization durumunu değiştiremez.
      if (_isCurrentSession(generation)) {
        _initializationFuture = null;
        rethrow;
      }
    }
  }

  /// Best-effort country sync. Location and network errors are deliberately
  /// ignored so they never prevent login or puzzle play.
  Future<void> syncCountryFromDevice({bool force = false}) {
    if (_isDisposed || !_isLoggedIn) return Future<void>.value();

    final current = _countrySyncOperation;
    if (current != null) return current;

    final operation = _performCountrySync(force: force);
    _countrySyncOperation = operation;
    return operation.whenComplete(() {
      if (identical(_countrySyncOperation, operation)) {
        _countrySyncOperation = null;
      }
    });
  }

  Future<void> _performCountrySync({required bool force}) async {
    final generation = _authStorage.sessionGeneration;
    if (!_isCurrentSession(generation)) return;

    try {
      final deviceCountry = await _countrySyncService
          .resolve(force: force)
          .timeout(const Duration(seconds: 6));
      if (deviceCountry == null || !_isCurrentSession(generation)) return;

      final currentCountry = _countryCode?.trim().toUpperCase();
      if (currentCountry == deviceCountry) return;

      final result = await _authApiService
          .updateCountryCode(deviceCountry)
          .timeout(const Duration(seconds: 8));
      if (!_isCurrentSession(generation) || !result.success) return;

      await _authStorage.updateCountryCode(
        deviceCountry,
        expectedGeneration: generation,
      );
      if (!_isCurrentSession(generation)) return;

      _countryCode = deviceCountry;
      _notifySafely();
    } catch (_) {
      // Permission, GPS, network, cooldown and stale-session failures keep
      // the existing account country and do not block the calling flow.
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

    if (!_isCurrentSession(generation)) return;

    final emailVerified = await _authStorage.getEmailVerified();

    if (!_isCurrentSession(generation)) return;

    _isLoggedIn = true;
    _userId = userId;
    _username = username;
    _email = email;
    _firstName = firstName;
    _lastName = lastName;
    _countryCode = countryCode;
    _hasPassword = hasPassword;
    _emailVerified = emailVerified;

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

    final operation = _authStorage.beginSessionChange();
    final generation = _authStorage.sessionGeneration;
    _countrySyncService.reset();
    _countrySyncOperation = null;
    _pendingLoginGeneration = generation;
    _initializationFuture = null;
    _clearUserData();
    _notifySafely();

    try {
      final result = await operation;
      _ensureCurrentSession(result);
      return result;
    } catch (_) {
      if (_pendingLoginGeneration == generation) {
        _pendingLoginGeneration = null;
      }
      rethrow;
    }
  }

  // Normal ve Google giriş işlemlerinin ortak oturum yönetimi.
  Future<ApiResponse<AuthResponse>> _performLogin(
      Future<ApiResponse<AuthResponse>> Function(int generation) action,
      ) {
    return _withLoading(() async {
      final generation = await _beginSessionChange();
      try {
        final result = await action(generation);
        _ensureCurrentSession(generation);
        if (result.success && result.data != null) {
          await _loadSession(generation);
          if (_isCurrentSession(generation) && _isLoggedIn) {
            unawaited(syncCountryFromDevice());
          }
        }
        _ensureCurrentSession(generation);
        return result;
      } finally {
        if (_pendingLoginGeneration == generation) {
          _pendingLoginGeneration = null;
        }
      }
    });
  }

  // Kullanıcının ülke bilgisini günceller.
  Future<ApiResponse<Map<String, dynamic>>> updateCountryCode(
      String? countryCode,
      ) async {
    final generation = _authStorage.sessionGeneration;

    _ensureCurrentSession(generation);

    final normalizedValue = countryCode?.trim().toUpperCase();

    final normalized = normalizedValue == null || normalizedValue.isEmpty
        ? null
        : normalizedValue;

    final result = await _authApiService.updateCountryCode(normalized);

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

    if (result.success && _isLoggedIn) {
      await _authStorage.updateEmailVerified(
        true,
        expectedGeneration: generation,
      );

      _ensureCurrentSession(generation);

      _emailVerified = true;

      _notifySafely();
    }

    return result;
  }


  // Ortak loading yönetimi.
  // Bir işlem tamamlandığında diğer işlemler devam ediyorsa
  // loading durumu kapatılmaz.
  Future<T> _withLoading<T>(Future<T> Function() action) async {
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
    return _performLogin(
          (generation) => _authApiService.login(
        email: email,
        password: password,
        expectedGeneration: generation,
      ),
    );
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

      _ensureCurrentSession(generation);

      final result = await _authApiService.verifyEmail(
        email: email,
        code: code,
      );

      _ensureCurrentSession(generation);

      if (result.success &&
          _isLoggedIn &&
          _email?.trim().toLowerCase() ==
              email.trim().toLowerCase()) {
        await _authStorage.updateEmailVerified(
          true,
          expectedGeneration: generation,
        );

        _ensureCurrentSession(generation);

        _emailVerified = true;

        _notifySafely();
      }

      return result;
    });
  }


  // E-posta doğrulama kodunu tekrar gönderir.
  Future<ApiResponse<Map<String, dynamic>>> resendVerification({
    required String email,
  }) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;

      final result = await _authApiService.resendVerification(email: email);

      _ensureCurrentSession(generation);

      return result;
    });
  }

  Future<ApiResponse<Map<String, dynamic>>> requestPasswordReset({
    required String email,
  }) => _withLoading(() =>
      _authApiService.requestPasswordReset(email: email));

  Future<ApiResponse<Map<String, dynamic>>> resetPassword({
    required String email,
    required String code,
    required String newPassword,
  }) => _withLoading(() => _authApiService.resetPassword(
        email: email,
        code: code,
        newPassword: newPassword,
      ));

  // Google ile giriş.
  Future<ApiResponse<AuthResponse>> loginWithGoogle() {
    return _performLogin(
          (generation) =>
          _authApiService.loginWithGoogle(expectedGeneration: generation),
    );
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
  Future<void> logout() {
    final generation = _authStorage.sessionGeneration;

    _ensureCurrentSession(generation);

    // Aynı nesle ait devam eden logout işlemini kullan.
    final existingOperation = _logoutOperations[generation];

    if (existingOperation != null) {
      return existingOperation;
    }

    final hasPendingLogin = _pendingLoginGeneration == generation;
    final operation = _performLogout(
      generation,
      hasPendingLogin: hasPendingLogin,
    );

    _logoutOperations[generation] = operation;

    return operation;
  }

  Future<ApiResponse<Map<String, dynamic>>> deleteAccount({String? currentPassword}) {
    return _withLoading(() async {
      final generation = _authStorage.sessionGeneration;
      _ensureCurrentSession(generation);
      final result = await _authApiService.deleteAccount(currentPassword: currentPassword);
      if (result.success) {
        _initializationFuture = null;
        _clearUserData();
        _notifySafely();
      }
      return result;
    });
  }

  Future<void> _performLogout(
      int generation, {
        required bool hasPendingLogin,
      }) async {
    try {
      await _withLoading(() async {
        _ensureCurrentSession(generation);

        try {
          await _authApiService.logout(
            forceSessionInvalidation: hasPendingLogin,
            onLocalSessionCleared: () {
              final currentGeneration = _authStorage.sessionGeneration;

              if (!_isCurrentSession(currentGeneration)) {
                return;
              }

              // Oturum bulunmadığında nesil değişmez.
              // Aktif oturum temizlendiğinde bir kez artar.
              if (currentGeneration != generation &&
                  currentGeneration != generation + 1) {
                return;
              }

              if (_pendingLoginGeneration == generation) {
                _pendingLoginGeneration = null;
              }

              _initializationFuture = null;

              _clearUserData();
              _notifySafely();
            },
          );
        } finally {
          final currentGeneration = _authStorage.sessionGeneration;

          if (_isCurrentSession(currentGeneration) &&
              currentGeneration == generation + 1) {
            final token = await _authStorage.getAccessToken();

            if (_isCurrentSession(currentGeneration) && token == null) {
              _initializationFuture = null;

              _clearUserData();
              _notifySafely();
            }
          }
        }
      });
    } finally {
      _logoutOperations.remove(generation);
    }
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
    _emailVerified = false;
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
