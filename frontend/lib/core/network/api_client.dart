import 'package:dio/dio.dart';

import '../../features/auth/models/auth_response.dart';
import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

class ApiClient {
  ApiClient._();

  static final Dio dio = _createDio();
  static Future<bool>? _refreshing;

  static Future<bool> _refreshSession() {
    return _refreshing ??= _performRefresh().whenComplete(
      () => _refreshing = null,
    );
  }

  static Future<bool> _performRefresh() async {
    final refreshToken = await AuthStorage.instance.getRefreshToken();
    if (refreshToken == null || refreshToken.isEmpty) return false;
    try {
      final response = await dio.post(
        ApiConstants.refreshTokenEndpoint,
        data: {'refreshToken': refreshToken},
      );
      if (response.statusCode != 200 ||
          response.data is! Map<String, dynamic>) {
        return false;
      }
      final envelope = response.data as Map<String, dynamic>;
      final data = envelope['data'];
      if (data is! Map<String, dynamic>) return false;
      await AuthStorage.instance.saveSession(AuthResponse.fromJson(data));
      return true;
    } catch (_) {
      return false;
    }
  }

  static Future<void> _expireSession() async {
    await AuthStorage.instance.clearSession();
    await AuthStorage.onSessionExpired?.call();
  }

  static Dio _createDio() {
    final dio = Dio(
      BaseOptions(
        baseUrl: ApiConstants.baseUrl,
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 15),
        sendTimeout: const Duration(seconds: 15),
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json',
        },
        followRedirects: true,
        validateStatus: (status) {
          return status != null && status < 500;
        },
      ),
    );

    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final isAuthEndpoint =
              options.path.startsWith('/api/auth/');

          if (!isAuthEndpoint) {
            final hadStoredToken =
                await AuthStorage.instance.hasStoredAccessToken();
            if (hadStoredToken &&
                await AuthStorage.instance.isAccessTokenExpired()) {
              final refreshed = await _refreshSession();
              if (!refreshed) {
                await _expireSession();
                handler.reject(DioException(
                  requestOptions: options,
                  error: 'AUTH_SESSION_EXPIRED',
                ));
                return;
              }
            }
            final token = await AuthStorage.instance.getAccessToken();

            if (token != null) {
              options.headers['Authorization'] =
                  'Bearer $token';
            } else if (hadStoredToken) {
              handler.reject(DioException(
                requestOptions: options,
                error: 'AUTH_SESSION_EXPIRED',
              ));
              return;
            }
          }

          handler.next(options);
        },
        onResponse: (response, handler) async {
          final hadBearer = response.requestOptions.headers
              .keys
              .any((key) => key.toLowerCase() == 'authorization');
          final isAuthEndpoint =
              response.requestOptions.path.startsWith('/api/auth/');
          if (response.statusCode == 401 && hadBearer && !isAuthEndpoint) {
            final request = response.requestOptions;
            if (request.extra['authRetried'] != true &&
                await _refreshSession()) {
              request.extra['authRetried'] = true;
              final token = await AuthStorage.instance.getAccessToken();
              request.headers['Authorization'] = 'Bearer $token';
              try {
                handler.resolve(await dio.fetch(request));
                return;
              } catch (_) {}
            }
            await _expireSession();
          }
          handler.next(response);
        },
      ),
    );

    return dio;
  }
}
