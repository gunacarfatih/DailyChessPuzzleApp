import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:daily_chess_puzzle/core/network/api_client.dart';
import 'package:daily_chess_puzzle/core/storage/auth_storage.dart';
import 'package:daily_chess_puzzle/features/auth/models/auth_response.dart';
import 'package:daily_chess_puzzle/providers/auth_provider.dart';

class _Adapter implements HttpClientAdapter {
  _Adapter(this.respond);
  final Future<ResponseBody> Function(RequestOptions) respond;
  @override
  Future<ResponseBody> fetch(RequestOptions options,
      Stream<Uint8List>? requestStream, Future<void>? cancelFuture) =>
      respond(options);
  @override
  void close({bool force = false}) {}
}

ResponseBody reply(int status, [Map<String, dynamic> data = const {}]) =>
    ResponseBody.fromString(jsonEncode(data), status,
        headers: {Headers.contentTypeHeader: [Headers.jsonContentType]});

AuthResponse account(int id, String token) => AuthResponse(
    userId: id, username: 'user$id', email: 'user$id@example.test',
    accessToken: token, refreshToken: 'refresh-$token',
    expiresAtUtc: DateTime.now().toUtc().add(const Duration(hours: 1)));

ResponseBody refreshed() => reply(200, {'data': {
  'userId': 1, 'username': 'user1', 'email': 'user1@example.test',
  'accessToken': 'new', 'refreshToken': 'refresh-new',
  'expiresAtUtc': account(1, 'new').expiresAtUtc.toIso8601String(),
}});

Matcher authError(String code) => isA<DioException>()
    .having((e) => e.error, 'error', code);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  final storage = AuthStorage.instance;
  late HttpClientAdapter original;
  late int refreshes;
  Future<void> signIn(int id, String token) async {
    final generation = await storage.beginSessionChange();
    await storage.saveSession(account(id, token), expectedGeneration: generation);
  }
  void adapters(Future<ResponseBody> Function(RequestOptions) api,
      Future<ResponseBody> Function(RequestOptions) refresh) {
    ApiClient.dio.httpClientAdapter = _Adapter(api);
    ApiClient.refreshAdapterFactory = () => _Adapter((options) {
      refreshes++;
      return refresh(options);
    });
  }
  Future<ResponseBody> rejectOld(RequestOptions options) async =>
      reply(options.headers['Authorization'] == 'Bearer new' ? 200 : 401);
  setUp(() async {
    FlutterSecureStorage.setMockInitialValues({});
    original = ApiClient.dio.httpClientAdapter;
    refreshes = 0;
    AuthStorage.onSessionExpired = null;
    await signIn(1, 'old');
  });
  tearDown(() async {
    ApiClient.refreshAdapterFactory = null;
    ApiClient.dio.httpClientAdapter = original;
    AuthStorage.onSessionExpired = null;
    await storage.beginSessionChange();
  });
  test('concurrent 401 requests share one refresh', () async {
    final entered = Completer<void>();
    final release = Completer<void>();
    final both = Completer<void>();
    var oldRequests = 0;
    adapters((o) async {
      if (o.headers['Authorization'] == 'Bearer old') {
        if (++oldRequests == 2) both.complete();
        return reply(401);
      }
      return reply(200);
    }, (_) async { entered.complete(); await release.future; return refreshed(); });
    final a = ApiClient.dio.get('/test/a');
    final b = ApiClient.dio.get('/test/b');
    await Future.wait([entered.future, both.future]);
    release.complete();
    expect((await Future.wait([a,b])).map((r) => r.statusCode), everyElement(200));
    expect(refreshes, 1);
  });
  test('late 401 uses already refreshed token', () async {
    final entered = Completer<void>();
    final release = Completer<void>();
    adapters((o) async {
      if (o.path == '/test/late' && o.headers['Authorization'] == 'Bearer old') {
        entered.complete(); await release.future; return reply(401);
      }
      return rejectOld(o);
    }, (_) async => refreshed());
    final late = ApiClient.dio.get('/test/late');
    await entered.future;
    expect((await ApiClient.dio.get('/test/first')).statusCode, 200);
    release.complete();
    expect((await late).statusCode, 200);
    expect(refreshes, 1);
  });
  for (final switchAccount in [false, true]) {
    test('refresh cannot overwrite ${switchAccount ? "new account" : "cleared session"}', () async {
      final entered = Completer<void>();
      final release = Completer<void>();
      adapters(rejectOld, (_) async { entered.complete(); await release.future; return refreshed(); });
      final pending = ApiClient.dio.get('/test/private');
      final check = expectLater(pending, throwsA(authError('AUTH_SESSION_CHANGED')));
      await entered.future;
      if (switchAccount) {
        await signIn(2, 'other');
      } else {
        await storage.clearSession(expectedGeneration: storage.sessionGeneration);
      }
      release.complete();
      await check;
      expect(await storage.getAccessToken(), switchAccount ? 'other' : null);
      expect(await storage.getRefreshToken(), switchAccount ? 'refresh-other' : null);
      expect(await storage.getUserId(), switchAccount ? 2 : null);
    });
  }
  test('old successful response is rejected after account switch', () async {
    final entered = Completer<void>();
    final release = Completer<void>();
    adapters((_) async { entered.complete(); await release.future; return reply(200); }, (_) async => refreshed());
    final pending = ApiClient.dio.get('/test/private');
    final check = expectLater(pending, throwsA(authError('AUTH_SESSION_CHANGED')));
    await entered.future;
    await signIn(2, 'other');
    release.complete(); await check;
    expect(await storage.getAccessToken(), 'other');
    expect(refreshes, 0);
  });
  for (final networkError in [false, true]) {
    test('refresh ${networkError ? "network failure" : "429"} preserves session', () async {
      adapters(rejectOld, (o) async {
        if (networkError) { throw DioException(requestOptions: o, type: DioExceptionType.connectionError); }
        return reply(429);
      });
      await expectLater(ApiClient.dio.get('/test/private'), throwsA(authError('AUTH_REFRESH_TEMPORARILY_UNAVAILABLE')));
      expect(await storage.getAccessToken(), 'old');
      expect(await storage.getRefreshToken(), 'refresh-old');
      expect(refreshes, 1);
    });
  }
  test('refresh 401 clears storage and notifies provider', () async {
    final provider = AuthProvider();
    addTearDown(provider.dispose);
    await provider.initialize();
    expect(provider.isLoggedIn, isTrue);
    adapters(rejectOld, (_) async => reply(401));
    await expectLater(ApiClient.dio.get('/test/private'), throwsA(authError('AUTH_SESSION_EXPIRED')));
    expect(await storage.getAccessToken(), isNull);
    expect(await storage.getRefreshToken(), isNull);
    expect(provider.isLoggedIn, isFalse);
    expect(provider.userId, isNull);
  });
  test('request retries at most once', () async {
    var requests = 0;
    adapters((_) async { requests++; return reply(401); }, (_) async => refreshed());
    expect((await ApiClient.dio.get('/test/private')).statusCode, 401);
    expect(requests, 2);
    expect(refreshes, 1);
  });
}
