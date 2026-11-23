import 'package:dio/dio.dart';
import 'package:google_sign_in/google_sign_in.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../../../core/storage/auth_storage.dart';
import '../../../core/storage/client_id_storage.dart';
import '../models/auth_response.dart';

class AuthApiService {
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

  // JWT gerektiren isteklerde kullanılacak Authorization başlığı.
  Future<Options> _getAuthenticatedOptions() async {
    final token = await _authStorage.getAccessToken();

    if (token == null || token.isEmpty) {
      throw Exception('Oturum süresi dolmuş. Lütfen tekrar giriş yap.');
    }

    return Options(
      headers: {
        'Authorization': 'Bearer $token',
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

    return _postMap(ApiConstants.registerEndpoint, requestData);
  }

  Future<ApiResponse<Map<String, dynamic>>> verifyEmail({
    required String email,
    required String code,
  }) async {
    return _postMap(ApiConstants.verifyEmailEndpoint, {
      'email': email.trim(),
      'code': code.trim(),
    });
  }

  Future<ApiResponse<Map<String, dynamic>>> resendVerification({
    required String email,
  }) async {
    return _postMap(ApiConstants.resendVerificationEndpoint, {
      'email': email.trim(),
    });
  }

  Future<ApiResponse<Map<String, dynamic>>> _postMap(
    String path,
    Map<String, dynamic> data,
  ) async {
    dynamic responseData;
    try {
      final response = await _dio.post(path, data: data);
      responseData = response.data;
    } on DioException catch (exception) {
      responseData = exception.response?.data;
      if (responseData is! Map) rethrow;
    }

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(responseData as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
  }

  // Mevcut kullanıcıyla e-posta ve şifre üzerinden giriş yapar.
  Future<ApiResponse<AuthResponse>> login({
    required String email,
    required String password,
  }) async {
    final response = await _dio.post(
      ApiConstants.loginEndpoint,
      data: {
        'email': email.trim(),
        'password': password,
      },
    );

    final result = ApiResponse<AuthResponse>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => AuthResponse.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );

    if (result.success && result.data != null) {
      await _authStorage.saveSession(result.data!);
    }

    return result;
  }

  // Google hesabıyla giriş yapar.
  //
  // 1. Google Sign-In başlatılır.
  // 2. Kullanıcı Google hesabını seçer.
  // 3. Google ID Token alınır.
  // 4. Token backend'e gönderilir.
  // 5. Backend JWT'si güvenli şekilde saklanır.
  Future<ApiResponse<AuthResponse>> loginWithGoogle() async {
    await _initializeGoogleSignIn();

    // Google hesap seçim ekranını aç.
    final GoogleSignInAccount account =
    await _googleSignIn.authenticate();

    // Google ID Token al.
    final GoogleSignInAuthentication authentication =
        account.authentication;

    final String? idToken = authentication.idToken;

    if (idToken == null || idToken.isEmpty) {
      throw Exception('Google ID Token alınamadı.');
    }

    // Mevcut misafir ClientId değerini al.
    final clientId = await _clientIdStorage.getClientId();

    // Google ID Token'ı backend'e gönder.
    final response = await _dio.post(
      _googleLoginEndpoint,
      data: {
        'idToken': idToken,
        'clientId': clientId,
      },
    );

    final result = ApiResponse<AuthResponse>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => AuthResponse.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );

    // Google token'ı değil, backend'in ürettiği JWT saklanır.
    if (result.success && result.data != null) {
      await _authStorage.saveSession(result.data!);
    }

    return result;
  }

  Future<ApiResponse<Map<String, dynamic>>> linkGoogleAccount({
    required String currentPassword,
  }) async {
    await _initializeGoogleSignIn();
    final account = await _googleSignIn.authenticate();
    final idToken = account.authentication.idToken;
    if (idToken == null || idToken.isEmpty) {
      throw Exception('Google ID Token alınamadı.');
    }

    final response = await _dio.post(
      '/api/auth/link-google',
      data: {
        'idToken': idToken,
        'currentPassword': currentPassword,
      },
      options: await _getAuthenticatedOptions(),
    );
    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
  }

  // Google hesabı olan ve henüz şifresi bulunmayan kullanıcı
  // için ilk şifreyi belirler.
  Future<ApiResponse<Map<String, dynamic>>> setPassword({
    required String newPassword,
  }) async {
    final options = await _getAuthenticatedOptions();

    final response = await _dio.post(
      _setPasswordEndpoint,
      data: {
        'newPassword': newPassword,
      },
      options: options,
    );

    final result = ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );

    return result;
  }

  // Mevcut şifresi olan kullanıcının şifresini değiştirir.
  Future<ApiResponse<Map<String, dynamic>>> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    final options = await _getAuthenticatedOptions();

    final response = await _dio.post(
      _changePasswordEndpoint,
      data: {
        'currentPassword': currentPassword,
        'newPassword': newPassword,
      },
      options: options,
    );

    final result = ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );

    if (result.success) {
      await _authStorage.updateHasPassword(true);
    }

    return result;
  }

  Future<ApiResponse<Map<String, dynamic>>> updateCountryCode(
    String? countryCode,
  ) async {
    final token = await _authStorage.getAccessToken();
    if (token == null || token.isEmpty) {
      throw Exception('AUTH_SESSION_EXPIRED');
    }

    final response = await _dio.patch(
      ApiConstants.updateCountryEndpoint,
      data: {'countryCode': countryCode},
    );
    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
  }

  // Kullanıcı oturumunu kapatır.
  Future<void> logout() async {
    final refreshToken =
    await _authStorage.getRefreshToken();

    // Yerel oturumda refresh token yoksa temizlenecek
    // sunucu oturumu da bulunmaz.
    if (refreshToken == null || refreshToken.isEmpty) {
      await _authStorage.clearSession();
      return;
    }

    final response = await _dio.post(
      ApiConstants.logoutEndpoint,
      data: {
        'refreshToken': refreshToken,
      },
    );

    if (response.statusCode != 200) {
      throw Exception(
        'Sunucu oturumu kapatamadı. Lütfen tekrar deneyin.',
      );
    }

    // Sunucu refresh token'ı iptal ettikten sonra
    // cihazdaki oturum bilgilerini temizle.
    await _authStorage.clearSession();
  }
}
