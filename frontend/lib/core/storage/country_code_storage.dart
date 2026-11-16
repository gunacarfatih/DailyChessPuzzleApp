
import 'package:shared_preferences/shared_preferences.dart';

class CountryCodeStorage {
  static const String _countryCodeKey = 'country_code';
  static const String _countryNameKey = 'country_name';

  /// Kayıtlı ülke kodunu getirir.
  /// Örnek: TR, DE, US
  Future<String?> getCountryCode() async {
    final preferences = await SharedPreferences.getInstance();

    return preferences.getString(_countryCodeKey);
  }

  /// Ülke kodunu kaydeder.
  Future<void> saveCountryCode(String countryCode) async {
    final preferences = await SharedPreferences.getInstance();

    await preferences.setString(
      _countryCodeKey,
      countryCode.toUpperCase(),
    );
  }

  /// Kayıtlı ülke adını getirir.
  Future<String?> getCountryName() async {
    final preferences = await SharedPreferences.getInstance();

    return preferences.getString(_countryNameKey);
  }

  /// Ülke adını kaydeder.
  Future<void> saveCountryName(String countryName) async {
    final preferences = await SharedPreferences.getInstance();

    await preferences.setString(
      _countryNameKey,
      countryName,
    );
  }

  /// Ülke kodunu ve adını birlikte kaydeder.
  Future<void> saveCountryInfo({
    required String countryCode,
    required String countryName,
  }) async {
    final preferences = await SharedPreferences.getInstance();

    await preferences.setString(
      _countryCodeKey,
      countryCode.toUpperCase(),
    );

    await preferences.setString(
      _countryNameKey,
      countryName,
    );
  }

  /// Kayıtlı ülke bilgilerini siler.
  Future<void> clearCountryCode() async {
    final preferences = await SharedPreferences.getInstance();

    await preferences.remove(_countryCodeKey);
    await preferences.remove(_countryNameKey);
  }
}
