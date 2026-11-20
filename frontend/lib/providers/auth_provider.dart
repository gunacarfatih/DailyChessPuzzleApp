
import 'package:flutter/foundation.dart';

import '../core/models/api_response.dart';
import '../core/storage/auth_storage.dart';
import '../features/auth/models/auth_response.dart';
import '../features/auth/services/auth_api_service.dart';

class AuthProvider extends ChangeNotifier {
  AuthProvider() {
    AuthStorage.onSessionExpired = _handleSessionExpired;
  }

  final AuthApiService _authApiService = AuthApiService();
  final AuthStorage _authStorage = AuthStorage.instance;

  // Oturum durumu
  bool _isLoggedIn = false;
  bool _isLoading = false;

  // Oturum başlatma işleminin tekrar çalışmasını önler.
  Future<void>? _initializationFuture;

  // Kullanıcı bilgileri
  int? _userId;
  String? _username;
  String? _email;
  String? _firstName;
  String? _lastName;
  String? _countryCode;
  bool _hasPassword = false;

  // Dışarıya açılan değişkenler
  bool get isLoggedIn => _isLoggedIn;
  bool get isLoading => _isLoading;

  int? get userId => _userId;
  String? get username => _username;
  String? get email => _email;
  String? get firstName => _firstName;
  String? get lastName => _lastName;
  String? get countryCode => _countryCode;
  bool get hasPassword => _hasPassword;

  // Uygulama açıldığında mevcut oturumu kontrol eder.
  // Birden fazla çağrıda aynı başlatma işlemini kullanır.
  Future<void> initialize() {
    return _initializationFuture ??= _initializeSession();
  }

  Future<void> _initializeSession() async {
    try {
      await _loadSession();
    } catch (error) {
      // Yükleme başarısız olursa tekrar denenebilmesine izin ver.
      _initializationFuture = null;
      rethrow;
    }
  }

  // Kayıtlı oturum bilgilerini güvenli depolamadan okur.
  Future<void> _loadSession() async {
    final token = await _authStorage.getAccessToken();

    if (token == null) {
      _clearUserData();
      notifyListeners();
      return;
    }

    _isLoggedIn = true;
    _userId = await _authStorage.getUserId();
    _username = await _authStorage.getUsername();
    _email = await _authStorage.getEmail();
    _firstName = await _authStorage.getFirstName();
    _lastName = await _authStorage.getLastName();
    _countryCode = await _authStorage.getCountryCode();
    _hasPassword = await _authStorage.getHasPassword();

    notifyListeners();
  }

  Future<void> _handleSessionExpired() async {
    _clearUserData();
    notifyListeners();
  }

  Future<ApiResponse<Map<String, dynamic>>> updateCountryCode(
    String? countryCode,
  ) async {
    final normalized = countryCode?.trim().toUpperCase();
    final result = await _authApiService.updateCountryCode(
      normalized == null || normalized.isEmpty ? null : normalized,
    );
    if (result.success) {
      _countryCode = normalized;
      await _authStorage.updateCountryCode(normalized);
      notifyListeners();
    }
    return result;
  }

  Future<ApiResponse<Map<String, dynamic>>> linkGoogleAccount({
    required String currentPassword,
  }) {
    return _authApiService.linkGoogleAccount(
      currentPassword: currentPassword,
    );
  }

  // Ortak loading yönetimi.
  Future<T> _withLoading<T>(Future<T> Function() action) async {
    _isLoading = true;
    notifyListeners();

    try {
      return await action();
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  // E-posta ve şifre ile giriş.
  Future<ApiResponse<AuthResponse>> login({
    required String email,
    required String password,
  }) {
    return _withLoading(() async {
      final result = await _authApiService.login(
        email: email,
        password: password,
      );

      if (result.success && result.data != null) {
        await _loadSession();
      }

      return result;
    });
  }

  // Yeni kullanıcı kaydı.
  Future<ApiResponse<AuthResponse>> register({
    required String username,
    required String firstName,
    required String lastName,
    required String email,
    required String password,
    String? countryCode,
  }) {
    return _withLoading(() async {
      final result = await _authApiService.register(
        username: username,
        firstName: firstName,
        lastName: lastName,
        email: email,
        password: password,
        countryCode: countryCode,
      );

      if (result.success && result.data != null) {
        await _loadSession();
      }

      return result;
    });
  }

  // Google ile giriş.
  Future<ApiResponse<AuthResponse>> loginWithGoogle() {
    return _withLoading(() async {
      final result = await _authApiService.loginWithGoogle();

      if (result.success && result.data != null) {
        await _loadSession();
      }

      return result;
    });
  }

  // Google hesabı için ilk şifreyi belirleme.
  Future<ApiResponse<Map<String, dynamic>>> setPassword({
    required String newPassword,
  }) {
    return _withLoading(() async {
      final result = await _authApiService.setPassword(
        newPassword: newPassword,
      );

      if (result.success) {
        _hasPassword = true;
        notifyListeners();
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
      final result = await _authApiService.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      );

      if (result.success) {
        _hasPassword = true;
        notifyListeners();
      }

      return result;
    });
  }

  // Oturumu kapatma.
  Future<void> logout() async {
    await _withLoading(() async {
      await _authApiService.logout();
      _clearUserData();
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
    AuthStorage.onSessionExpired = null;
    super.dispose();
  }
}
