
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../features/auth/models/auth_response.dart';

class AuthStorage {
  AuthStorage._();

  static final AuthStorage instance = AuthStorage._();

  static const FlutterSecureStorage _storage =
  FlutterSecureStorage();

  static const String _tokenKey = 'auth_access_token';
  static const String _userIdKey = 'auth_user_id';
  static const String _usernameKey = 'auth_username';
  static const String _emailKey = 'auth_email';
  static const String _countryCodeKey = 'auth_country_code';
  static const String _expiresAtKey = 'auth_expires_at';

  Future<void> saveSession(AuthResponse response) async {
    await _storage.write(
      key: _tokenKey,
      value: response.accessToken,
    );

    await _storage.write(
      key: _userIdKey,
      value: response.userId.toString(),
    );

    await _storage.write(
      key: _usernameKey,
      value: response.username,
    );

    await _storage.write(
      key: _emailKey,
      value: response.email,
    );

    await _storage.write(
      key: _countryCodeKey,
      value: response.countryCode ?? '',
    );

    await _storage.write(
      key: _expiresAtKey,
      value: response.expiresAtUtc.toIso8601String(),
    );
  }

  Future<String?> getAccessToken() async {
    final token = await _storage.read(key: _tokenKey);

    if (token == null || token.isEmpty) {
      return null;
    }

    final expiresAt = await _storage.read(key: _expiresAtKey);

    if (expiresAt != null) {
      final expiryDate = DateTime.tryParse(expiresAt);

      if (expiryDate == null ||
          !expiryDate.isAfter(DateTime.now().toUtc())) {
        await clearSession();
        return null;
      }
    }

    return token;
  }

  Future<bool> isLoggedIn() async {
    return await getAccessToken() != null;
  }

  Future<int?> getUserId() async {
    final value = await _storage.read(key: _userIdKey);
    return int.tryParse(value ?? '');
  }

  Future<String?> getUsername() async {
    return _storage.read(key: _usernameKey);
  }

  Future<String?> getEmail() async {
    return _storage.read(key: _emailKey);
  }

  Future<String?> getCountryCode() async {
    return _storage.read(key: _countryCodeKey);
  }

  Future<void> clearSession() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _userIdKey);
    await _storage.delete(key: _usernameKey);
    await _storage.delete(key: _emailKey);
    await _storage.delete(key: _countryCodeKey);
    await _storage.delete(key: _expiresAtKey);
  }
}
