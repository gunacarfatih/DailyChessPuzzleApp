import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:daily_chess_puzzle/core/storage/auth_storage.dart';
import 'package:daily_chess_puzzle/features/auth/models/auth_response.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  final storage = AuthStorage.instance;
  setUp(() async {
    FlutterSecureStorage.setMockInitialValues({});
    AuthStorage.onSessionExpired = null;
    await storage.beginSessionChange();
  });
  tearDown(() async {
    AuthStorage.onSessionExpired = null;
    await storage.beginSessionChange();
  });
  test('stale writes and expiry cannot affect new account', () async {
    final old = storage.sessionGeneration;
    final current = await storage.beginSessionChange();
    final response = AuthResponse(userId: 2, username: 'new',
        email: 'new@example.test', accessToken: 'new-token',
        refreshToken: 'new-refresh', expiresAtUtc: DateTime.now().toUtc());
    await storage.saveSession(response, expectedGeneration: current);
    await expectLater(storage.saveSession(response, expectedGeneration: old), throwsStateError);
    await storage.expireSession(old);
    expect(await storage.getAccessToken(), 'new-token');
    expect(await storage.getUserId(), 2);
  });
}