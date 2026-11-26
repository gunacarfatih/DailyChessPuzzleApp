import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:daily_chess_puzzle/core/constants/api_constants.dart';
import 'package:daily_chess_puzzle/core/network/api_client.dart';
import 'package:daily_chess_puzzle/core/storage/auth_storage.dart';
import 'package:daily_chess_puzzle/features/auth/models/auth_response.dart';
import 'package:daily_chess_puzzle/features/auth/services/auth_api_service.dart';
import 'package:daily_chess_puzzle/providers/auth_provider.dart';

class _Adapter implements HttpClientAdapter {
  _Adapter(this.respond);

  final Future<ResponseBody> Function(RequestOptions) respond;

  @override
  Future<ResponseBody> fetch(
      RequestOptions options,
      Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture,
      ) {
    return respond(options);
  }

  @override
  void close({bool force = false}) {}
}

ResponseBody _reply(
    int status, [
      Map<String, dynamic> data = const {},
    ]) {
  return ResponseBody.fromString(
    jsonEncode(data),
    status,
    headers: {
      Headers.contentTypeHeader: [
        Headers.jsonContentType,
      ],
    },
  );
}

AuthResponse _account(int id, String token) {
  return AuthResponse(
    userId: id,
    username: 'user$id',
    email: 'user$id@example.test',
    accessToken: token,
    refreshToken: 'refresh-$token',
    expiresAtUtc: DateTime.now()
        .toUtc()
        .add(const Duration(hours: 1)),
  );
}

