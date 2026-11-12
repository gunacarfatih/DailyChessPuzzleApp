
import 'package:dio/dio.dart';

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

  /// Mevcut kullanıcıyla giriş yapar.
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

  /// Kullanıcı oturumunu kapatır.
  Future<void> logout() async {
    await _authStorage.clearSession();
  }
}
