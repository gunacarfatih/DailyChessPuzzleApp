
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../features/auth/models/auth_response.dart';

class AuthStorage {
  AuthStorage._();

  static final AuthStorage instance = AuthStorage._();

  // Oturum sona erdiğinde Provider'a bildirim gönderilir.
  static Future<void> Function(int generation)? onSessionExpired;

  // Her oturum değişikliğinde artırılan nesil numarası.
  int _sessionGeneration = 0;

  int get sessionGeneration => _sessionGeneration;

  // İşlemin hâlâ aynı oturuma ait olup olmadığını kontrol eder.
  bool isCurrentGeneration(int generation) {
    return _sessionGeneration == generation;
  }

  // Önceki oturuma ait asenkron işlemleri geçersiz kılar.
  int advanceSessionGeneration() {
    return ++_sessionGeneration;
  }

  // Eski oturuma ait işlemlerin devam etmesini engeller.
  void ensureCurrentGeneration(int generation) {
    if (!isCurrentGeneration(generation)) {
      throw StateError('AUTH_SESSION_CHANGED');
    }
  }

  // Güvenli depolama işlemlerinin sırasını korur.
  Future<void> _operationQueue = Future<void>.value();

  Future<T> _enqueue<T>(Future<T> Function() operation) {
    final result = _operationQueue.then<T>(
          (_) => operation(),
    );

    _operationQueue = result.then<void>(
          (_) {},
      onError: (Object error, StackTrace stackTrace) {},
    );

    return result;
  }

  static const FlutterSecureStorage _storage =
  FlutterSecureStorage();

  static const String _tokenKey = 'auth_access_token';
  static const String _refreshTokenKey = 'auth_refresh_token';
  static const String _userIdKey = 'auth_user_id';
  static const String _usernameKey = 'auth_username';
  static const String _emailKey = 'auth_email';
  static const String _firstNameKey = 'auth_first_name';
  static const String _lastNameKey = 'auth_last_name';
  static const String _countryCodeKey = 'auth_country_code';
  static const String _hasPasswordKey = 'auth_has_password';
  static const String _emailVerifiedKey = 'auth_email_verified';
  static const String _expiresAtKey = 'auth_expires_at';

  // Yeni bir oturum başlatır ve önceki oturumu geçersiz kılar.
  Future<int> beginSessionChange() async {
    final generation = advanceSessionGeneration();

    await _enqueue(() async {
      ensureCurrentGeneration(generation);
      await _deleteSessionData();
    });

    ensureCurrentGeneration(generation);

    return generation;
  }

// Oturum bilgilerini yalnızca ilgili oturum hâlâ geçerliyse kaydeder.
  Future<void> saveSession(
      AuthResponse response, {
        required int expectedGeneration,
      }) {
    return _enqueue(() async {
      ensureCurrentGeneration(expectedGeneration);

      final storedUserId = await _storage.read(
        key: _userIdKey,
      );

      ensureCurrentGeneration(expectedGeneration);

      final storedEmail = await _storage.read(
        key: _emailKey,
      );

      ensureCurrentGeneration(expectedGeneration);

      final storedEmailVerified = await _storage.read(
        key: _emailVerifiedKey,
      );

      ensureCurrentGeneration(expectedGeneration);

      // Mevcut doğrulama bilgisinin aynı kullanıcıya ve
      // aynı e-posta adresine ait olduğunu kontrol et.
      final isSameAccount =
          storedUserId == response.userId.toString() &&
              storedEmail?.trim().toLowerCase() ==
                  response.email.trim().toLowerCase();

      // Eski refresh yanıtının başarılı doğrulamayı
      // geri almasını engelle.
      final emailVerified =
          response.emailVerified ||
              (isSameAccount && storedEmailVerified == 'true');

      final values = <String, String>{
        _tokenKey: response.accessToken,
        _userIdKey: response.userId.toString(),
        _usernameKey: response.username,
        _emailKey: response.email,
        _firstNameKey: response.firstName ?? '',
        _lastNameKey: response.lastName ?? '',
        _countryCodeKey: response.countryCode ?? '',
        _hasPasswordKey: response.hasPassword.toString(),
        _emailVerifiedKey: emailVerified.toString(),
        _expiresAtKey: response.expiresAtUtc.toIso8601String(),
      };

      final refreshToken = response.refreshToken;

      if (refreshToken != null && refreshToken.isNotEmpty) {
        values[_refreshTokenKey] = refreshToken;
      }

      try {
        for (final entry in values.entries) {
          ensureCurrentGeneration(expectedGeneration);

          await _storage.write(
            key: entry.key,
            value: entry.value,
          );
        }

        ensureCurrentGeneration(expectedGeneration);
      } catch (_) {
        // Kısmen yazılmış oturum bilgilerinin
        // kullanılmasını engeller.
        await _deleteSessionData();
        rethrow;
      }
    });
  }


