import 'package:dio/dio.dart';

import '../../features/auth/models/auth_response.dart';
import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

enum _RefreshResult {
  success,
  invalidToken,
  temporaryFailure,
}

class ApiClient {
  ApiClient._();

  static final Dio dio = _createDio();
  static Future<_RefreshResult>? _refreshing;

  static Future<_RefreshResult> _refreshSession() {
    return _refreshing ??= _performRefresh().whenComplete(
          () => _refreshing = null,
    );
  }

  static Future<_RefreshResult> _performRefresh() async {
    final refreshToken =
    await AuthStorage.instance.getRefreshToken();

    if (refreshToken == null || refreshToken.isEmpty) {
      return _RefreshResult.invalidToken;
    }

    try {
      final response = await dio.post(
        ApiConstants.refreshTokenEndpoint,
        data: {
          'refreshToken': refreshToken,
        },
      );

      if (response.statusCode == 401) {
        return _RefreshResult.invalidToken;
      }

      if (response.statusCode != 200 ||
          response.data is! Map<String, dynamic>) {
        return _RefreshResult.temporaryFailure;
      }

      final envelope =
      response.data as Map<String, dynamic>;

      final data = envelope['data'];

      if (data is! Map<String, dynamic>) {
        return _RefreshResult.temporaryFailure;
      }

      await AuthStorage.instance.saveSession(
        AuthResponse.fromJson(data),
      );

      return _RefreshResult.success;
    } on DioException {
      // Bağlantı kesilmesi, timeout ve 5xx gibi hatalarda
      // oturum bilgileri korunur.
      return _RefreshResult.temporaryFailure;
    } catch (_) {
      // Beklenmeyen veya bozuk sunucu cevabı da kullanıcının
      // refresh token'ını silmek için yeterli kanıt değildir.
      return _RefreshResult.temporaryFailure;
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
              final refreshResult = await _refreshSession();

              if (refreshResult == _RefreshResult.invalidToken) {
                await _expireSession();

                handler.reject(DioException(
                  requestOptions: options,
                  error: 'AUTH_SESSION_EXPIRED',
                ));
                return;
              }

              if (refreshResult == _RefreshResult.temporaryFailure) {
                handler.reject(DioException(
                  requestOptions: options,
                  error: 'AUTH_REFRESH_TEMPORARILY_UNAVAILABLE',
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
          if (response.statusCode == 401 &&
              hadBearer &&
              !isAuthEndpoint) {
            final request = response.requestOptions;

            if (request.extra['authRetried'] != true) {
              final refreshResult = await _refreshSession();

              if (refreshResult == _RefreshResult.success) {
                request.extra['authRetried'] = true;

                final token =
                await AuthStorage.instance.getAccessToken();

                request.headers['Authorization'] =
                'Bearer $token';

                try {
                  handler.resolve(
                    await dio.fetch(request),
                  );
                  return;
                } on DioException catch (error) {
                  // Refresh başarılı olduğu için oturumu silme.
                  // Tekrarlanan isteğin kendi ağ hatasını döndür.
                  handler.reject(error);
                  return;
                }
              }

              if (refreshResult ==
                  _RefreshResult.invalidToken) {
                await _expireSession();
              }
            }
          }
          handler.next(response);
        },
      ),
    );

    return dio;
  }
}
