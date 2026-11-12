
import 'package:dio/dio.dart';
import 'package:google_sign_in/google_sign_in.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../../../core/storage/auth_storage.dart';
import '../../../core/storage/client_id_storage.dart';
import '../models/auth_response.dart';

class AuthApiService {
  final Dio _dio = ApiClient.dio;

  final AuthStorage _authStorage = AuthStorage.instance;
  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  // Google Web OAuth Client ID.
  // Android Client ID değil, Web Client ID kullanılmalıdır.
  static const String _googleServerClientId =
      '50351820821-cfv637694e6k8nfjm8ve48s28u4o2pef.apps.googleusercontent.com';

  static const String _googleLoginEndpoint = '/api/auth/google';

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

  /// Yeni kullanıcı kaydı oluşturur.
  /// Misafir ClientId değerini de backend'e gönderir.
  Future<ApiResponse<AuthResponse>> register({
    required String username,
    required String email,
    required String password,
    String? countryCode,
  }) async {
    final clientId = await _clientIdStorage.getClientId();

    final requestData = <String, dynamic>{
      'username': username.trim(),
      'email': email.trim(),
      'password': password,
      'clientId': clientId,
      'countryCode': countryCode,
    };

    final response = await _dio.post(
      ApiConstants.registerEndpoint,
      data: requestData,
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

  /// Mevcut kullanıcıyla e-posta ve şifre üzerinden giriş yapar.
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

  /// Google hesabıyla giriş yapar.
  ///
  /// 1. Google Sign-In başlatılır.
  /// 2. Kullanıcı Google hesabını seçer.
  /// 3. Google ID Token alınır.
  /// 4. Token backend'e gönderilir.
  /// 5. Backend JWT'si güvenli şekilde saklanır.
  Future<ApiResponse<AuthResponse>> loginWithGoogle() async {
    await _initializeGoogleSignIn();

    // Google hesap seçim ekranını aç.
    final GoogleSignInAccount account =
    await _googleSignIn.authenticate();

    // Google ID Token al.
    final GoogleSignInAuthentication authentication =
    await account.authentication;

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

  /// Kullanıcı oturumunu kapatır.
  Future<void> logout() async {
    await _authStorage.clearSession();
  }
}