Map<String, dynamic> _accountJson(
    int id,
    String token,
    ) {
  return {
    'userId': id,
    'username': 'user$id',
    'email': 'user$id@example.test',
    'accessToken': token,
    'refreshToken': 'refresh-$token',
    'expiresAtUtc':
    _account(id, token).expiresAtUtc.toIso8601String(),
    'hasPassword': true,
  };
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  final storage = AuthStorage.instance;

  late AuthProvider provider;
  late HttpClientAdapter originalAdapter;

  Future<void> signIn(
      int id,
      String token,
      ) async {
    final generation = await storage.beginSessionChange();

    await storage.saveSession(
      _account(id, token),
      expectedGeneration: generation,
    );
  }

  void logoutAdapter(
      Future<ResponseBody> Function(RequestOptions) respond,
      ) {
    AuthApiService.logoutAdapterFactory =
        () => _Adapter(respond);
  }

  Future<void> expectLoggedOut() async {
    expect(await storage.getAccessToken(), isNull);
    expect(await storage.getRefreshToken(), isNull);
    expect(await storage.getUserId(), isNull);

    expect(provider.isLoggedIn, isFalse);
    expect(provider.userId, isNull);
    expect(provider.username, isNull);
    expect(provider.email, isNull);
  }

  setUp(() async {
    FlutterSecureStorage.setMockInitialValues({});

    originalAdapter = ApiClient.dio.httpClientAdapter;

    AuthStorage.onSessionExpired = null;
    AuthApiService.logoutAdapterFactory = null;
    ApiClient.refreshAdapterFactory = null;

    await signIn(1, 'old');

    provider = AuthProvider();

    await provider.initialize();

    expect(provider.isLoggedIn, isTrue);
    expect(provider.userId, 1);
  });

  tearDown(() async {
    provider.dispose();

    AuthApiService.logoutAdapterFactory = null;
    ApiClient.refreshAdapterFactory = null;

    ApiClient.dio.httpClientAdapter = originalAdapter;

    AuthStorage.onSessionExpired = null;

    await storage.beginSessionChange();
  });

  test(
    'logout sends correct refresh token and clears session',
        () async {
      var requests = 0;

      logoutAdapter((options) async {
        requests++;

        expect(
          options.path,
          ApiConstants.logoutEndpoint,
        );

        expect(options.method, 'POST');

        expect(
          options.data,
          {'refreshToken': 'refresh-old'},
        );

        expect(
          options.headers.keys.any(
                (key) => key.toLowerCase() == 'authorization',
          ),
          isFalse,
        );

        return _reply(200);
      });

      await provider.logout();

      expect(requests, 1);
      expect(provider.isLoading, isFalse);

      await expectLoggedOut();
    },
  );

  test(
    'provider and storage are logged out before server responds',
        () async {
      final entered = Completer<void>();
      final release = Completer<void>();

      logoutAdapter((_) async {
        entered.complete();

        await release.future;

        return _reply(200);
      });

      final pending = provider.logout();

      try {
        await entered.future.timeout(
          const Duration(seconds: 5),
        );

        await expectLoggedOut();
      } finally {
        release.complete();

        await pending;
      }
    },
  );

  test(
    'refresh cannot restore session after real logout',
        () async {
      final entered = Completer<void>();
      final release = Completer<void>();

      ApiClient.dio.httpClientAdapter =
          _Adapter((_) async => _reply(401));

      ApiClient.refreshAdapterFactory =
          () => _Adapter((_) async {
        entered.complete();

        await release.future;

        return _reply(200, {
          'data': _accountJson(1, 'refreshed'),
        });
      });

      logoutAdapter((_) async => _reply(200));

      final pending =
      ApiClient.dio.get('/test/private');

      final check = expectLater(
        pending,
        throwsA(
          isA<DioException>().having(
                (error) => error.error,
            'error',
            'AUTH_SESSION_CHANGED',
          ),
        ),
      );

      try {
        await entered.future.timeout(
          const Duration(seconds: 5),
        );

        await provider.logout();
      } finally {
        release.complete();

        await check;
      }

      await expectLoggedOut();
    },
  );

  for (final status in [200, 500]) {
    test(
      'late logout $status response cannot clear new account',
          () async {
        final entered = Completer<void>();
        final release = Completer<void>();

        logoutAdapter((options) async {
          expect(
            options.data,
            {'refreshToken': 'refresh-old'},
          );

          entered.complete();

          await release.future;

          return _reply(status);
        });

        ApiClient.dio.httpClientAdapter =
            _Adapter((options) async {
              expect(
                options.path,
                ApiConstants.loginEndpoint,
              );

              return _reply(200, {
                'success': true,
                'data': _accountJson(2, 'other'),
              });
            });

        final pending = provider.logout();

        final Future<void> completion;

        if (status == 500) {
          completion = expectLater(
            pending,
            throwsA(isA<DioException>()),
          );
        } else {
          completion = pending;
        }

        try {
          await entered.future.timeout(
            const Duration(seconds: 5),
          );

          final login = await provider.login(
            email: 'user2@example.test',
            password: 'test-password',
          );

          expect(login.success, isTrue);
          expect(provider.isLoggedIn, isTrue);
          expect(provider.userId, 2);
        } finally {
          release.complete();

          await completion;
        }

        expect(provider.isLoggedIn, isTrue);
        expect(provider.userId, 2);
        expect(provider.isLoading, isFalse);

        expect(
          await storage.getAccessToken(),
          'other',
        );

        expect(
          await storage.getRefreshToken(),
          'refresh-other',
        );

        expect(await storage.getUserId(), 2);
      },
    );
  }

  test(
    'network failure leaves provider and storage logged out',
        () async {
      logoutAdapter((options) async {
        throw DioException(
          requestOptions: options,
          type: DioExceptionType.connectionError,
        );
      });

      await expectLater(
        provider.logout(),
        throwsA(isA<DioException>()),
      );

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );

  test(
    'logout 500 leaves provider and storage logged out',
        () async {
      logoutAdapter((_) async => _reply(500));

      await expectLater(
        provider.logout(),
        throwsA(isA<DioException>()),
      );

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );

  test(
    'logout 401 is treated as successful logout',
        () async {
      logoutAdapter((_) async => _reply(401));

      await provider.logout();

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );

  test(
    'concurrent logout calls complete without session errors',
        () async {
      var requests = 0;

      logoutAdapter((_) async {
        requests++;

        return _reply(200);
      });

      final first = provider.logout();
      final second = provider.logout();

      await Future.wait([
        first,
        second,
      ]);

      expect(requests, 1);

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );
  test(
    'repeated logout while server response is pending',
        () async {
      final entered = Completer<void>();
      final release = Completer<void>();

      var requests = 0;

      logoutAdapter((_) async {
        requests++;

        entered.complete();

        await release.future;

        return _reply(200);
      });

      final first = provider.logout();

      try {
        await entered.future.timeout(
          const Duration(seconds: 5),
        );

        await expectLoggedOut();

        final generation = storage.sessionGeneration;

        final second = provider.logout();

        release.complete();

        await Future.wait([first, second]);

        expect(requests, 1);

        expect(
          storage.sessionGeneration,
          generation,
          reason: 'Repeated logout must not advance generation',
        );

        await expectLoggedOut();

        expect(provider.isLoading, isFalse);
      } finally {
        if (!release.isCompleted) {
          release.complete();
        }

        await first;
      }
    },
  );

  test(
    'new account can logout while old logout is pending',
        () async {
      final entered = Completer<void>();
      final release = Completer<void>();

      final sentTokens = <String>[];

      logoutAdapter((options) async {
        final data = options.data as Map;

        final token = data['refreshToken'] as String;

        sentTokens.add(token);

        if (token == 'refresh-old') {
          entered.complete();

          await release.future;
        }

        return _reply(200);
      });

      ApiClient.dio.httpClientAdapter =
          _Adapter((options) async {
            expect(
              options.path,
              ApiConstants.loginEndpoint,
            );

            return _reply(200, {
              'success': true,
              'data': _accountJson(2, 'other'),
            });
          });

      final firstLogout = provider.logout();

      try {
        await entered.future.timeout(
          const Duration(seconds: 5),
        );

        final login = await provider.login(
          email: 'user2@example.test',
          password: 'test-password',
        );

        expect(login.success, isTrue);
        expect(provider.isLoggedIn, isTrue);
        expect(provider.userId, 2);

        expect(
          await storage.getRefreshToken(),
          'refresh-other',
        );

        await provider.logout().timeout(
          const Duration(seconds: 5),
        );

        await expectLoggedOut();

        expect(sentTokens, [
          'refresh-old',
          'refresh-other',
        ]);
      } finally {
        release.complete();

        await firstLogout;
      }

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );

  test(
    'logout without an active session is a no-op',
        () async {
      var requests = 0;

      logoutAdapter((_) async {
        requests++;

        return _reply(200);
      });

      await provider.logout();

      await expectLoggedOut();

      expect(requests, 1);

      final generation = storage.sessionGeneration;

      await provider.logout();

      expect(requests, 1);

      expect(
        storage.sessionGeneration,
        generation,
        reason: 'Logout without a session must not change generation',
      );

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );

  test(
    'disposing provider during pending logout is safe',
        () async {
      final entered = Completer<void>();
      final release = Completer<void>();

      logoutAdapter((_) async {
        entered.complete();

        await release.future;

        return _reply(200);
      });

      final pending = provider.logout();

      try {
        await entered.future.timeout(
          const Duration(seconds: 5),
        );

        await expectLoggedOut();

        provider.dispose();

        // Ortak tearDown işleminin aynı Provider'ı
        // ikinci kez dispose etmesini önler.
        provider = AuthProvider();

        await provider.initialize();

        expect(provider.isLoggedIn, isFalse);
      } finally {
        release.complete();

        await pending;
      }

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );

  test(
    'logout cancels pending login before tokens are stored',
        () async {
      final entered = Completer<void>();
      final release = Completer<void>();

      logoutAdapter((_) async => _reply(200));

      ApiClient.dio.httpClientAdapter =
          _Adapter((options) async {
            expect(
              options.path,
              ApiConstants.loginEndpoint,
            );

            entered.complete();

            await release.future;

            return _reply(200, {
              'success': true,
              'data': _accountJson(2, 'other'),
            });
          });

      final pending = provider.login(
        email: 'user2@example.test',
        password: 'test-password',
      );

      final check = expectLater(
        pending,
        throwsA(
          anyOf(
            isA<StateError>(),
            isA<DioException>().having(
                  (error) => error.error,
              'error',
              'AUTH_SESSION_CHANGED',
            ),
          ),
        ),
      );

      try {
        await entered.future.timeout(
          const Duration(seconds: 5),
        );

        await provider.logout();

        await expectLoggedOut();
      } finally {
        release.complete();

        await check;
      }

      await expectLoggedOut();

      expect(provider.isLoading, isFalse);
    },
  );
}