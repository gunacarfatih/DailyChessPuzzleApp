
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
            final token =
            await AuthStorage.instance.getAccessToken();

            if (token != null) {
              options.headers['Authorization'] =
              'Bearer $token';
            }
          }

          handler.next(options);
        },
      ),
    );

    return dio;
  }
}
