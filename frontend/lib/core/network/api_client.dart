import 'package:dio/dio.dart';

import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

class ApiClient {
  ApiClient._();

  static final Dio dio = _createDio();

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
            final token =
            await AuthStorage.instance.getAccessToken();

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
            await AuthStorage.instance.clearSession();
            await AuthStorage.onSessionExpired?.call();
          }
          handler.next(response);
        },
      ),
    );

    return dio;
  }
}
