import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../features/auth/models/auth_response.dart';
import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

enum _RefreshResult {
  success,
  invalidToken,
  temporaryFailure,
  sessionChanged,
}

class ApiClient {
  ApiClient._();

  static final Dio dio = _createDio();

  @visibleForTesting
  static HttpClientAdapter Function()? refreshAdapterFactory;

  static final AuthStorage _authStorage = AuthStorage.instance;

  static const String sessionGenerationKey = 'authSessionGeneration';
  static const String _authRetriedKey = 'authRetried';

  // Aynı oturuma ait isteklerin tek bir refresh işlemini
  // paylaşmasını sağlar.
  static final Map<int, Future<_RefreshResult>> _refreshOperations = {};

  static Future<_RefreshResult> _refreshSession(
      int generation,
      ) {
    if (!_authStorage.isCurrentGeneration(generation)) {
      return Future.value(_RefreshResult.sessionChanged);
    }

    return _refreshOperations.putIfAbsent(
      generation,
          () => _performRefresh(generation).whenComplete(() {
        _refreshOperations.remove(generation);
      }),
    );
  }

  static Future<_RefreshResult> _performRefresh(
      int generation,
      ) async {
    Dio? refreshDio;

    try {
      _authStorage.ensureCurrentGeneration(generation);

      final refreshToken = await _authStorage.getRefreshToken();

      _authStorage.ensureCurrentGeneration(generation);

      if (refreshToken == null || refreshToken.isEmpty) {
        return _RefreshResult.invalidToken;
      }

      // Refresh için interceptor içermeyen ayrı Dio kullanılır.
      refreshDio = Dio(
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

      final adapter = refreshAdapterFactory?.call();
      if (adapter != null) {
        refreshDio.httpClientAdapter = adapter;
      }

      final response = await refreshDio.post(
        ApiConstants.refreshTokenEndpoint,
        data: {
          'refreshToken': refreshToken,
        },
      );

      // Refresh sırasında hesap değişmiş veya oturum kapanmış olabilir.
      if (!_authStorage.isCurrentGeneration(generation)) {
        return _RefreshResult.sessionChanged;
      }

      // Yalnızca refresh endpoint'inin 401 yanıtı
      // sunucu tarafından reddedilmiş oturumu gösterir.
      if (response.statusCode == 401) {
        return _RefreshResult.invalidToken;
      }

      // 429, 5xx ve beklenmeyen yanıtlarda
      // oturum bilgileri korunur.
      if (response.statusCode != 200 ||
          response.data is! Map<String, dynamic>) {
        return _RefreshResult.temporaryFailure;
      }

      final envelope = response.data as Map<String, dynamic>;
      final data = envelope['data'];

      if (data is! Map<String, dynamic>) {
        return _RefreshResult.temporaryFailure;
      }

      final authResponse = AuthResponse.fromJson(data);

      // Refresh yanıtının aynı kullanıcıya ait olduğunu doğrula.
      final currentUserId = await _authStorage.getUserId();

      _authStorage.ensureCurrentGeneration(generation);

      if (currentUserId == null ||
          authResponse.userId != currentUserId) {
        return _RefreshResult.invalidToken;
      }

      // Boş access token içeren yanıtları kabul etme.
      if (authResponse.accessToken.isEmpty) {
        return _RefreshResult.temporaryFailure;
      }

      // Refresh sonucu yalnızca başladığı oturuma yazılabilir.
      await _authStorage.saveSession(
        authResponse,
        expectedGeneration: generation,
      );

      _authStorage.ensureCurrentGeneration(generation);

      return _RefreshResult.success;
    } on DioException catch (error) {
      if (!_authStorage.isCurrentGeneration(generation)) {
        return _RefreshResult.sessionChanged;
      }

      if (error.response?.statusCode == 401) {
        return _RefreshResult.invalidToken;
      }

      // Bağlantı kesilmesi, timeout, 429 ve 5xx
      // durumlarında oturum korunur.
      return _RefreshResult.temporaryFailure;
    } catch (_) {
      if (!_authStorage.isCurrentGeneration(generation)) {
        return _RefreshResult.sessionChanged;
      }

      return _RefreshResult.temporaryFailure;
    } finally {
      refreshDio?.close();
    }
  }

  // Yalnızca ilgili oturumun süresini sona erdirir.
  static Future<void> _expireSession(int generation) async {
    await _authStorage.expireSession(generation);
  }

  // İstek için standart oturum hatası oluşturur.
  static DioException _authError(
      RequestOptions options,
      String errorCode,
      ) {
    return DioException(
      requestOptions: options,
      type: DioExceptionType.cancel,
      error: errorCode,
    );
  }

  // Auth endpoint'lerinin mevcut davranışını korur.
  static bool _isAuthEndpoint(RequestOptions options) {
    final path = options.uri.path;

    if (!path.startsWith('/api/auth/')) {
      return false;
    }

    // E-posta doğrulama işlemleri JWT gerektirir
    // ve otomatik token yenilemeden yararlanabilir.
    const refreshableAuthEndpoints = <String>{
      '/api/auth/verify-email',
      '/api/auth/verification/resend',
    };

    return !refreshableAuthEndpoints.contains(path);
  }

  static bool _hasAuthorization(RequestOptions options) {
    return options.headers.keys.any(
          (key) => key.toLowerCase() == 'authorization',
    );
  }

  static String? _getAuthorization(RequestOptions options) {
    for (final entry in options.headers.entries) {
      if (entry.key.toLowerCase() == 'authorization') {
        return entry.value?.toString();
      }
    }

    return null;
  }

  static void _setAuthorization(
      RequestOptions options,
      String token,
      ) {
    final headers = <String, dynamic>{};

    for (final entry in options.headers.entries) {
      if (entry.key.toLowerCase() != 'authorization') {
        headers[entry.key] = entry.value;
      }
    }

    headers['Authorization'] = 'Bearer $token';

    options.headers = headers;
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
          // Retry edilen istekler ilk oturum neslini korur.
          final generation =
              options.extra[sessionGenerationKey] as int? ??
                  _authStorage.sessionGeneration;

          options.extra[sessionGenerationKey] = generation;

          try {
            _authStorage.ensureCurrentGeneration(generation);

            final isAuthEndpoint = _isAuthEndpoint(options);

            // Auth endpoint'lerinde açıkça gönderilen
            // Authorization başlığı korunur.
            if (!isAuthEndpoint) {
              final hadStoredToken =
              await _authStorage.hasStoredAccessToken();

              _authStorage.ensureCurrentGeneration(generation);

              final isExpired = hadStoredToken &&
                  await _authStorage.isAccessTokenExpired();

              _authStorage.ensureCurrentGeneration(generation);

              if (isExpired) {
                final refreshResult =
                await _refreshSession(generation);

                if (refreshResult == _RefreshResult.sessionChanged) {
                  handler.reject(
                    _authError(options, 'AUTH_SESSION_CHANGED'),
                  );
                  return;
                }

                if (refreshResult == _RefreshResult.invalidToken) {
                  await _expireSession(generation);

                  handler.reject(
                    _authError(options, 'AUTH_SESSION_EXPIRED'),
                  );
                  return;
                }

                if (refreshResult ==
                    _RefreshResult.temporaryFailure) {
                  handler.reject(
                    _authError(
                      options,
                      'AUTH_REFRESH_TEMPORARILY_UNAVAILABLE',
                    ),
                  );
                  return;
                }
              }

              final token = await _authStorage.getAccessToken();

              _authStorage.ensureCurrentGeneration(generation);

              if (token != null) {
                _setAuthorization(options, token);
              } else if (hadStoredToken ||
                  _hasAuthorization(options)) {
                handler.reject(
                  _authError(options, 'AUTH_SESSION_EXPIRED'),
                );
                return;
              }
            }

            // İstek gönderilmeden önce son oturum kontrolü.
            _authStorage.ensureCurrentGeneration(generation);

            handler.next(options);
          } catch (error) {
            handler.reject(
              _authError(
                options,
                !_authStorage.isCurrentGeneration(generation)
                    ? 'AUTH_SESSION_CHANGED'
                    : 'AUTH_REQUEST_FAILED',
              ),
            );
          }
        },

        onResponse: (response, handler) async {
          final request = response.requestOptions;

          final generation =
          request.extra[sessionGenerationKey] as int?;

          if (generation == null) {
            handler.next(response);
            return;
          }

          try {
            // Eski oturumdan gelen yanıtların uygulama
            // state'ini değiştirmesini engelle.
            _authStorage.ensureCurrentGeneration(generation);

            final hadBearer = _hasAuthorization(request);
            final isAuthEndpoint = _isAuthEndpoint(request);

            if (response.statusCode == 401 &&
                hadBearer &&
                !isAuthEndpoint &&
                request.extra[_authRetriedKey] != true) {
              final sentAuthorization =
              _getAuthorization(request);

              final currentToken =
              await _authStorage.getAccessToken();

              _authStorage.ensureCurrentGeneration(generation);

              _RefreshResult refreshResult;

              // Başka bir istek tokenı zaten yenilemişse
              // gereksiz refresh işlemi başlatma.
              if (currentToken != null &&
                  sentAuthorization != 'Bearer $currentToken') {
                refreshResult = _RefreshResult.success;
              } else {
                refreshResult =
                await _refreshSession(generation);
              }

              if (refreshResult == _RefreshResult.sessionChanged) {
                handler.reject(
                  _authError(request, 'AUTH_SESSION_CHANGED'),
                );
                return;
              }

              if (refreshResult == _RefreshResult.success) {
                _authStorage.ensureCurrentGeneration(generation);

                final token = await _authStorage.getAccessToken();

                _authStorage.ensureCurrentGeneration(generation);

                if (token == null || token.isEmpty) {
                  handler.reject(
                    _authError(request, 'AUTH_SESSION_EXPIRED'),
                  );
                  return;
                }

                // İstek yalnızca bir kez tekrar gönderilebilir.
                request.extra[_authRetriedKey] = true;

                // İlk isteğin oturum nesli korunur.
                request.extra[sessionGenerationKey] = generation;

                _setAuthorization(request, token);

                final retryResponse = await dio.fetch(request);

                _authStorage.ensureCurrentGeneration(generation);

                handler.resolve(retryResponse);
                return;
              }

              if (refreshResult == _RefreshResult.invalidToken) {
                await _expireSession(generation);

                handler.reject(
                  _authError(request, 'AUTH_SESSION_EXPIRED'),
                );
                return;
              }

              if (refreshResult ==
                  _RefreshResult.temporaryFailure) {
                handler.reject(
                  _authError(
                    request,
                    'AUTH_REFRESH_TEMPORARILY_UNAVAILABLE',
                  ),
                );
                return;
              }
            }

            _authStorage.ensureCurrentGeneration(generation);

            handler.next(response);
          } on DioException catch (error) {
            handler.reject(error);
          } catch (_) {
            handler.reject(
              _authError(
                request,
                !_authStorage.isCurrentGeneration(generation)
                    ? 'AUTH_SESSION_CHANGED'
                    : 'AUTH_RESPONSE_FAILED',
              ),
            );
          }
        },

        onError: (error, handler) {
          final generation = error
              .requestOptions.extra[sessionGenerationKey] as int?;

          if (error.type == DioExceptionType.cancel &&
              error.error == 'AUTH_SESSION_EXPIRED') {
            handler.next(error);
            return;
          }

          if (generation != null &&
              !_authStorage.isCurrentGeneration(generation)) {
            handler.reject(
              _authError(
                error.requestOptions,
                'AUTH_SESSION_CHANGED',
              ),
            );
            return;
          }

          handler.next(error);
        },
      ),
    );

    return dio;
  }
}
