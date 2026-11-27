import 'package:dio/dio.dart';
import 'package:google_sign_in/google_sign_in.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../../../core/storage/auth_storage.dart';
import '../../../core/storage/client_id_storage.dart';
import '../models/auth_response.dart';
import 'package:flutter/foundation.dart';

class AuthApiService {
  @visibleForTesting
  static HttpClientAdapter Function()? logoutAdapterFactory;
  AuthApiService({
    Dio? dio,
    ClientIdStorage? clientIdStorage,
  })  : _dio = dio ?? ApiClient.dio,
        _authStorage = AuthStorage.instance,
        _clientIdStorage = clientIdStorage ?? ClientIdStorage();

  final Dio _dio;
  final AuthStorage _authStorage;
  final ClientIdStorage _clientIdStorage;

  // Google Web OAuth Client ID.
  // Android Client ID değil, Web Client ID kullanılmalıdır.
  static const String _googleServerClientId =
      '50351820821-cfv637694e6k8nfjm8ve48s28u4o2pef.apps.googleusercontent.com';

  static const String _googleLoginEndpoint = '/api/auth/google';
  static const String _setPasswordEndpoint = '/api/auth/set-password';
  static const String _changePasswordEndpoint = '/api/auth/change-password';

  // Google Sign-In singleton instance.
  static final GoogleSignIn _googleSignIn = GoogleSignIn.instance;

  // Google Sign-In yalnızca bir kez initialize edilir.
  static Future<void>? _googleInitialization;

  Future<void> _initializeGoogleSignIn() async {
    _googleInitialization ??= _googleSignIn.initialize(
      serverClientId: _googleServerClientId,
    );

    await _googleInitialization;
  }

  // İsteğin ait olduğu oturum neslini belirler.
  Options _getSessionOptions(int generation) {
    _authStorage.ensureCurrentGeneration(generation);

    return Options(
      extra: {
        ApiClient.sessionGenerationKey: generation,
      },
    );
  }

  // JWT gerektiren isteklerde Authorization başlığı
  // ve ilgili oturum nesli birlikte gönderilir.
  Future<Options> _getAuthenticatedOptions(
      int generation,
      ) async {
    _authStorage.ensureCurrentGeneration(generation);

    final token = await _authStorage.getAccessToken();

    _authStorage.ensureCurrentGeneration(generation);

    if (token == null || token.isEmpty) {
      throw Exception(
        'Oturum süresi dolmuş. Lütfen tekrar giriş yap.',
      );
    }

    return Options(
      headers: {
        'Authorization': 'Bearer $token',
      },
      extra: {
        ApiClient.sessionGenerationKey: generation,
      },
    );
  }

  // Yeni kullanıcı kaydı oluşturur.
  // Misafir ClientId gönderilmez.
  // Yeni hesap bağımsız olarak oluşturulur.
  Future<ApiResponse<Map<String, dynamic>>> register({
    required String username,
    required String firstName,
    required String lastName,
    required String email,
    required String password,
    String? countryCode,
  }) async {
    final requestData = <String, dynamic>{
      'username': username.trim(),
      'firstName': firstName.trim(),
      'lastName': lastName.trim(),
      'email': email.trim(),
      'password': password,
      'countryCode': countryCode,
    };

    return _postMap(
      ApiConstants.registerEndpoint,
      requestData,
    );
  }


  Future<ApiResponse<Map<String, dynamic>>> verifyEmail({
    required String email,
    required String code,
  }) async {
    return _postMap(
      ApiConstants.verifyEmailEndpoint,
      {
        'email': email.trim(),
        'code': code.trim(),
      },
      authenticated: true,
    );
  }

  Future<ApiResponse<Map<String, dynamic>>> resendVerification({
    required String email,
  }) async {
    return _postMap(
      ApiConstants.resendVerificationEndpoint,
      {
        'email': email.trim(),
      },
      authenticated: true,
    );
  }

  Future<ApiResponse<Map<String, dynamic>>> requestPasswordReset({
    required String email,
  }) => _postMap(ApiConstants.requestPasswordResetEndpoint, {
        'email': email.trim(),
      });

  Future<ApiResponse<Map<String, dynamic>>> resetPassword({
    required String email,
    required String code,
    required String newPassword,
  }) => _postMap(ApiConstants.resetPasswordEndpoint, {
        'email': email.trim(),
        'code': code.trim(),
        'newPassword': newPassword,
      });