  // Depolama okumalarını da işlem kuyruğundan geçirir.
  Future<String?> _read(String key) {
    final generation = sessionGeneration;

    return _enqueue(() async {
      ensureCurrentGeneration(generation);

      final value = await _storage.read(key: key);

      ensureCurrentGeneration(generation);

      return value;
    });
  }

  Future<String?> getAccessToken() async {
    final token = await _read(_tokenKey);

    return token == null || token.isEmpty ? null : token;
  }

  Future<String?> getRefreshToken() async {
    final token = await _read(_refreshTokenKey);

    return token == null || token.isEmpty ? null : token;
  }

  Future<bool> isAccessTokenExpired() async {
    final expiresAt = await _read(_expiresAtKey);

    if (expiresAt == null) return false;

    final expiryDate = DateTime.tryParse(expiresAt);

    return expiryDate == null ||
        !expiryDate.isAfter(DateTime.now().toUtc());
  }

  Future<bool> hasStoredAccessToken() async {
    return await getAccessToken() != null;
  }

  Future<bool> isLoggedIn() async {
    return await getAccessToken() != null;
  }

  Future<int?> getUserId() async {
    final value = await _read(_userIdKey);

    return int.tryParse(value ?? '');
  }

  Future<String?> getUsername() {
    return _read(_usernameKey);
  }

  Future<String?> getEmail() {
    return _read(_emailKey);
  }

  Future<String?> getFirstName() {
    return _read(_firstNameKey);
  }

  Future<String?> getLastName() {
    return _read(_lastNameKey);
  }

  Future<String?> getCountryCode() {
    return _read(_countryCodeKey);
  }

  // Ülke bilgisi yalnızca ilgili oturum için güncellenir.
  Future<void> updateCountryCode(
      String? countryCode, {
        required int expectedGeneration,
      }) {
    return _enqueue(() async {
      ensureCurrentGeneration(expectedGeneration);

      if (countryCode == null || countryCode.isEmpty) {
        await _storage.delete(key: _countryCodeKey);
      } else {
        await _storage.write(
          key: _countryCodeKey,
          value: countryCode,
        );
      }

      ensureCurrentGeneration(expectedGeneration);
    });
  }

  Future<bool> getHasPassword() async {
    final value = await _read(_hasPasswordKey);

    return value == 'true';
  }

  // Şifre durumu yalnızca ilgili oturum için güncellenir.
  Future<void> updateHasPassword(
      bool hasPassword, {
        required int expectedGeneration,
      }) {
    return _enqueue(() async {
      ensureCurrentGeneration(expectedGeneration);

      await _storage.write(
        key: _hasPasswordKey,
        value: hasPassword.toString(),
      );

      ensureCurrentGeneration(expectedGeneration);
    });
  }


// E-posta doğrulama durumunu güvenli depolamadan okur.
  Future<bool> getEmailVerified() async {
    final value = await _read(_emailVerifiedKey);

    return value == 'true';
  }

// E-posta doğrulama durumu yalnızca ilgili oturum için güncellenir.
  Future<void> updateEmailVerified(
      bool emailVerified, {
        required int expectedGeneration,
      }) {
    return _enqueue(() async {
      ensureCurrentGeneration(expectedGeneration);

      await _storage.write(
        key: _emailVerifiedKey,
        value: emailVerified.toString(),
      );

      ensureCurrentGeneration(expectedGeneration);
    });
  }


  // Oturumu geçersiz kılar ve güvenli depolamayı temizler.
  Future<void> clearSession({
    required int expectedGeneration,
  }) async {
    ensureCurrentGeneration(expectedGeneration);

    final generation = advanceSessionGeneration();

    await _enqueue(() async {
      if (!isCurrentGeneration(generation)) return;

      await _deleteSessionData();
    });
  }

  // Yalnızca belirtilen oturumun süresini sona erdirir.
  Future<void> expireSession(int expectedGeneration) async {
    if (!isCurrentGeneration(expectedGeneration)) return;

    final generation = advanceSessionGeneration();

    await _enqueue(() async {
      if (!isCurrentGeneration(generation)) return;

      await _deleteSessionData();
    });

    if (!isCurrentGeneration(generation)) return;

    await onSessionExpired?.call(generation);
  }

  // Depolama kuyruğu içerisinden çağrılmalıdır.
  Future<void> _deleteSessionData() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _refreshTokenKey);
    await _storage.delete(key: _userIdKey);
    await _storage.delete(key: _usernameKey);
    await _storage.delete(key: _emailKey);
    await _storage.delete(key: _firstNameKey);
    await _storage.delete(key: _lastNameKey);
    await _storage.delete(key: _countryCodeKey);
    await _storage.delete(key: _hasPasswordKey);
    await _storage.delete(key: _emailVerifiedKey);
    await _storage.delete(key: _expiresAtKey);
  }

}