  Future<ApiResponse<Map<String, dynamic>>> _postMap(
      String path,
      Map<String, dynamic> data, {
        bool authenticated = false,
      }) async {
    final generation = _authStorage.sessionGeneration;

    dynamic responseData;

    try {
      final options = authenticated
          ? await _getAuthenticatedOptions(generation)
          : _getSessionOptions(generation);

      _authStorage.ensureCurrentGeneration(generation);

      final response = await _dio.post(
        path,
        data: data,
        options: options,
      );

      responseData = response.data;
    } on DioException catch (exception) {
      _authStorage.ensureCurrentGeneration(generation);

      responseData = exception.response?.data;

      if (responseData is! Map) rethrow;
    }

    _authStorage.ensureCurrentGeneration(generation);

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(responseData as Map),
      fromJsonT: (json) =>
      Map<String, dynamic>.from(json as Map),
    );
  }

  // Mevcut kullanıcıyla e-posta ve şifre üzerinden giriş yapar.
  //
  // Oturum değişikliği AuthProvider tarafından başlatılır.
  // Giriş sonucu yalnızca ilgili oturum nesline kaydedilir.
  Future<ApiResponse<AuthResponse>> login({
    required String email,
    required String password,
    int? expectedGeneration,
  }) async {
    final generation = expectedGeneration ??
        await _authStorage.beginSessionChange();

    _authStorage.ensureCurrentGeneration(generation);

    final response = await _dio.post(
      ApiConstants.loginEndpoint,
      data: {
        'email': email.trim(),
        'password': password,
      },
      options: _getSessionOptions(generation),
    );

    _authStorage.ensureCurrentGeneration(generation);

    final result = ApiResponse<AuthResponse>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => AuthResponse.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );

    if (result.success && result.data != null) {
      await _authStorage.saveSession(
        result.data!,
        expectedGeneration: generation,
      );
    }

    _authStorage.ensureCurrentGeneration(generation);

    return result;
  }

  // Google hesabıyla giriş yapar.
  //
  // 1. Google Sign-In başlatılır.
  // 2. Kullanıcı Google hesabını seçer.
  // 3. Google ID Token alınır.
  // 4. Token backend'e gönderilir.
  // 5. Backend JWT'si güvenli şekilde saklanır.
  //
  // Her asenkron aşamadan sonra oturum nesli kontrol edilir.
  Future<ApiResponse<AuthResponse>> loginWithGoogle({
    int? expectedGeneration,
  }) async {
    final generation = expectedGeneration ??
        await _authStorage.beginSessionChange();

    _authStorage.ensureCurrentGeneration(generation);

    await _initializeGoogleSignIn();

    _authStorage.ensureCurrentGeneration(generation);

    // Google hesap seçim ekranını aç.
    final GoogleSignInAccount account =
    await _googleSignIn.authenticate();

    _authStorage.ensureCurrentGeneration(generation);

    // Google ID Token al.
    final GoogleSignInAuthentication authentication =
        account.authentication;

    final String? idToken = authentication.idToken;

    if (idToken == null || idToken.isEmpty) {
      throw Exception('Google ID Token alınamadı.');
    }

    // Mevcut misafir ClientId değerini al.
    final clientId = await _clientIdStorage.getClientId();

    _authStorage.ensureCurrentGeneration(generation);

    // Google ID Token'ı backend'e gönder.
    final response = await _dio.post(
      _googleLoginEndpoint,
      data: {
        'idToken': idToken,
        'clientId': clientId,
      },
      options: _getSessionOptions(generation),
    );

    _authStorage.ensureCurrentGeneration(generation);

    final result = ApiResponse<AuthResponse>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => AuthResponse.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );

    // Google token'ı değil, backend'in ürettiği JWT saklanır.
    if (result.success && result.data != null) {
      await _authStorage.saveSession(
        result.data!,
        expectedGeneration: generation,
      );
    }

    _authStorage.ensureCurrentGeneration(generation);

    return result;
  }

  // Mevcut kullanıcı hesabına Google hesabı bağlar.
  Future<ApiResponse<Map<String, dynamic>>> linkGoogleAccount({
    required String currentPassword,
  }) async {
    final generation = _authStorage.sessionGeneration;

    await _initializeGoogleSignIn();

    _authStorage.ensureCurrentGeneration(generation);

    final account = await _googleSignIn.authenticate();

    // Google hesap seçimi sırasında oturum değişmiş olabilir.
    _authStorage.ensureCurrentGeneration(generation);

    final idToken = account.authentication.idToken;

    if (idToken == null || idToken.isEmpty) {
      throw Exception('Google ID Token alınamadı.');
    }

    final options = await _getAuthenticatedOptions(
      generation,
    );

    _authStorage.ensureCurrentGeneration(generation);

    final response = await _dio.post(
      '/api/auth/link-google',
      data: {
        'idToken': idToken,
        'currentPassword': currentPassword,
      },
      options: options,
    );

    _authStorage.ensureCurrentGeneration(generation);

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) =>
      Map<String, dynamic>.from(json as Map),
    );
  }

  // Google hesabı olan ve henüz şifresi bulunmayan kullanıcı
  // için ilk şifreyi belirler.
  Future<ApiResponse<Map<String, dynamic>>> setPassword({
    required String newPassword,
  }) async {
    final generation = _authStorage.sessionGeneration;

    final options = await _getAuthenticatedOptions(
      generation,
    );

    _authStorage.ensureCurrentGeneration(generation);

    final response = await _dio.post(
      _setPasswordEndpoint,
      data: {
        'newPassword': newPassword,
      },
      options: options,
    );

    _authStorage.ensureCurrentGeneration(generation);

    final result = ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) =>
      Map<String, dynamic>.from(json as Map),
    );

    if (result.success) {
      await _authStorage.updateHasPassword(
        true,
        expectedGeneration: generation,
      );
    }

    _authStorage.ensureCurrentGeneration(generation);

    return result;
  }

  // Mevcut şifresi olan kullanıcının şifresini değiştirir.
  Future<ApiResponse<Map<String, dynamic>>> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    final generation = _authStorage.sessionGeneration;

    final options = await _getAuthenticatedOptions(
      generation,
    );

    _authStorage.ensureCurrentGeneration(generation);

    final response = await _dio.post(
      _changePasswordEndpoint,
      data: {
        'currentPassword': currentPassword,
        'newPassword': newPassword,
      },
      options: options,
    );

    _authStorage.ensureCurrentGeneration(generation);

    final result = ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) =>
      Map<String, dynamic>.from(json as Map),
    );

    // Backend başarılı parola değişikliğinde refresh
    // token'ları iptal eder.
    //
    // Yerel oturumun temizlenmesi AuthProvider tarafından
    // ilgili oturum nesli kontrol edilerek gerçekleştirilir.

    return result;
  }

  // Kullanıcının ülke bilgisini günceller.
  Future<ApiResponse<Map<String, dynamic>>> updateCountryCode(
      String? countryCode,
      ) async {
    final generation = _authStorage.sessionGeneration;

    final options = await _getAuthenticatedOptions(
      generation,
    );

    _authStorage.ensureCurrentGeneration(generation);

    final response = await _dio.patch(
      ApiConstants.updateCountryEndpoint,
      data: {
        'countryCode': countryCode,
      },
      options: options,
    );

    _authStorage.ensureCurrentGeneration(generation);

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) =>
      Map<String, dynamic>.from(json as Map),
    );
  }

  // Kullanıcı oturumunu kapatır.
  //
  // Yerel oturum, sunucu yanıtı beklenmeden geçersiz kılınır.
  // Böylece devam eden eski refresh işlemleri oturumu
  // yeniden oluşturamaz.
  Future<void> logout({
    bool forceSessionInvalidation = false,
    VoidCallback? onLocalSessionCleared,
  }) async {
    final generation = _authStorage.sessionGeneration;

    final refreshToken = await _authStorage.getRefreshToken();

    _authStorage.ensureCurrentGeneration(generation);

    final accessToken = await _authStorage.getAccessToken();

    _authStorage.ensureCurrentGeneration(generation);

    // Aktif bir oturum bulunmuyorsa gereksiz yere
    // oturum neslini artırma. Devam eden login varsa
    // token bulunmasa bile nesli geçersiz kıl.
    if (!forceSessionInvalidation &&
        refreshToken == null &&
        accessToken == null) {
      onLocalSessionCleared?.call();
      return;
    }

    // Önce yerel oturumu geçersiz kıl ve temizle.
    await _authStorage.clearSession(
      expectedGeneration: generation,
    );

    if (_authStorage.isCurrentGeneration(generation + 1)) {
      onLocalSessionCleared?.call();
    }

    // Refresh token bulunmuyorsa sunucuya istek gönderilmez.
    if (refreshToken == null || refreshToken.isEmpty) {
      return;
    }

    // Logout isteği mevcut kullanıcı oturumundan bağımsızdır.
    final logoutDio = Dio(
      BaseOptions(
        baseUrl: ApiConstants.baseUrl,
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 15),
        sendTimeout: const Duration(seconds: 15),
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json',
        },
        validateStatus: (status) {
          return status != null && status < 500;
        },
      ),
    );

    try {
      final adapterFactory = logoutAdapterFactory;

      if (adapterFactory != null) {
        logoutDio.httpClientAdapter = adapterFactory();
      }

      final response = await logoutDio.post(
        ApiConstants.logoutEndpoint,
        data: {
          'refreshToken': refreshToken,
        },
      );

      if (response.statusCode != 200 &&
          response.statusCode != 401) {
        throw Exception(
          'Sunucu oturumu kapatamadı. Yerel oturum kapatıldı.',
        );
      }
    } finally {
      logoutDio.close();
    }

    // Yeni bir oturum açılmış olabileceğinden
    // burada tekrar clearSession çağrılmaz.
  }

  Future<ApiResponse<Map<String, dynamic>>> deleteAccount({String? currentPassword}) async {
    final generation = _authStorage.sessionGeneration;
    final options = await _getAuthenticatedOptions(generation);
    String? googleIdToken;
    if (currentPassword == null) {
      await _initializeGoogleSignIn();
      await _googleSignIn.signOut();
      final account = await _googleSignIn.authenticate();
      googleIdToken = account.authentication.idToken;
      if (googleIdToken == null) throw Exception('Google doğrulaması başarısız.');
    }
    _authStorage.ensureCurrentGeneration(generation);
    final response = await _dio.delete(
      '/api/auth/account',
      data: {'currentPassword': ?currentPassword,
        'googleIdToken': ?googleIdToken},
      options: options,
    );
    _authStorage.ensureCurrentGeneration(generation);
    final result = ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
    if (result.success) {
      await _authStorage.clearSession(expectedGeneration: generation);
    }
    return result;
  }
}
